using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SubVora.Application.Notifications;

namespace SubVora.Infrastructure.Notifications;

/// <summary>Typed HttpClient over the FCM HTTP v1 API - one client per app instance, same shape as OpenAiEmbeddingClient/ExchangeRateHostClient.</summary>
public class FcmPushNotificationSender : IPushNotificationSender
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly HttpClient _httpClient;
    private readonly ILogger<FcmPushNotificationSender> _logger;
    private readonly string _projectId;
    private readonly ServiceAccount _serviceAccount;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _cachedAccessTokenExpiresAt = DateTimeOffset.MinValue;

    public FcmPushNotificationSender(HttpClient httpClient, IConfiguration configuration, ILogger<FcmPushNotificationSender> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _projectId = configuration["Firebase:ProjectId"]
            ?? throw new InvalidOperationException("Firebase:ProjectId is not configured.");

        var serviceAccountJson = configuration["Firebase:ServiceAccountJson"]
            ?? throw new InvalidOperationException("Firebase:ServiceAccountJson is not configured.");
        _serviceAccount = JsonSerializer.Deserialize<ServiceAccount>(serviceAccountJson)
            ?? throw new InvalidOperationException("Firebase:ServiceAccountJson could not be parsed.");
    }

    public async Task<PushSendResult> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/projects/{_projectId}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new FcmSendRequest(new FcmMessage(deviceToken, new FcmNotification(title, body))));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return PushSendResult.Sent;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (errorBody.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase))
        {
            return PushSendResult.TokenInvalid;
        }

        _logger.LogWarning("FCM send failed for a device token (non-token error): {StatusCode} {Body}", response.StatusCode, errorBody);
        return PushSendResult.Sent;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedAccessTokenExpiresAt)
        {
            return _cachedAccessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedAccessTokenExpiresAt)
            {
                return _cachedAccessToken;
            }

            var assertion = BuildSignedJwtAssertion();

            using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }), cancellationToken);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Firebase OAuth token response could not be parsed.");

            _cachedAccessToken = tokenResponse.AccessToken;
            _cachedAccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private string BuildSignedJwtAssertion()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_serviceAccount.PrivateKey);
        var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim("scope", MessagingScope),
        };

        var token = new JwtSecurityToken(
            issuer: _serviceAccount.ClientEmail,
            audience: TokenEndpoint,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private record ServiceAccount(
        [property: JsonPropertyName("client_email")] string ClientEmail,
        [property: JsonPropertyName("private_key")] string PrivateKey);

    private record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private record FcmSendRequest([property: JsonPropertyName("message")] FcmMessage Message);

    private record FcmMessage(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("notification")] FcmNotification Notification);

    private record FcmNotification(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body);
}

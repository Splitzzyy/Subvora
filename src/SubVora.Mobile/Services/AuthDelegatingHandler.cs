using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Services;

/// <summary>
/// Attaches the stored access token to outbound requests and transparently refreshes it once on a 401.
/// Registered as the primary/attached handler for every Refit client except <see cref="Api.IAuthApi"/>,
/// which must not loop through this handler for its own login/register/refresh calls.
/// </summary>
public class AuthDelegatingHandler : DelegatingHandler
{
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _refreshClient;

    public event EventHandler? SessionExpired;

    /// <param name="refreshClient">
    /// A plain HttpClient (no AuthDelegatingHandler attached) used solely to call the refresh
    /// endpoint, so a 401 during refresh can never recurse back into this handler.
    /// </param>
    public AuthDelegatingHandler(ITokenStore tokenStore, HttpClient refreshClient)
    {
        _tokenStore = tokenStore;
        _refreshClient = refreshClient;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _tokenStore.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var refreshToken = await _tokenStore.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refreshToken))
        {
            await ExpireSessionAsync();
            return response;
        }

        AuthTokenResponse? refreshed = null;
        try
        {
            var refreshResponse = await _refreshClient.PostAsJsonAsync(
                "api/v1/auth/refresh",
                new RefreshRequest { RefreshToken = refreshToken },
                cancellationToken);

            if (refreshResponse.IsSuccessStatusCode)
            {
                refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken: cancellationToken);
            }
        }
        catch (HttpRequestException)
        {
            refreshed = null;
        }

        if (refreshed is null)
        {
            await ExpireSessionAsync();
            return response;
        }

        await _tokenStore.SaveTokensAsync(refreshed);

        var retryRequest = await CloneRequestAsync(request);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private async Task ExpireSessionAsync()
    {
        await _tokenStore.ClearAsync();
        SessionExpired?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

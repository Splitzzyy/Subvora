using System.Net;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>Minimal fake IAuthApi for ViewModel tests - no real HTTP involved.</summary>
public class FakeAuthApi : IAuthApi
{
    public Func<RegisterRequest, Task<IApiResponse>> RegisterHandler =
        _ => Task.FromResult(CreateResponse(HttpStatusCode.Created));

    public Func<LoginRequest, Task<IApiResponse<AuthTokenResponse>>> LoginHandler =
        _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, SampleTokens()));

    public Func<RefreshRequest, Task<IApiResponse<AuthTokenResponse>>> RefreshHandler =
        _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, SampleTokens()));

    public List<RegisterRequest> RegisterCalls { get; } = [];
    public List<LoginRequest> LoginCalls { get; } = [];
    public List<RefreshRequest> LogoutCalls { get; } = [];

    public Task<IApiResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        RegisterCalls.Add(request);
        return RegisterHandler(request);
    }

    public Task<IApiResponse<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        LoginCalls.Add(request);
        return LoginHandler(request);
    }

    public Task<IApiResponse<AuthTokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default) =>
        RefreshHandler(request);

    public Task<IApiResponse> LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        LogoutCalls.Add(request);
        return Task.FromResult(CreateResponse(HttpStatusCode.NoContent));
    }

    public static AuthTokenResponse SampleTokens() => new()
    {
        AccessToken = "sample-access-token",
        RefreshToken = "sample-refresh-token",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
    };

    public static IApiResponse CreateResponse(HttpStatusCode statusCode, string? validationErrorJson = null)
    {
        var httpResponse = BuildHttpResponse(statusCode, validationErrorJson);
        var settings = new RefitSettings();

        if (validationErrorJson is null)
        {
            return new ApiResponse<object>(httpResponse, null, settings);
        }

        var exception = BuildApiException(httpResponse, settings);
        return new ApiResponse<object>(httpResponse, null, settings, exception);
    }

    public static IApiResponse<AuthTokenResponse> CreateResponse(HttpStatusCode statusCode, AuthTokenResponse? content, string? validationErrorJson = null)
    {
        var httpResponse = BuildHttpResponse(statusCode, validationErrorJson);
        var settings = new RefitSettings();

        if (validationErrorJson is null)
        {
            return new ApiResponse<AuthTokenResponse>(httpResponse, content, settings);
        }

        var exception = BuildApiException(httpResponse, settings);
        return new ApiResponse<AuthTokenResponse>(httpResponse, content, settings, exception);
    }

    private static HttpResponseMessage BuildHttpResponse(HttpStatusCode statusCode, string? validationErrorJson)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://test.local/"),
        };
        if (validationErrorJson is not null)
        {
            response.Content = new StringContent(validationErrorJson, System.Text.Encoding.UTF8, "application/json");
        }

        return response;
    }

    private static ApiException BuildApiException(HttpResponseMessage response, RefitSettings settings) =>
        ApiException.Create(
            new HttpRequestMessage(HttpMethod.Post, "https://test.local/"),
            HttpMethod.Post,
            response,
            settings).GetAwaiter().GetResult();
}

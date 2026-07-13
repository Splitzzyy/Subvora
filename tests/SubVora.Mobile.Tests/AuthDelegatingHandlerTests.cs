using System.Net;
using System.Net.Http.Json;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests;

public class AuthDelegatingHandlerTests
{
    private sealed class FakeTokenStore : ITokenStore
    {
        public string? AccessToken;
        public string? RefreshToken;
        public bool Cleared;

        public Task<string?> GetAccessTokenAsync() => Task.FromResult(AccessToken);

        public Task<string?> GetRefreshTokenAsync() => Task.FromResult(RefreshToken);

        public Task SaveTokensAsync(AuthTokenResponse tokens)
        {
            AccessToken = tokens.AccessToken;
            RefreshToken = tokens.RefreshToken;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            Cleared = true;
            AccessToken = null;
            RefreshToken = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInnerHandler : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private static (AuthDelegatingHandler Handler, FakeInnerHandler Inner, FakeTokenStore TokenStore) CreateHandler()
    {
        var inner = new FakeInnerHandler();
        var tokenStore = new FakeTokenStore();
        var refreshClient = new HttpClient(inner) { BaseAddress = new Uri("https://test.local/") };
        var handler = new AuthDelegatingHandler(tokenStore, refreshClient) { InnerHandler = inner };
        return (handler, inner, tokenStore);
    }

    [Fact]
    public async Task SendAsync_WithoutStoredToken_SendsNoAuthorizationHeader()
    {
        var (handler, inner, _) = CreateHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };

        await client.GetAsync("api/v1/subscriptions");

        Assert.Null(inner.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_WithStoredToken_AttachesBearerHeader()
    {
        var (handler, inner, tokenStore) = CreateHandler();
        tokenStore.AccessToken = "stored-access-token";
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };

        await client.GetAsync("api/v1/subscriptions");

        Assert.Equal("Bearer", inner.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("stored-access-token", inner.Requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_On401_RefreshesOnceAndRetriesOriginalRequest()
    {
        var (handler, inner, tokenStore) = CreateHandler();
        tokenStore.AccessToken = "expired-access-token";
        tokenStore.RefreshToken = "valid-refresh-token";

        var newTokens = new AuthTokenResponse
        {
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };

        inner.Respond = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(newTokens) };
            }

            var isRetry = request.Headers.Authorization?.Parameter == "new-access-token";
            return new HttpResponseMessage(isRetry ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        };

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var response = await client.GetAsync("api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("new-access-token", tokenStore.AccessToken);
        Assert.False(tokenStore.Cleared);

        var refreshCalls = inner.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"));
        Assert.Equal(1, refreshCalls);

        var originalRequestAttempts = inner.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions"));
        Assert.Equal(2, originalRequestAttempts);
    }

    [Fact]
    public async Task SendAsync_WhenRefreshItselfFails_ClearsTokenStoreAndDoesNotRetryIndefinitely()
    {
        var (handler, inner, tokenStore) = CreateHandler();
        tokenStore.AccessToken = "expired-access-token";
        tokenStore.RefreshToken = "also-invalid-refresh-token";

        inner.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/auth/refresh")
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var response = await client.GetAsync("api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(tokenStore.Cleared);

        var refreshCalls = inner.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"));
        Assert.Equal(1, refreshCalls);

        var originalRequestAttempts = inner.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions"));
        Assert.Equal(1, originalRequestAttempts);
    }

    [Fact]
    public async Task SendAsync_On401_WithNoStoredRefreshToken_ClearsTokenStoreWithoutCallingRefresh()
    {
        var (handler, inner, tokenStore) = CreateHandler();
        tokenStore.AccessToken = "expired-access-token";
        tokenStore.RefreshToken = null;

        inner.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var response = await client.GetAsync("api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(tokenStore.Cleared);
        Assert.DoesNotContain(inner.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"));
    }
}

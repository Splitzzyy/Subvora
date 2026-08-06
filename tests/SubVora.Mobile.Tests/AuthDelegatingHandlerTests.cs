using System.Net;
using System.Net.Http.Json;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Services;
using SubVora.Mobile.Tests.Fakes;

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
        private readonly List<HttpRequestMessage> _requests = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        /// <summary>Optional hook to hold a request mid-flight, so concurrency can be forced rather than hoped for.</summary>
        public Func<HttpRequestMessage, Task>? OnRequest;

        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get { lock (_requests) { return [.. _requests]; } }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }

            if (OnRequest is not null)
            {
                await OnRequest(request);
            }

            return Respond(request);
        }
    }

    private static (AuthDelegatingHandler Handler, FakeInnerHandler Inner, FakeTokenStore TokenStore, FakeLocalCacheService Cache) CreateHandler()
    {
        var inner = new FakeInnerHandler();
        var tokenStore = new FakeTokenStore();
        var cache = new FakeLocalCacheService();
        var refreshClient = new HttpClient(inner) { BaseAddress = new Uri("https://test.local/") };
        var handler = new AuthDelegatingHandler(tokenStore, refreshClient, cache) { InnerHandler = inner };
        return (handler, inner, tokenStore, cache);
    }

    [Fact]
    public async Task SendAsync_WithoutStoredToken_SendsNoAuthorizationHeader()
    {
        var (handler, inner, _, _) = CreateHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };

        await client.GetAsync("api/v1/subscriptions");

        Assert.Null(inner.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_WithStoredToken_AttachesBearerHeader()
    {
        var (handler, inner, tokenStore, _) = CreateHandler();
        tokenStore.AccessToken = "stored-access-token";
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };

        await client.GetAsync("api/v1/subscriptions");

        Assert.Equal("Bearer", inner.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("stored-access-token", inner.Requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_On401_RefreshesOnceAndRetriesOriginalRequest()
    {
        var (handler, inner, tokenStore, _) = CreateHandler();
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
        var (handler, inner, tokenStore, _) = CreateHandler();
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
        var (handler, inner, tokenStore, _) = CreateHandler();
        tokenStore.AccessToken = "expired-access-token";
        tokenStore.RefreshToken = null;

        inner.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var response = await client.GetAsync("api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(tokenStore.Cleared);
        Assert.DoesNotContain(inner.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"));
    }

    [Fact]
    public async Task SendAsync_WithConcurrent401s_RefreshesOnceAndRetriesEveryRequest()
    {
        const int concurrentRequests = 5;

        var (handler, inner, tokenStore, _) = CreateHandler();
        tokenStore.AccessToken = "expired-access-token";
        tokenStore.RefreshToken = "valid-refresh-token";

        var newTokens = new AuthTokenResponse
        {
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };

        // Hold every first attempt until all of them have reached the server, so the overlap that
        // makes the backend read a replayed refresh token as theft is forced, not merely likely.
        var allRequestsArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivedCount = 0;

        inner.OnRequest = async request =>
        {
            var isFirstAttempt = !request.RequestUri!.AbsolutePath.EndsWith("/auth/refresh")
                && request.Headers.Authorization?.Parameter == "expired-access-token";
            if (!isFirstAttempt)
            {
                return;
            }

            if (Interlocked.Increment(ref arrivedCount) == concurrentRequests)
            {
                allRequestsArrived.SetResult();
            }

            await allRequestsArrived.Task;
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
        var responses = await Task.WhenAll(Enumerable
            .Range(0, concurrentRequests)
            .Select(_ => client.GetAsync("api/v1/subscriptions")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, inner.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/auth/refresh")));
        Assert.False(tokenStore.Cleared);
        Assert.Equal("new-access-token", tokenStore.AccessToken);
    }

    [Fact]
    public async Task SendAsync_WhenSessionExpires_ClearsTheLocalCacheAsWellAsTheTokens()
    {
        var (handler, inner, tokenStore, cache) = CreateHandler();
        tokenStore.AccessToken = "expired-access-token";
        tokenStore.RefreshToken = "revoked-refresh-token";
        await cache.UpsertAsync(new CachedBurnRate { Monthly = 42m, HomeCurrency = "USD" });
        await cache.UpsertAsync(new CachedSubscription { Id = Guid.NewGuid(), CustomName = "Netflix" });

        inner.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        await client.GetAsync("api/v1/subscriptions");

        Assert.True(tokenStore.Cleared);
        Assert.Empty(await cache.GetAllAsync<CachedBurnRate>());
        Assert.Empty(await cache.GetAllAsync<CachedSubscription>());
    }
}

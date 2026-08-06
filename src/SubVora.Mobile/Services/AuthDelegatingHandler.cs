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
    private readonly ILocalCacheService _localCacheService;

    // Single-flights the refresh. The server rotates refresh tokens and treats a replayed one as
    // theft (AuthService.RefreshAsync revokes the whole chain), so two requests refreshing off the
    // same stored token would sign the user out. One refreshes; the rest wait and reuse its result.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public event EventHandler? SessionExpired;

    /// <param name="refreshClient">
    /// A plain HttpClient (no AuthDelegatingHandler attached) used solely to call the refresh
    /// endpoint, so a 401 during refresh can never recurse back into this handler.
    /// </param>
    public AuthDelegatingHandler(ITokenStore tokenStore, HttpClient refreshClient, ILocalCacheService localCacheService)
    {
        _tokenStore = tokenStore;
        _refreshClient = refreshClient;
        _localCacheService = localCacheService;
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

        var freshAccessToken = await RefreshOnceAsync(accessToken, cancellationToken);
        if (freshAccessToken is null)
        {
            return response;
        }

        response.Dispose();

        var retryRequest = await CloneRequestAsync(request);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshAccessToken);
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    /// <summary>
    /// Returns the access token to retry with, or null when the session is over. Only the first
    /// caller to reach the lock with a given stale token actually calls /auth/refresh; anyone who
    /// arrives after it finished picks up whatever it stored instead of refreshing again.
    /// </summary>
    private async Task<string?> RefreshOnceAsync(string? staleAccessToken, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var storedAccessToken = await _tokenStore.GetAccessTokenAsync();
            if (storedAccessToken != staleAccessToken)
            {
                // Another request already handled this expiry while we waited. A token means it
                // refreshed and we retry with that one; none means it ended the session and
                // already raised SessionExpired, so this request just returns its 401.
                return string.IsNullOrEmpty(storedAccessToken) ? null : storedAccessToken;
            }

            var refreshToken = await _tokenStore.GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken))
            {
                await ExpireSessionAsync();
                return null;
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
                return null;
            }

            await _tokenStore.SaveTokensAsync(refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Ends the session exactly as an explicit sign-out does. The SQLite mirror holds the previous
    /// account's subscriptions and totals, so it has to go with the tokens - otherwise the next
    /// person to open the app offline is shown them.
    /// </summary>
    private async Task ExpireSessionAsync()
    {
        await _tokenStore.ClearAsync();
        await _localCacheService.ClearAllAsync();
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

using System.Net.Http.Json;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Services;

/// <summary>
/// Owns the parts of the session that every API client has to share: the single-flight refresh and
/// the signal that the session is over.
/// <para>
/// Split out from <see cref="AuthDelegatingHandler"/> because HttpClientFactory assigns
/// <c>InnerHandler</c> on each DelegatingHandler it builds a pipeline around, and refuses an
/// instance that already has one. A single handler shared by every Refit client therefore throws
/// the moment a second client is created. The handlers must be transient; this must not be, or the
/// refresh lock stops being one lock.
/// </para>
/// </summary>
public class SessionRefresher
{
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _refreshClient;
    private readonly ILocalCacheService _localCacheService;

    // The server rotates refresh tokens and treats a replayed one as theft (AuthService.RefreshAsync
    // revokes the whole chain), so two requests refreshing off the same stored token would sign the
    // user out. One refreshes; the rest wait and reuse its result.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public event EventHandler? SessionExpired;

    /// <param name="refreshClient">
    /// A plain HttpClient (no AuthDelegatingHandler attached) used solely to call the refresh
    /// endpoint, so a 401 during refresh can never recurse back into the handler.
    /// </param>
    public SessionRefresher(ITokenStore tokenStore, HttpClient refreshClient, ILocalCacheService localCacheService)
    {
        _tokenStore = tokenStore;
        _refreshClient = refreshClient;
        _localCacheService = localCacheService;
    }

    /// <summary>
    /// Returns the access token to retry with, or null when the session is over. Only the first
    /// caller to reach the lock with a given stale token actually calls /auth/refresh; anyone who
    /// arrives after it finished picks up whatever it stored instead of refreshing again.
    /// </summary>
    public async Task<string?> RefreshOnceAsync(string? staleAccessToken, CancellationToken cancellationToken)
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
}

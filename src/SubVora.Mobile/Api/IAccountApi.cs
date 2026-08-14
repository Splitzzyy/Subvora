using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

/// <summary>
/// The auth endpoints that require a bearer token, split out from <see cref="IAuthApi"/> so they can
/// be registered with <c>AuthDelegatingHandler</c> attached.
/// <para>
/// The split exists because <see cref="IAuthApi"/> must <em>not</em> chain that handler - it carries
/// <c>/auth/refresh</c>, and a 401 during refresh would recurse straight back into refresh. Leaving
/// these two calls there meant they went out with no <c>Authorization</c> header at all, against
/// endpoints the API marks <c>[Authorize]</c>: change-password answered 401 every time and was
/// reported to the user as an expired session, and logout's revoke silently never happened while the
/// client cleared its tokens and moved on.
/// </para>
/// <para>
/// Anything added here must be an endpoint that requires authentication and is not itself part of
/// the refresh path. <c>RefitClientCompositionTests</c> asserts this interface is registered with the
/// handler and that <see cref="IAuthApi"/> is not.
/// </para>
/// </summary>
public interface IAccountApi
{
    /// <summary>
    /// Changes the signed-in user's password. Returns a fresh token pair, because succeeding
    /// revokes every refresh token the account holds - including this device's.
    /// </summary>
    [Post("/api/v1/auth/change-password")]
    Task<IApiResponse<AuthTokenResponse>> ChangePasswordAsync([Body] ChangePasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the caller's refresh token, ending that session. Always 204 when it lands - the
    /// server is deliberately quiet about whether the presented token existed, belonged to someone
    /// else, or was already revoked.
    /// </summary>
    [Post("/api/v1/auth/logout")]
    Task<IApiResponse> LogoutAsync([Body] RefreshRequest request, CancellationToken cancellationToken = default);
}

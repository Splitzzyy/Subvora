using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

/// <summary>
/// The auth endpoints that take no bearer token. Registered <em>without</em>
/// <c>AuthDelegatingHandler</c> on purpose: this interface carries <c>/auth/refresh</c>, and chaining
/// the handler here would let a 401 during refresh recurse back into refresh.
/// <para>
/// An endpoint that requires authentication does not belong here - it goes on
/// <see cref="IAccountApi"/>, which is registered with the handler attached. Adding one here would
/// ship a call with no <c>Authorization</c> header against an <c>[Authorize]</c> endpoint, which is
/// exactly how change-password and logout came to be silently broken.
/// </para>
/// </summary>
public interface IAuthApi
{
    [Post("/api/v1/auth/register")]
    Task<IApiResponse> RegisterAsync([Body] RegisterRequest request, CancellationToken cancellationToken = default);

    [Post("/api/v1/auth/login")]
    Task<IApiResponse<AuthTokenResponse>> LoginAsync([Body] LoginRequest request, CancellationToken cancellationToken = default);

    [Post("/api/v1/auth/refresh")]
    Task<IApiResponse<AuthTokenResponse>> RefreshAsync([Body] RefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a reset code. Answers 200 whether or not the address has an account - the server
    /// will not say which, so the client must not imply it either.
    /// </summary>
    [Post("/api/v1/auth/forgot-password")]
    Task<IApiResponse> ForgotPasswordAsync([Body] ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a new password from a code. 400 covers wrong, expired, already used and over the
    /// attempt limit, all with the same message - deliberately, so guessing learns nothing.
    /// </summary>
    [Post("/api/v1/auth/reset-password")]
    Task<IApiResponse> ResetPasswordAsync([Body] ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

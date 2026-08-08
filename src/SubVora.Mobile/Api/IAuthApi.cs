using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface IAuthApi
{
    [Post("/api/v1/auth/register")]
    Task<IApiResponse> RegisterAsync([Body] RegisterRequest request, CancellationToken cancellationToken = default);

    [Post("/api/v1/auth/login")]
    Task<IApiResponse<AuthTokenResponse>> LoginAsync([Body] LoginRequest request, CancellationToken cancellationToken = default);

    [Post("/api/v1/auth/refresh")]
    Task<IApiResponse<AuthTokenResponse>> RefreshAsync([Body] RefreshRequest request, CancellationToken cancellationToken = default);

    [Post("/api/v1/auth/logout")]
    Task<IApiResponse> LogoutAsync([Body] RefreshRequest request, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Changes the signed-in user's password. Returns a fresh token pair, because succeeding
    /// revokes every refresh token the account holds - including this device's.
    /// </summary>
    [Post("/api/v1/auth/change-password")]
    Task<IApiResponse<AuthTokenResponse>> ChangePasswordAsync([Body] ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

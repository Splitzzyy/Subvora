namespace SubVora.Application.Auth;

public interface IAuthService
{
    /// <summary>
    /// Creates the account, or - when the address is already registered - emails the existing
    /// owner and returns as if nothing happened. There is deliberately no result to inspect:
    /// telling the caller which of those occurred is what makes register an enumeration oracle.
    /// </summary>
    Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<RefreshResult> RefreshAsync(string presentedRefreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(Guid userId, string presentedRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>Always completes successfully regardless of whether the email matches an account - callers must never be able to tell the two cases apart (no account enumeration).</summary>
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<ResetPasswordResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the password of an already-authenticated user, after checking they know the current
    /// one. Revokes every refresh token the account has - the same eviction a reset performs, for
    /// the same reason - and returns a fresh pair so the caller's own device stays signed in.
    /// </summary>
    Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

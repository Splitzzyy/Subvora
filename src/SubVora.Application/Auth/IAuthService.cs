namespace SubVora.Application.Auth;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<RefreshResult> RefreshAsync(string presentedRefreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(Guid userId, string presentedRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>Always completes successfully regardless of whether the email matches an account - callers must never be able to tell the two cases apart (no account enumeration).</summary>
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<ResetPasswordResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

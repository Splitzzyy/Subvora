namespace SubVora.Application.Auth;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<RefreshResult> RefreshAsync(string presentedRefreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(Guid userId, string presentedRefreshToken, CancellationToken cancellationToken = default);
}

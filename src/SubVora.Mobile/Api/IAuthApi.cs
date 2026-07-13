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
}

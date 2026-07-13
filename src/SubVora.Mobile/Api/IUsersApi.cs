using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface IUsersApi
{
    [Get("/api/v1/users/me")]
    Task<UserProfileDto> GetMeAsync(CancellationToken cancellationToken = default);

    [Put("/api/v1/users/me")]
    Task<UserProfileDto> UpdateMeAsync([Body] UpdateUserProfileRequest request, CancellationToken cancellationToken = default);
}

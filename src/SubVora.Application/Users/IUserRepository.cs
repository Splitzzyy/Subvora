namespace SubVora.Application.Users;

public interface IUserRepository
{
    Task<string?> GetPreferredCurrencyAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default);
}

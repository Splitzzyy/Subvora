using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeUsersApi : IUsersApi
{
    public Func<Task<UserProfileDto>> GetMeHandler =
        () => Task.FromResult(new UserProfileDto { Id = Guid.NewGuid(), Email = "user@example.com", PreferredCurrency = "USD" });

    public Func<UpdateUserProfileRequest, Task<UserProfileDto>> UpdateMeHandler =
        request => Task.FromResult(new UserProfileDto
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PreferredCurrency = request.PreferredCurrency,
            DefaultAlertDaysAdvance = request.DefaultAlertDaysAdvance,
        });

    public List<UpdateUserProfileRequest> UpdateMeCalls { get; } = [];

    public Task<UserProfileDto> GetMeAsync(CancellationToken cancellationToken = default) => GetMeHandler();

    public Task<UserProfileDto> UpdateMeAsync(UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        UpdateMeCalls.Add(request);
        return UpdateMeHandler(request);
    }
}

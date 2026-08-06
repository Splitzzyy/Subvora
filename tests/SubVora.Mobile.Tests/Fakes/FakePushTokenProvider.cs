using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

public class FakePushTokenProvider : IPushTokenProvider
{
    public string Platform { get; set; } = "Android";

    public Func<Task<string?>> GetTokenHandler = () => Task.FromResult<string?>("fcm-registration-token");

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) => GetTokenHandler();
}

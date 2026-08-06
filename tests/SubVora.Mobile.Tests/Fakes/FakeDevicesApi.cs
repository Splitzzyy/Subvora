using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeDevicesApi : IDevicesApi
{
    public Func<RegisterDeviceTokenRequest, Task> RegisterHandler = _ => Task.CompletedTask;

    public List<RegisterDeviceTokenRequest> RegisterCalls { get; } = [];

    public Task RegisterAsync(RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default)
    {
        RegisterCalls.Add(request);
        return RegisterHandler(request);
    }
}

namespace SubVora.Application.Devices;

public interface IDeviceTokenRepository
{
    Task<DeviceTokenDto> UpsertAsync(Guid userId, RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default);
}

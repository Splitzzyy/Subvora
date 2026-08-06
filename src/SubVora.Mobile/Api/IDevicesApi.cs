using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface IDevicesApi
{
    // The response body (the stored device token row) is of no use to the client, so it
    // is discarded - a non-success status surfaces as an ApiException the caller swallows.
    [Post("/api/v1/devices")]
    Task RegisterAsync([Body] RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default);
}

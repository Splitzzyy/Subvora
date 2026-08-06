using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Notifications;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeRenewalNotificationScheduler : IRenewalNotificationScheduler
{
    public bool PermissionGranted { get; set; } = true;

    public int PermissionRequests { get; private set; }

    /// <summary>Each sync's subscription list, snapshotted - the caller passes a live collection it then mutates.</summary>
    public List<List<SubscriptionDto>> SyncCalls { get; } = [];

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        PermissionRequests++;
        return Task.FromResult(PermissionGranted);
    }

    public Task SyncAsync(IEnumerable<SubscriptionDto> subscriptions, CancellationToken cancellationToken = default)
    {
        SyncCalls.Add([.. subscriptions]);
        return Task.CompletedTask;
    }
}

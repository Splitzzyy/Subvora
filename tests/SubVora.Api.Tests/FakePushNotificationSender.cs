using SubVora.Application.Notifications;

namespace SubVora.Api.Tests;

/// <summary>No-op stand-in for FcmPushNotificationSender - only needed so RenewalAlertBackgroundService's eager DI resolution at host startup doesn't require real Firebase config in Api.Tests.</summary>
public class FakePushNotificationSender : IPushNotificationSender
{
    public Task<PushSendResult> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default) =>
        Task.FromResult(PushSendResult.Sent);
}

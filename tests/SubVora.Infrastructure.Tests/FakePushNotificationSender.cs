using System.Collections.Concurrent;
using SubVora.Application.Notifications;

namespace SubVora.Infrastructure.Tests;

/// <summary>Deterministic stand-in for FcmPushNotificationSender - Infrastructure.Tests never dial out to FCM.</summary>
public class FakePushNotificationSender : IPushNotificationSender
{
    public record SentPush(string DeviceToken, string Title, string Body);

    public ConcurrentBag<SentPush> SentPushes { get; } = new();

    /// <summary>Tokens in this set return TokenInvalid instead of Sent, simulating an FCM "unregistered" response.</summary>
    public HashSet<string> InvalidTokens { get; } = new();

    public Task<PushSendResult> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        SentPushes.Add(new SentPush(deviceToken, title, body));
        return Task.FromResult(InvalidTokens.Contains(deviceToken) ? PushSendResult.TokenInvalid : PushSendResult.Sent);
    }
}

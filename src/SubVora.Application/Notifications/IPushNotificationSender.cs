namespace SubVora.Application.Notifications;

public enum PushSendResult
{
    Sent,
    TokenInvalid,
}

public interface IPushNotificationSender
{
    Task<PushSendResult> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default);
}

using SubVora.Domain.Entities;

namespace SubVora.Application.Alerts;

public interface IRenewalAlertScanner
{
    /// <summary>
    /// Returns the active subscriptions renewing exactly <c>alert_days_advance</c> days from
    /// <paramref name="today"/> that don't already have a notifications_log row for today.
    /// </summary>
    IReadOnlyList<UserSubscription> Scan(DateOnly today, IEnumerable<UserSubscription> activeSubscriptions, IEnumerable<NotificationLog> existingLogsForToday);
}

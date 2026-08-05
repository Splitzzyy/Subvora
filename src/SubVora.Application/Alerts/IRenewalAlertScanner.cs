using SubVora.Domain.Entities;

namespace SubVora.Application.Alerts;

public interface IRenewalAlertScanner
{
    /// <summary>
    /// Returns the active subscriptions renewing exactly <c>alert_days_advance</c> days from
    /// <paramref name="today"/> that don't already have a notifications_log row for today.
    /// </summary>
    IReadOnlyList<UserSubscription> Scan(DateOnly today, IEnumerable<UserSubscription> activeSubscriptions, IEnumerable<NotificationLog> existingLogsForToday);

    /// <summary>
    /// Returns the active subscriptions whose <c>next_billing_date</c> is already in the past and
    /// therefore needs rolling forward. Deliberately separate from <see cref="Scan"/>: "due to
    /// alert" and "due to advance" are different predicates over the same data.
    /// </summary>
    IReadOnlyList<UserSubscription> FindDueForAdvance(DateOnly today, IEnumerable<UserSubscription> activeSubscriptions);
}

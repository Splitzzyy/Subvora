using SubVora.Domain.Entities;

namespace SubVora.Application.Alerts;

/// <summary>
/// Pure logic over already-fetched subscriptions/logs - same "no EF in Application" pattern as
/// <c>BurnRateCalculator</c>. The idempotency guard is (UserSubscriptionId, AlertDaysAdvance)
/// among today's log rows, not a DB-level constraint - notifications_log's unique index also
/// covers SentAt, which is a fresh timestamp on every insert and can't dedupe re-runs by itself.
/// </summary>
public class RenewalAlertScanner : IRenewalAlertScanner
{
    public IReadOnlyList<UserSubscription> Scan(DateOnly today, IEnumerable<UserSubscription> activeSubscriptions, IEnumerable<NotificationLog> existingLogsForToday)
    {
        var alreadyNotified = existingLogsForToday
            .Select(log => (log.UserSubscriptionId, log.AlertDaysAdvance))
            .ToHashSet();

        return activeSubscriptions
            .Where(s => s.IsActive)
            .Where(s => s.NextBillingDate.AddDays(-s.AlertDaysAdvance) == today)
            .Where(s => !alreadyNotified.Contains((s.Id, s.AlertDaysAdvance)))
            .ToList();
    }
}

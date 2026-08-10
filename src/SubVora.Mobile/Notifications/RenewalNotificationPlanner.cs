using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Formatting;

namespace SubVora.Mobile.Notifications;

/// <summary>
/// Turns the subscription list into the set of reminders the OS should hold. Pure - no platform, no
/// clock of its own - so the rules that decide whether a user is reminded are unit-testable, which
/// the platform scheduling call around it is not.
/// </summary>
public static class RenewalNotificationPlanner
{
    /// <summary>
    /// iOS keeps at most 64 pending local notifications per app and silently drops the rest, so the
    /// cap is enforced here rather than discovered on a device. One reminder per subscription means
    /// this only bites past 64 tracked subscriptions, and the nearest dates are the ones kept.
    /// </summary>
    public const int MaxPendingNotifications = 64;

    /// <summary>Late enough to be awake, early enough to act before a charge lands during the day.</summary>
    private static readonly TimeOnly NotifyAtTimeOfDay = new(9, 0);

    public static IReadOnlyList<PlannedRenewalNotification> Plan(IEnumerable<SubscriptionDto> subscriptions, DateTime nowLocal)
    {
        return subscriptions
            .Where(subscription => subscription.IsActive)
            .Select(subscription => new
            {
                Subscription = subscription,
                // alert_days_advance is the user's lead time. It is applied here, on the device:
                // reminders are derived from the subscription list and handed to the OS, and there
                // is no server-side scan and no push service behind them.
                NotifyAt = subscription.NextBillingDate
                    .AddDays(-subscription.AlertDaysAdvance)
                    .ToDateTime(NotifyAtTimeOfDay),
            })
            // A reminder in the past is one the OS would either fire immediately or reject. Both are
            // wrong: the charge has already happened or is happening today.
            .Where(item => item.NotifyAt > nowLocal)
            .OrderBy(item => item.NotifyAt)
            .ThenBy(item => item.Subscription.CustomName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxPendingNotifications)
            .Select((item, index) => new PlannedRenewalNotification(
                Id: index + 1,
                Title: item.Subscription.CycleCadence == BillingCycleType.OneTime
                    ? "Payment due soon"
                    : "Subscription renewing soon",
                Body: BuildBody(item.Subscription),
                NotifyAt: item.NotifyAt))
            .ToList();
    }

    private static string BuildBody(SubscriptionDto subscription)
    {
        var days = subscription.AlertDaysAdvance;
        var action = subscription.CycleCadence == BillingCycleType.OneTime
            ? "is due"
            : "renews";
        var when = days switch
        {
            <= 0 => $"{action} today",
            1 => $"{action} tomorrow",
            _ => $"{action} in {days} days",
        };

        // The amount is the point of the reminder - "Netflix renews in 3 days" is a fact,
        // "Netflix renews in 3 days - ₹1,699.50" is a decision. Symbol rather than the trailing
        // code: a notification is read at a glance on a lock screen, where "₹1,699.50" lands and
        // "1,699.50 INR" has to be parsed. Codes without an unambiguous symbol still print as codes.
        return $"{subscription.CustomName} {when} - {CurrencySymbols.Format(subscription.CostAmount, subscription.Currency)}";
    }
}

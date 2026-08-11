using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Billing;

/// <summary>
/// Works out when a subscription's current billing period ends, so the add/edit form can fill the
/// next billing date in instead of asking the user to do calendar arithmetic.
/// </summary>
public static class BillingCycleAdvancer
{
    /// <summary>
    /// One billing period after the purchase date - the end of the period the purchase started.
    /// <para>
    /// Deliberately not "the next occurrence after today". A subscription bought on 23 Apr 2025 and
    /// billed yearly has its period end on 23 Apr 2026, and that is what the form shows, even though
    /// the date has since passed. Skipping ahead to 2027 hides the period the user actually entered
    /// and reads as the app having got the date wrong.
    /// </para>
    /// <para>
    /// A date that has passed is left alone on purpose, here and on the server. That is how the app
    /// says a charge is outstanding - it is what <c>SubscriptionDto.IsOverdue</c> reads - and it
    /// moves only when the user marks the charge paid
    /// (<c>POST /api/v1/subscriptions/{id}/mark-paid</c>), which steps on one cycle from the date
    /// just settled. Nothing advances it on a timer; a job that did would erase the signal overdue
    /// depends on.
    /// </para>
    /// <c>OneTime</c> never recurs, so it is returned unchanged.
    /// </summary>
    public static DateTime NextBillingDate(DateTime purchaseDate, BillingCycleType cadence)
    {
        var start = purchaseDate.Date;

        return cadence switch
        {
            BillingCycleType.Weekly => start.AddDays(7),
            BillingCycleType.Monthly => start.AddMonths(1),
            BillingCycleType.Quarterly => start.AddMonths(3),
            BillingCycleType.Yearly => start.AddYears(1),
            BillingCycleType.OneTime => start,
            _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unhandled billing cycle cadence."),
        };
    }
}

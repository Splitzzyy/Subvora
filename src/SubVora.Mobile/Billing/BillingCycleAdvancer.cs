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
    /// Keeping a passed date honest is the server's job, not this one:
    /// <c>BillingDateAdvanceBackgroundService</c> rolls <c>next_billing_date</c> forward a cycle at a
    /// time once it is in the past, using <c>SubVora.Application.Billing.BillingCycleAdvancer</c>.
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
            BillingCycleType.Yearly => start.AddYears(1),
            BillingCycleType.OneTime => start,
            _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unhandled billing cycle cadence."),
        };
    }
}

using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Billing;

/// <summary>
/// Works out the next date a subscription will bill, so the add/edit form can fill it in instead of
/// asking the user to do calendar arithmetic.
/// <para>
/// Deliberately mirrors <c>SubVora.Application.Billing.BillingCycleAdvancer</c>, which the server's
/// <c>BillingDateAdvanceBackgroundService</c> uses to roll passed dates forward. There is no shared
/// project between client and server by design (see CLAUDE.md), so the two must be kept in step by
/// hand - if the stepping rule changes on one side, change it on the other.
/// </para>
/// </summary>
public static class BillingCycleAdvancer
{
    /// <summary>
    /// Returns the first billing date on or after <paramref name="today"/>, starting from
    /// <paramref name="purchaseDate"/> and stepping one cycle at a time.
    /// A purchase date in the future bills on that date. <c>OneTime</c> never recurs, so it bills
    /// on its purchase date and is returned unchanged.
    /// </summary>
    public static DateTime NextBillingDate(DateTime purchaseDate, BillingCycleType cadence, DateTime today)
    {
        if (cadence == BillingCycleType.OneTime)
        {
            return purchaseDate;
        }

        // Stepping one cycle at a time (rather than computing the cycle count) is what keeps
        // month-end and leap-day behaviour anchored to the original day-of-month: AddMonths/AddYears
        // clamp Jan 31 -> Feb 28, and stepping from the clamped date onward is the same sequence a
        // billing provider would charge on.
        var next = purchaseDate.Date;
        var cutoff = today.Date;
        while (next <= cutoff)
        {
            next = cadence switch
            {
                BillingCycleType.Weekly => next.AddDays(7),
                BillingCycleType.Monthly => next.AddMonths(1),
                BillingCycleType.Yearly => next.AddYears(1),
                _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unhandled billing cycle cadence."),
            };
        }

        return next;
    }
}

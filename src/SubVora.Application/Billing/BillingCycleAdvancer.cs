using SubVora.Domain.Enums;

namespace SubVora.Application.Billing;

/// <summary>
/// Pure date arithmetic for rolling a passed <c>next_billing_date</c> forward - same "no EF in
/// Application" pattern as <c>BurnRateCalculator</c>. Driven purely by comparing the stored date
/// against today, never by a counter, so re-running it on the same day is a no-op.
/// </summary>
public static class BillingCycleAdvancer
{
    /// <summary>
    /// Returns the first occurrence of <paramref name="current"/>'s cadence strictly after
    /// <paramref name="today"/>. A date several cycles stale lands in the future, not one cycle
    /// forward. <c>OneTime</c> never recurs, so it is returned unchanged.
    /// </summary>
    public static DateOnly AdvanceTo(DateOnly current, BillingCycleType cadence, DateOnly today)
    {
        if (cadence == BillingCycleType.OneTime)
        {
            return current;
        }

        // Stepping one cycle at a time (rather than computing the cycle count) is what keeps
        // month-end and leap-day behaviour anchored to the original day-of-month: DateOnly's
        // AddMonths/AddYears clamp Jan 31 -> Feb 28, and stepping from the clamped date onward
        // is the same sequence a billing provider would charge on.
        var next = current;
        while (next <= today)
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

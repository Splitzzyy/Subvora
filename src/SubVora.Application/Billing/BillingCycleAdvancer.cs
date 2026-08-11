using SubVora.Domain.Enums;

namespace SubVora.Application.Billing;

/// <summary>
/// Pure date arithmetic for moving a subscription on to its next billing date - same "no EF in
/// Application" pattern as <c>BurnRateCalculator</c>.
/// <para>
/// Only ever called when a user marks a charge paid. Nothing advances a billing date on a timer any
/// more: a date left in the past is the signal that a charge is outstanding, and a background job
/// that quietly moved it would erase exactly that signal.
/// </para>
/// </summary>
public static class BillingCycleAdvancer
{
    /// <summary>
    /// One cycle on from <paramref name="current"/>. <c>OneTime</c> never recurs, so it is returned
    /// unchanged - callers end the subscription instead.
    /// </summary>
    public static DateOnly AdvanceOneCycle(DateOnly current, BillingCycleType cadence) => cadence switch
    {
        // AddMonths/AddYears clamp rather than overflow (31 Jan -> 28 Feb), which is the sequence a
        // billing provider charges on.
        BillingCycleType.Weekly => current.AddDays(7),
        BillingCycleType.Monthly => current.AddMonths(1),
        // Three calendar months, not 91 days: a quarterly charge falls on the same day of the
        // month each time, and adding days would drift it forward across the short months.
        BillingCycleType.Quarterly => current.AddMonths(3),
        BillingCycleType.Yearly => current.AddYears(1),
        BillingCycleType.OneTime => current,
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unhandled billing cycle cadence."),
    };
}

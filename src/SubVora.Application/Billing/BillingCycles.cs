using SubVora.Domain.Enums;

namespace SubVora.Application.Billing;

/// <summary>Cycle arithmetic shared by the burn rate and each subscription's home-currency figures.</summary>
public static class BillingCycles
{
    public const int MonthsPerYear = 12;

    /// <summary>
    /// Weeks in a year for the purpose of the weekly headline. 52, not 365/7 - a weekly
    /// subscription should read back as exactly its own cost per week.
    /// </summary>
    public const int WeeksPerYear = 52;

    /// <summary>
    /// How many times a cycle is charged in a year, or null for <c>OneTime</c>, which never recurs.
    /// A subscription's annual cost is its price times this, and every other figure is derived from
    /// that sum.
    /// <para>
    /// It replaces normalising to a daily rate (<c>cost / cycle_days</c> with a 30-day month and a
    /// 365-day year), which quietly made a year 365/30 = 12.17 months: 1000/month reported 12166.67
    /// a year instead of 12000. The error was invisible at small numbers and grew with the total.
    /// Calendar cycles are counts, not spans of days, and counting them is also what a user checks
    /// the figure against.
    /// </para>
    /// </summary>
    public static int? ChargesPerYear(BillingCycleType cadence) => cadence switch
    {
        BillingCycleType.Weekly => WeeksPerYear,
        BillingCycleType.Monthly => MonthsPerYear,
        BillingCycleType.Quarterly => 4,
        BillingCycleType.Yearly => 1,
        BillingCycleType.OneTime => null,
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unexpected billing cycle."),
    };
}

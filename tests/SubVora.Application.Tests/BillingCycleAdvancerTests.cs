using SubVora.Application.Billing;
using SubVora.Domain.Enums;

namespace SubVora.Application.Tests;

public class BillingCycleAdvancerTests
{
    [Fact]
    public void AdvanceTo_Weekly_AddsSevenDays()
    {
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2026, 3, 1), BillingCycleType.Weekly, new DateOnly(2026, 3, 2));

        Assert.Equal(new DateOnly(2026, 3, 8), result);
    }

    [Fact]
    public void AdvanceTo_MonthlyFromJanuary31InCommonYear_ClampsToFebruary28()
    {
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2026, 1, 31), BillingCycleType.Monthly, new DateOnly(2026, 2, 1));

        Assert.Equal(new DateOnly(2026, 2, 28), result);
    }

    [Fact]
    public void AdvanceTo_MonthlyFromJanuary31InLeapYear_ClampsToFebruary29()
    {
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2028, 1, 31), BillingCycleType.Monthly, new DateOnly(2028, 2, 1));

        Assert.Equal(new DateOnly(2028, 2, 29), result);
    }

    [Fact]
    public void AdvanceTo_YearlyFromFebruary29_ClampsToFebruary28()
    {
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2028, 2, 29), BillingCycleType.Yearly, new DateOnly(2028, 3, 1));

        Assert.Equal(new DateOnly(2029, 2, 28), result);
    }

    [Fact]
    public void AdvanceTo_DateSeveralCyclesStale_LandsOnFirstOccurrenceAfterToday()
    {
        // Three monthly cycles behind: advancing once would still leave the date in the past.
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2026, 1, 10), BillingCycleType.Monthly, new DateOnly(2026, 4, 15));

        Assert.Equal(new DateOnly(2026, 5, 10), result);
    }

    [Fact]
    public void AdvanceTo_DateExactlyToday_MovesToNextCycle()
    {
        // "Strictly after today": a date landing on today has already been billed for this cycle.
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2026, 3, 10), BillingCycleType.Monthly, new DateOnly(2026, 3, 10));

        Assert.Equal(new DateOnly(2026, 4, 10), result);
    }

    [Fact]
    public void AdvanceTo_FutureDate_IsUnchanged()
    {
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2026, 6, 1), BillingCycleType.Monthly, new DateOnly(2026, 3, 10));

        Assert.Equal(new DateOnly(2026, 6, 1), result);
    }

    [Fact]
    public void AdvanceTo_OneTime_IsNeverAdvanced()
    {
        var result = BillingCycleAdvancer.AdvanceTo(new DateOnly(2026, 1, 10), BillingCycleType.OneTime, new DateOnly(2026, 4, 15));

        Assert.Equal(new DateOnly(2026, 1, 10), result);
    }
}

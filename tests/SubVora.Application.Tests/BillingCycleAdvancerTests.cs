using SubVora.Application.Billing;
using SubVora.Domain.Enums;

namespace SubVora.Application.Tests;

/// <summary>
/// Only reached when a user marks a charge paid, so it moves exactly one cycle - never "forward
/// until the date is in the future". A charge settled late still settles the date it was for.
/// </summary>
public class BillingCycleAdvancerTests
{
    [Theory]
    [InlineData(BillingCycleType.Weekly, 2026, 8, 14)]
    [InlineData(BillingCycleType.Monthly, 2026, 9, 7)]
    [InlineData(BillingCycleType.Yearly, 2027, 8, 7)]
    public void AdvancesExactlyOneCycle(BillingCycleType cadence, int year, int month, int day)
    {
        var result = BillingCycleAdvancer.AdvanceOneCycle(new DateOnly(2026, 8, 7), cadence);

        Assert.Equal(new DateOnly(year, month, day), result);
    }

    [Fact]
    public void ALongOverdueChargeMovesOnOneCycleOnly()
    {
        // Two years late, but paying settles one period - the next charge is a month after the one
        // just paid, not a month after today. Anything else silently forgives the periods between.
        var result = BillingCycleAdvancer.AdvanceOneCycle(new DateOnly(2024, 3, 10), BillingCycleType.Monthly);

        Assert.Equal(new DateOnly(2024, 4, 10), result);
    }

    [Fact]
    public void OneTimeNeverRecurs()
    {
        var current = new DateOnly(2026, 5, 1);

        Assert.Equal(current, BillingCycleAdvancer.AdvanceOneCycle(current, BillingCycleType.OneTime));
    }

    [Fact]
    public void MonthEndClampsRatherThanOverflowing()
    {
        // 31 Jan has no counterpart in February, so it clamps to the 28th instead of spilling into
        // March - the same day a billing provider would charge.
        var result = BillingCycleAdvancer.AdvanceOneCycle(new DateOnly(2026, 1, 31), BillingCycleType.Monthly);

        Assert.Equal(new DateOnly(2026, 2, 28), result);
    }

    [Fact]
    public void LeapDaySurvivesANonLeapYear()
    {
        var result = BillingCycleAdvancer.AdvanceOneCycle(new DateOnly(2024, 2, 29), BillingCycleType.Yearly);

        Assert.Equal(new DateOnly(2025, 2, 28), result);
    }
}

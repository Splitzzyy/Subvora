using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Billing;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Mirrors the server's BillingCycleAdvancer semantics. If one side's stepping rule changes, these
/// are the assertions that should stop the two drifting apart unnoticed.
/// </summary>
public class BillingCycleAdvancerTests
{
    private static readonly DateTime Today = new(2026, 8, 7);

    private static DateTime Next(DateTime purchase, BillingCycleType cadence) =>
        BillingCycleAdvancer.NextBillingDate(purchase, cadence, Today);

    [Theory]
    [InlineData(BillingCycleType.Weekly, 2026, 8, 14)]
    [InlineData(BillingCycleType.Monthly, 2026, 9, 7)]
    [InlineData(BillingCycleType.Yearly, 2027, 8, 7)]
    public void PurchasedToday_BillsOneFullCycleFromNow(BillingCycleType cadence, int year, int month, int day)
    {
        Assert.Equal(new DateTime(year, month, day), Next(Today, cadence));
    }

    [Fact]
    public void OneTime_BillsOnItsPurchaseDateAndNeverRecurs()
    {
        var purchase = new DateTime(2026, 1, 15);

        Assert.Equal(purchase, Next(purchase, BillingCycleType.OneTime));
    }

    [Fact]
    public void FuturePurchaseDate_BillsOnThatDateRatherThanAdvancingPastIt()
    {
        var purchase = new DateTime(2026, 12, 1);

        Assert.Equal(purchase, Next(purchase, BillingCycleType.Monthly));
    }

    [Fact]
    public void LongStalePurchaseDate_LandsInTheFutureNotOneCycleOn()
    {
        // Two years of monthly cycles behind us; stepping must not stop at 2024-04-10. The 10th of
        // this month is still ahead of today (the 7th), so that is where it lands.
        var result = Next(new DateTime(2024, 3, 10), BillingCycleType.Monthly);

        Assert.Equal(new DateTime(2026, 8, 10), result);
    }

    [Fact]
    public void MonthEndPurchase_StaysAnchoredToTheClampedDayNotTheOriginal()
    {
        // Jan 31 clamps to Feb 28, and every later step runs from the clamped date - the same
        // sequence a billing provider charges on, and why this steps one cycle at a time.
        var result = BillingCycleAdvancer.NextBillingDate(
            new DateTime(2026, 1, 31), BillingCycleType.Monthly, new DateTime(2026, 3, 1));

        Assert.Equal(new DateTime(2026, 3, 28), result);
    }

    [Fact]
    public void LeapDayPurchase_SurvivesANonLeapYear()
    {
        var result = BillingCycleAdvancer.NextBillingDate(
            new DateTime(2024, 2, 29), BillingCycleType.Yearly, new DateTime(2024, 6, 1));

        Assert.Equal(new DateTime(2025, 2, 28), result);
    }

    [Fact]
    public void TimeOfDayOnThePurchaseDate_DoesNotLeakIntoTheResult()
    {
        var result = BillingCycleAdvancer.NextBillingDate(
            new DateTime(2026, 8, 7, 22, 45, 0), BillingCycleType.Weekly, Today);

        Assert.Equal(new DateTime(2026, 8, 14), result);
    }
}

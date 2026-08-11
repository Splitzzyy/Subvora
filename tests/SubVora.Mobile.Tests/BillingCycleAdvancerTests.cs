using System.Globalization;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Billing;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The next billing date is one billing period after the purchase date - the end of the period the
/// purchase started - and nothing else. It is deliberately not "the next occurrence after today":
/// skipping a passed period hides the dates the user entered. Rolling a passed date forward is the
/// server's job (BillingDateAdvanceBackgroundService).
/// </summary>
public class BillingCycleAdvancerTests
{
    private static DateTime Next(DateTime purchase, BillingCycleType cadence) =>
        BillingCycleAdvancer.NextBillingDate(purchase, cadence);

    [Theory]
    [InlineData("2026-08-07", BillingCycleType.Weekly, "2026-08-14")]
    [InlineData("2026-08-07", BillingCycleType.Monthly, "2026-09-07")]
    [InlineData("2026-08-07", BillingCycleType.Quarterly, "2026-11-07")]
    [InlineData("2026-08-07", BillingCycleType.Yearly, "2027-08-07")]
    public void OneCycleAfterThePurchaseDate(string purchase, BillingCycleType cadence, string expected)
    {
        Assert.Equal(Date(expected), Next(Date(purchase), cadence));
    }

    [Fact]
    public void OneTime_BillsOnItsPurchaseDateAndNeverRecurs()
    {
        var purchase = new DateTime(2026, 1, 15);

        Assert.Equal(purchase, Next(purchase, BillingCycleType.OneTime));
    }

    /// <summary>
    /// The reported bug: a subscription bought in 2025 and billed yearly ends its period in 2026.
    /// Advancing to 2027 because 2026 has already gone by loses the period that was entered.
    /// </summary>
    [Theory]
    [InlineData("2025-04-23", "2026-04-23")]
    [InlineData("2025-01-15", "2026-01-15")]
    [InlineData("2025-12-01", "2026-12-01")]
    public void YearlyFromAPreviousYear_EndsItsPeriodTheFollowingYear(string purchase, string expected)
    {
        Assert.Equal(Date(expected), Next(Date(purchase), BillingCycleType.Yearly));
    }

    [Fact]
    public void APurchaseSeveralPeriodsOldStillReportsItsOwnPeriodEnd()
    {
        // Not 2026-08-10 or any later step - one cycle, once.
        Assert.Equal(new DateTime(2024, 4, 10), Next(new DateTime(2024, 3, 10), BillingCycleType.Monthly));
    }

    [Fact]
    public void MonthEndPurchase_ClampsRatherThanOverflowing()
    {
        // 31 Jan has no counterpart in February, so AddMonths clamps to the 28th rather than
        // spilling into March.
        Assert.Equal(new DateTime(2026, 2, 28), Next(new DateTime(2026, 1, 31), BillingCycleType.Monthly));
    }

    [Fact]
    public void LeapDayPurchase_SurvivesANonLeapYear()
    {
        Assert.Equal(new DateTime(2025, 2, 28), Next(new DateTime(2024, 2, 29), BillingCycleType.Yearly));
    }

    [Fact]
    public void FuturePurchaseDate_IsTreatedNoDifferently()
    {
        Assert.Equal(new DateTime(2027, 3, 1), Next(new DateTime(2027, 2, 1), BillingCycleType.Monthly));
    }

    [Fact]
    public void TimeOfDayOnThePurchaseDate_DoesNotLeakIntoTheResult()
    {
        Assert.Equal(new DateTime(2026, 8, 14), Next(new DateTime(2026, 8, 7, 22, 45, 0), BillingCycleType.Weekly));
    }

    private static DateTime Date(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);
}

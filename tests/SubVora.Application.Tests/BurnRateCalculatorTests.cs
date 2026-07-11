using SubVora.Application.Dashboard;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Tests;

public class BurnRateCalculatorTests
{
    private readonly BurnRateCalculator _calculator = new();

    private static SubscriptionDto RecurringSubscription(decimal cost, BillingCycleType cycle, bool isFreeTrial = false, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "Test Subscription",
        CostAmount = cost,
        Currency = "USD",
        CycleCadence = cycle,
        PurchaseDate = new DateOnly(DateTime.UtcNow.Year, 1, 1),
        NextBillingDate = new DateOnly(DateTime.UtcNow.Year, 2, 1),
        AlertDaysAdvance = 3,
        IsFreeTrial = isFreeTrial,
        IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static SubscriptionDto OneTimeSubscription(decimal cost, DateOnly purchaseDate, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "One-Time Purchase",
        CostAmount = cost,
        Currency = "USD",
        CycleCadence = BillingCycleType.OneTime,
        PurchaseDate = purchaseDate,
        NextBillingDate = purchaseDate,
        AlertDaysAdvance = 3,
        IsFreeTrial = false,
        IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void CalculatesWeeklyMonthlyYearly_ForMixOfCycles()
    {
        // Costs chosen so each subscription's daily rate is exactly 1 (cost == cycle length in
        // days), avoiding decimal-division rounding noise in the expected values below.
        var subscriptions = new[]
        {
            RecurringSubscription(7m, BillingCycleType.Weekly),
            RecurringSubscription(30m, BillingCycleType.Monthly),
            RecurringSubscription(365m, BillingCycleType.Yearly),
        };

        var result = _calculator.Calculate(subscriptions);

        // dailyRateSum = 1 + 1 + 1 = 3
        Assert.Equal(21m, result.Weekly);
        Assert.Equal(90m, result.Monthly);
        Assert.Equal(1095m, result.Yearly);
    }

    [Fact]
    public void ExcludesOneTimePurchasesFromRecurringTotals_ButSumsThemSeparately()
    {
        var thisYear = DateTime.UtcNow.Year;
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly),
            OneTimeSubscription(99m, new DateOnly(thisYear, 3, 15)),
        };

        var result = _calculator.Calculate(subscriptions);

        // dailyRate = 30/30 = 1, so Weekly = 1*7 = 7
        Assert.Equal(7m, result.Weekly);
        Assert.Equal(30m, result.Monthly);
        Assert.Equal(99m, result.OneTimeThisYear);
    }

    [Fact]
    public void OneTimePurchase_FromAPastYear_IsExcludedFromOneTimeThisYear()
    {
        var lastYear = DateTime.UtcNow.Year - 1;
        var subscriptions = new[] { OneTimeSubscription(99m, new DateOnly(lastYear, 12, 31)) };

        var result = _calculator.Calculate(subscriptions);

        Assert.Equal(0m, result.OneTimeThisYear);
    }

    [Fact]
    public void ExcludesActiveFreeTrialsFromTotals()
    {
        var subscriptions = new[] { RecurringSubscription(30m, BillingCycleType.Monthly, isFreeTrial: true) };

        var result = _calculator.Calculate(subscriptions);

        Assert.Equal(0m, result.Weekly);
        Assert.Equal(0m, result.Monthly);
        Assert.Equal(0m, result.Yearly);
    }

    [Fact]
    public void IncludesConvertedTrialOnceIsFreeTrialIsFalse()
    {
        var subscriptions = new[] { RecurringSubscription(30m, BillingCycleType.Monthly, isFreeTrial: false) };

        var result = _calculator.Calculate(subscriptions);

        Assert.Equal(30m, result.Monthly);
    }

    [Fact]
    public void ExcludesInactiveSubscriptionsFromAllTotals()
    {
        var thisYear = DateTime.UtcNow.Year;
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly, isActive: false),
            OneTimeSubscription(99m, new DateOnly(thisYear, 6, 1), isActive: false),
        };

        var result = _calculator.Calculate(subscriptions);

        Assert.Equal(0m, result.Monthly);
        Assert.Equal(0m, result.OneTimeThisYear);
    }

    [Fact]
    public void NoSubscriptions_ReturnsAllZeroes()
    {
        var result = _calculator.Calculate([]);

        Assert.Equal(0m, result.Weekly);
        Assert.Equal(0m, result.Monthly);
        Assert.Equal(0m, result.Yearly);
        Assert.Equal(0m, result.OneTimeThisYear);
    }
}

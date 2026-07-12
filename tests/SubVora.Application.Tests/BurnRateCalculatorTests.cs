using SubVora.Application.Currency;
using SubVora.Application.Dashboard;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Tests;

public class BurnRateCalculatorTests
{
    private readonly FakeFxRateService _fxRateService = new();
    private readonly BurnRateCalculator _calculator;

    public BurnRateCalculatorTests()
    {
        _calculator = new BurnRateCalculator(_fxRateService);
    }

    private static SubscriptionDto RecurringSubscription(decimal cost, BillingCycleType cycle, bool isFreeTrial = false, bool isActive = true, string currency = "USD") => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "Test Subscription",
        CostAmount = cost,
        Currency = currency,
        CycleCadence = cycle,
        PurchaseDate = new DateOnly(DateTime.UtcNow.Year, 1, 1),
        NextBillingDate = new DateOnly(DateTime.UtcNow.Year, 2, 1),
        AlertDaysAdvance = 3,
        IsFreeTrial = isFreeTrial,
        IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static SubscriptionDto OneTimeSubscription(decimal cost, DateOnly purchaseDate, bool isActive = true, string currency = "USD") => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "One-Time Purchase",
        CostAmount = cost,
        Currency = currency,
        CycleCadence = BillingCycleType.OneTime,
        PurchaseDate = purchaseDate,
        NextBillingDate = purchaseDate,
        AlertDaysAdvance = 3,
        IsFreeTrial = false,
        IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task CalculatesWeeklyMonthlyYearly_ForMixOfCycles()
    {
        // Costs chosen so each subscription's daily rate is exactly 1 (cost == cycle length in
        // days), avoiding decimal-division rounding noise in the expected values below.
        var subscriptions = new[]
        {
            RecurringSubscription(7m, BillingCycleType.Weekly),
            RecurringSubscription(30m, BillingCycleType.Monthly),
            RecurringSubscription(365m, BillingCycleType.Yearly),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        // dailyRateSum = 1 + 1 + 1 = 3
        Assert.Equal(21m, result.Weekly);
        Assert.Equal(90m, result.Monthly);
        Assert.Equal(1095m, result.Yearly);
    }

    [Fact]
    public async Task ExcludesOneTimePurchasesFromRecurringTotals_ButSumsThemSeparately()
    {
        var thisYear = DateTime.UtcNow.Year;
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly),
            OneTimeSubscription(99m, new DateOnly(thisYear, 3, 15)),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        // dailyRate = 30/30 = 1, so Weekly = 1*7 = 7
        Assert.Equal(7m, result.Weekly);
        Assert.Equal(30m, result.Monthly);
        Assert.Equal(99m, result.OneTimeThisYear);
    }

    [Fact]
    public async Task OneTimePurchase_FromAPastYear_IsExcludedFromOneTimeThisYear()
    {
        var lastYear = DateTime.UtcNow.Year - 1;
        var subscriptions = new[] { OneTimeSubscription(99m, new DateOnly(lastYear, 12, 31)) };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        Assert.Equal(0m, result.OneTimeThisYear);
    }

    [Fact]
    public async Task ExcludesActiveFreeTrialsFromTotals()
    {
        var subscriptions = new[] { RecurringSubscription(30m, BillingCycleType.Monthly, isFreeTrial: true) };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        Assert.Equal(0m, result.Weekly);
        Assert.Equal(0m, result.Monthly);
        Assert.Equal(0m, result.Yearly);
    }

    [Fact]
    public async Task IncludesConvertedTrialOnceIsFreeTrialIsFalse()
    {
        var subscriptions = new[] { RecurringSubscription(30m, BillingCycleType.Monthly, isFreeTrial: false) };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        Assert.Equal(30m, result.Monthly);
    }

    [Fact]
    public async Task ExcludesInactiveSubscriptionsFromAllTotals()
    {
        var thisYear = DateTime.UtcNow.Year;
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly, isActive: false),
            OneTimeSubscription(99m, new DateOnly(thisYear, 6, 1), isActive: false),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        Assert.Equal(0m, result.Monthly);
        Assert.Equal(0m, result.OneTimeThisYear);
    }

    [Fact]
    public async Task NoSubscriptions_ReturnsAllZeroes()
    {
        var result = await _calculator.CalculateAsync([], "USD");

        Assert.Equal(0m, result.Weekly);
        Assert.Equal(0m, result.Monthly);
        Assert.Equal(0m, result.Yearly);
        Assert.Equal(0m, result.OneTimeThisYear);
    }

    [Fact]
    public async Task ConvertsMixedCurrencySubscriptionsToHomeCurrencyBeforeSumming()
    {
        _fxRateService.SetRate("EUR", "USD", 1.1m);
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly, currency: "USD"),
            RecurringSubscription(30m, BillingCycleType.Monthly, currency: "EUR"),
            OneTimeSubscription(100m, new DateOnly(DateTime.UtcNow.Year, 3, 1), currency: "EUR"),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        // USD sub: dailyRate = 1. EUR sub converted: 30 * 1.1 = 33, dailyRate = 1.1. Sum = 2.1/day.
        Assert.Equal(Math.Round(2.1m * 30, 2), result.Monthly);
        Assert.Equal(110m, result.OneTimeThisYear);
        Assert.Equal("USD", result.HomeCurrency);
        Assert.Empty(result.UnresolvedSubscriptionIds);
    }

    [Fact]
    public async Task MissingFxRateForAPair_ExcludesThatSubscriptionAndFlagsIt()
    {
        var resolvedSubscription = RecurringSubscription(30m, BillingCycleType.Monthly, currency: "USD");
        var unresolvedSubscription = RecurringSubscription(30m, BillingCycleType.Monthly, currency: "JPY");
        var subscriptions = new[] { resolvedSubscription, unresolvedSubscription };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        // Only the USD subscription contributes; JPY has no cached rate and is excluded, not zeroed.
        Assert.Equal(30m, result.Monthly);
        Assert.Equal([unresolvedSubscription.Id], result.UnresolvedSubscriptionIds);
    }

    private sealed class FakeFxRateService : IFxRateService
    {
        private readonly Dictionary<(string BaseCurrency, string TargetCurrency), decimal> _rates = new();

        public void SetRate(string baseCurrency, string targetCurrency, decimal rate) =>
            _rates[(baseCurrency, targetCurrency)] = rate;

        public Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<decimal?> GetRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rates.TryGetValue((baseCurrency, targetCurrency), out var rate) ? rate : (decimal?)null);
    }
}

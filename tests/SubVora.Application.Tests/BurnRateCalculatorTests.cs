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

    private static SubscriptionDto RecurringSubscription(decimal cost, BillingCycleType cycle, bool isFreeTrial = false, bool isActive = true, string currency = "USD", Guid? categoryId = null, string? categoryName = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "Test Subscription",
        CostAmount = cost,
        Currency = currency,
        CycleCadence = cycle,
        PurchaseDate = new DateOnly(DateTime.UtcNow.Year, 1, 1),
        NextBillingDate = new DateOnly(DateTime.UtcNow.Year, 2, 1),
        AlertDaysAdvance = 3,
        CategoryId = categoryId,
        CategoryName = categoryName,
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

    [Fact]
    public async Task ReportsTheOldestRateItUsed_SoAStalledRefreshJobIsVisible()
    {
        var fresh = DateTimeOffset.UtcNow.AddHours(-2);
        var stale = DateTimeOffset.UtcNow.AddDays(-31);
        _fxRateService.SetRate("EUR", "USD", 1.1m, fresh);
        _fxRateService.SetRate("GBP", "USD", 1.3m, stale);

        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly, currency: "EUR"),
            RecurringSubscription(30m, BillingCycleType.Monthly, currency: "GBP"),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        // A month-old rate still converts - the age is reported, not enforced.
        Assert.Empty(result.UnresolvedSubscriptionIds);
        Assert.Equal(stale, result.OldestRateFetchedAt);
    }

    [Fact]
    public async Task NoConversionNeeded_LeavesTheRateAgeUnset()
    {
        var subscriptions = new[] { RecurringSubscription(30m, BillingCycleType.Monthly, currency: "USD") };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        Assert.Null(result.OldestRateFetchedAt);
    }

    [Fact]
    public async Task GroupsMonthlySpendByCategory_ExcludingOneTimeAndTrials()
    {
        var streamingCategoryId = Guid.NewGuid();
        var utilitiesCategoryId = Guid.NewGuid();
        var thisYear = DateTime.UtcNow.Year;
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly, categoryId: streamingCategoryId, categoryName: "Streaming"),
            RecurringSubscription(30m, BillingCycleType.Monthly, categoryId: streamingCategoryId, categoryName: "Streaming"),
            RecurringSubscription(30m, BillingCycleType.Monthly, categoryId: utilitiesCategoryId, categoryName: "Utilities"),
            RecurringSubscription(30m, BillingCycleType.Monthly, categoryId: streamingCategoryId, categoryName: "Streaming", isFreeTrial: true),
            OneTimeSubscription(99m, new DateOnly(thisYear, 3, 15)),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        Assert.Equal(2, result.ByCategory.Count);
        var streaming = result.ByCategory.Single(c => c.CategoryId == streamingCategoryId);
        Assert.Equal("Streaming", streaming.CategoryName);
        Assert.Equal(60m, streaming.MonthlyAmount);
        var utilities = result.ByCategory.Single(c => c.CategoryId == utilitiesCategoryId);
        Assert.Equal("Utilities", utilities.CategoryName);
        Assert.Equal(30m, utilities.MonthlyAmount);
    }

    [Fact]
    public async Task SubscriptionsWithNoCategory_GroupUnderUncategorized()
    {
        var subscriptions = new[]
        {
            RecurringSubscription(30m, BillingCycleType.Monthly),
            RecurringSubscription(30m, BillingCycleType.Monthly),
        };

        var result = await _calculator.CalculateAsync(subscriptions, "USD");

        var uncategorized = Assert.Single(result.ByCategory);
        Assert.Null(uncategorized.CategoryId);
        Assert.Equal("Uncategorized", uncategorized.CategoryName);
        Assert.Equal(60m, uncategorized.MonthlyAmount);
    }

    [Fact]
    public async Task CalculateAsync_ResolvesEveryRateInOneCall_WhateverTheSubscriptionCount()
    {
        // The N+1 this replaced: twenty USD subscriptions used to issue the same query twenty
        // times, on the screen the app opens to.
        _fxRateService.SetRate("USD", "INR", 83m);
        var subscriptions = Enumerable.Range(0, 20)
            .Select(_ => RecurringSubscription(10m, BillingCycleType.Monthly, currency: "USD"))
            .ToList();

        await _calculator.CalculateAsync(subscriptions, "INR");

        // One call, not twenty. There is no per-subscription path left to fall back to.
        Assert.Equal(1, _fxRateService.BatchCalls);
    }

    [Fact]
    public async Task CalculateAsync_AsksForEachCurrencyOnce_AndNotForTheHomeCurrency()
    {
        _fxRateService.SetRate("USD", "INR", 83m);
        _fxRateService.SetRate("EUR", "INR", 90m);

        var subscriptions = new[]
        {
            RecurringSubscription(10m, BillingCycleType.Monthly, currency: "USD"),
            RecurringSubscription(10m, BillingCycleType.Monthly, currency: "USD"),
            RecurringSubscription(10m, BillingCycleType.Monthly, currency: "EUR"),
            // Home currency: converts at 1 and must never be looked up, since no identity row exists.
            RecurringSubscription(10m, BillingCycleType.Monthly, currency: "INR"),
        };

        await _calculator.CalculateAsync(subscriptions, "INR");

        Assert.Equal(["USD", "EUR"], _fxRateService.LastRequestedBaseCurrencies.Order().Reverse());
        Assert.DoesNotContain("INR", _fxRateService.LastRequestedBaseCurrencies);
    }

    [Fact]
    public async Task CalculateAsync_DoesNotAskForInactiveSubscriptionsCurrencies()
    {
        // Inactive rows are skipped for the totals, so fetching their rates would be work done to
        // be thrown away - and could trigger a live provider call for a currency nobody uses.
        _fxRateService.SetRate("USD", "INR", 83m);

        var subscriptions = new[]
        {
            RecurringSubscription(10m, BillingCycleType.Monthly, currency: "USD"),
            RecurringSubscription(10m, BillingCycleType.Monthly, isActive: false, currency: "JPY"),
        };

        await _calculator.CalculateAsync(subscriptions, "INR");

        Assert.DoesNotContain("JPY", _fxRateService.LastRequestedBaseCurrencies);
    }

    [Fact]
    public async Task CalculateAsync_WithASequenceThatCanOnlyBeEnumeratedOnce_StillWorks()
    {
        // The calculator now walks the collection twice - once for currencies, once for amounts -
        // so it must materialize rather than trusting the caller's IEnumerable.
        _fxRateService.SetRate("USD", "INR", 83m);
        var subscriptions = SingleUseSequence(RecurringSubscription(10m, BillingCycleType.Monthly, currency: "USD"));

        var result = await _calculator.CalculateAsync(subscriptions, "INR");

        Assert.Equal(830m, result.Monthly);

        static IEnumerable<SubscriptionDto> SingleUseSequence(params SubscriptionDto[] items)
        {
            var consumed = false;
            return Inner();

            IEnumerable<SubscriptionDto> Inner()
            {
                if (consumed)
                {
                    throw new InvalidOperationException("This sequence has already been enumerated.");
                }

                consumed = true;
                foreach (var item in items)
                {
                    yield return item;
                }
            }
        }
    }

    private sealed class FakeFxRateService : IFxRateService
    {
        private readonly Dictionary<(string BaseCurrency, string TargetCurrency), CachedFxRate> _rates = new();

        public void SetRate(string baseCurrency, string targetCurrency, decimal rate, DateTimeOffset? fetchedAt = null) =>
            _rates[(baseCurrency, targetCurrency)] = new CachedFxRate(rate, fetchedAt ?? DateTimeOffset.UtcNow);

        public Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <summary>How many times the batch was asked. The calculator should need exactly one call, whatever the subscription count.</summary>
        public int BatchCalls { get; private set; }

        /// <summary>The base currencies of the most recent batch, so a test can assert duplicates were collapsed.</summary>
        public IReadOnlyCollection<string> LastRequestedBaseCurrencies { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, CachedFxRate>> GetRatesAsync(
            IReadOnlyCollection<string> baseCurrencies,
            string targetCurrency,
            CancellationToken cancellationToken = default)
        {
            BatchCalls++;

            // Mirrors the real implementation: identity pairs are dropped, the rest deduplicated.
            var wanted = baseCurrencies
                .Where(currency => !string.Equals(currency, targetCurrency, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            LastRequestedBaseCurrencies = wanted;

            var resolved = new Dictionary<string, CachedFxRate>(StringComparer.OrdinalIgnoreCase);
            foreach (var currency in wanted)
            {
                if (_rates.TryGetValue((currency, targetCurrency), out var rate))
                {
                    resolved[currency] = rate;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, CachedFxRate>>(resolved);
        }
    }
}

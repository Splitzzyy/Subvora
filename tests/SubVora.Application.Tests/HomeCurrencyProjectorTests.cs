using SubVora.Application.Currency;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Tests;

public class HomeCurrencyProjectorTests
{
    private static SubscriptionDto Subscription(decimal cost, BillingCycleType cycle, string currency = "INR") => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "Test",
        CostAmount = cost,
        Currency = currency,
        CycleCadence = cycle,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 2, 1),
        IsActive = true,
    };

    [Fact]
    public async Task Monthly_CountsTwelveCharges_NotAThirtyDayRate()
    {
        var sub = Subscription(1000m, BillingCycleType.Monthly);

        await new HomeCurrencyProjector(new FakeRates()).ProjectAsync([sub], "INR");

        Assert.Equal("INR", sub.HomeCurrency);
        Assert.Equal(1000m, sub.HomeCurrencyAmount);
        Assert.Equal(12000m, sub.HomeCurrencyYearly);
        Assert.Equal(1000m, sub.HomeCurrencyMonthly);
    }

    [Theory]
    [InlineData(BillingCycleType.Weekly, 520, 43.33)]
    [InlineData(BillingCycleType.Quarterly, 40, 3.33)]
    [InlineData(BillingCycleType.Yearly, 10, 0.83)]
    public async Task EachCycle_UsesItsChargesPerYear(BillingCycleType cycle, decimal yearly, decimal monthly)
    {
        var sub = Subscription(10m, cycle);

        await new HomeCurrencyProjector(new FakeRates()).ProjectAsync([sub], "INR");

        Assert.Equal(yearly, sub.HomeCurrencyYearly);
        Assert.Equal(monthly, sub.HomeCurrencyMonthly);
    }

    [Fact]
    public async Task ForeignCurrency_ConvertsAtTheCachedRate_AndLeavesTheStoredAmountAlone()
    {
        var sub = Subscription(20m, BillingCycleType.Monthly, "USD");

        await new HomeCurrencyProjector(new FakeRates(("USD", 95m))).ProjectAsync([sub], "INR");

        Assert.Equal(1900m, sub.HomeCurrencyAmount);
        Assert.Equal(22800m, sub.HomeCurrencyYearly);
        Assert.Equal(20m, sub.CostAmount);
        Assert.Equal("USD", sub.Currency);
    }

    [Fact]
    public async Task MissingRate_LeavesTheFiguresNull()
    {
        var sub = Subscription(20m, BillingCycleType.Monthly, "CHF");

        await new HomeCurrencyProjector(new FakeRates()).ProjectAsync([sub], "INR");

        Assert.Equal("INR", sub.HomeCurrency);
        Assert.Null(sub.HomeCurrencyAmount);
        Assert.Null(sub.HomeCurrencyYearly);
        Assert.Null(sub.HomeCurrencyMonthly);
    }

    [Fact]
    public async Task OneTime_HasAnAmountButNoRecurringFigures()
    {
        var sub = Subscription(500m, BillingCycleType.OneTime);

        await new HomeCurrencyProjector(new FakeRates()).ProjectAsync([sub], "INR");

        Assert.Equal(500m, sub.HomeCurrencyAmount);
        Assert.Null(sub.HomeCurrencyYearly);
        Assert.Null(sub.HomeCurrencyMonthly);
    }

    private sealed class FakeRates(params (string Base, decimal Rate)[] rates) : IFxRateService
    {
        public Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, CachedFxRate>> GetRatesAsync(IReadOnlyCollection<string> baseCurrencies, string targetCurrency, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, CachedFxRate>>(rates.ToDictionary(r => r.Base, r => new CachedFxRate(r.Rate, DateTimeOffset.UtcNow), StringComparer.OrdinalIgnoreCase));
    }
}

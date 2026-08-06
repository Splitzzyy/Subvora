using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SubVora.Application.Currency;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Currency;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class FxRateRefreshJobTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;

    public FxRateRefreshJobTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = AppDbContextOptionsFactory.Build(_fixture.ConnectionString);
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString)));
        services.AddScoped<IFxRateService, FxRateService>();
        // FxRateService fetches a pair on a cache miss, so it needs a client of its own. The
        // refresh job is handed its client explicitly by BuildService and ignores this one.
        services.AddScoped<IExchangeRateClient>(_ => OnDemandClient);
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>Swapped per test to control what an on-demand (cache-miss) fetch returns.</summary>
    private IExchangeRateClient OnDemandClient { get; set; } = new ThrowingExchangeRateClient();

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _dbContext.DisposeAsync();
    }

    private async Task<Guid> SeedUserWithUsdSubscriptionAndEurPreferredAsync()
    {
        var user = new User
        {
            Email = $"fx-{Guid.NewGuid()}@example.com",
            PasswordHash = "not-a-real-hash",
            PreferredCurrency = "EUR",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _dbContext.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            CustomName = "Test Subscription",
            CostAmount = 10m,
            Currency = "USD",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 2, 1),
            AlertDaysAdvance = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        return user.Id;
    }

    private FxRateRefreshBackgroundService BuildService(IExchangeRateClient client)
    {
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var logger = _serviceProvider.GetRequiredService<ILogger<FxRateRefreshBackgroundService>>();
        return new FxRateRefreshBackgroundService(scopeFactory, client, logger, new ConfigurationBuilder().Build());
    }

    private async Task<CachedFxRate?> GetRateAsync(string baseCurrency, string targetCurrency)
    {
        using var scope = _serviceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IFxRateService>().GetRateAsync(baseCurrency, targetCurrency);
    }

    [Fact]
    public async Task FxRateRefreshJob_UpsertsRatesWithoutDuplicates()
    {
        await SeedUserWithUsdSubscriptionAndEurPreferredAsync();

        await BuildService(new FakeExchangeRateClient(0.90m)).RefreshOnceAsync();
        await BuildService(new FakeExchangeRateClient(0.95m)).RefreshOnceAsync();

        var rates = await _dbContext.FxRates.AsNoTracking()
            .Where(r => r.BaseCurrency == "USD" && r.TargetCurrency == "EUR")
            .ToListAsync();

        Assert.Single(rates);
        Assert.Equal(0.95m, rates[0].Rate);
    }

    [Fact]
    public async Task FxRateRefreshJob_ClientThrows_LeavesPreviouslyCachedRatesUnchanged()
    {
        await SeedUserWithUsdSubscriptionAndEurPreferredAsync();

        await BuildService(new FakeExchangeRateClient(0.90m)).RefreshOnceAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildService(new ThrowingExchangeRateClient()).RefreshOnceAsync());

        var rate = await _dbContext.FxRates.AsNoTracking()
            .SingleAsync(r => r.BaseCurrency == "USD" && r.TargetCurrency == "EUR");
        Assert.Equal(0.90m, rate.Rate);
    }

    [Fact]
    public async Task GetRate_ForAPairTheScheduledPassNeverFetched_FetchesItOnDemandAndCachesIt()
    {
        // The gap this closes: a user adding their first subscription in a new currency was
        // excluded from their own totals until the next scheduled pass, up to a day later.
        OnDemandClient = new FakeExchangeRateClient(1.25m);

        var rate = await GetRateAsync("AAA", "BBB");

        Assert.NotNull(rate);
        Assert.Equal(1.25m, rate!.Rate);

        var cached = await _dbContext.FxRates.AsNoTracking().SingleAsync(r => r.BaseCurrency == "AAA" && r.TargetCurrency == "BBB");
        Assert.Equal(1.25m, cached.Rate);
    }

    [Fact]
    public async Task GetRate_WhenTheOnDemandFetchFails_ReturnsNullWithoutThrowing()
    {
        OnDemandClient = new ThrowingExchangeRateClient();

        Assert.Null(await GetRateAsync("CCC", "DDD"));
    }

    [Fact]
    public async Task GetRate_ForAPairAlreadyCached_DoesNotCallTheProvider()
    {
        OnDemandClient = new FakeExchangeRateClient(2m);
        await GetRateAsync("EEE", "FFF");

        var countingClient = new FakeExchangeRateClient(99m);
        OnDemandClient = countingClient;
        var rate = await GetRateAsync("EEE", "FFF");

        Assert.Equal(2m, rate!.Rate);
        Assert.Equal(0, countingClient.CallCount);
    }

    private class FakeExchangeRateClient : IExchangeRateClient
    {
        private readonly decimal _rate;

        public FakeExchangeRateClient(decimal rate)
        {
            _rate = rate;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<ExchangeRate> rates = targetCurrencies.Select(target => new ExchangeRate(baseCurrency, target, _rate)).ToList();
            return Task.FromResult(rates);
        }
    }

    private class ThrowingExchangeRateClient : IExchangeRateClient
    {
        public Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated exchangerate.host outage.");
    }
}

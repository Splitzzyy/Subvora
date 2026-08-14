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
        var rates = await scope.ServiceProvider.GetRequiredService<IFxRateService>()
            .GetRatesAsync([baseCurrency], targetCurrency);
        return rates.GetValueOrDefault(baseCurrency);
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

        // No longer throws: failures are isolated per base currency so one bad pair cannot discard
        // the pass. A total outage still writes nothing, which is what this test has always been
        // about - the previously cached rate must survive untouched either way.
        await BuildService(new ThrowingExchangeRateClient()).RefreshOnceAsync();

        var rate = await _dbContext.FxRates.AsNoTracking()
            .SingleAsync(r => r.BaseCurrency == "USD" && r.TargetCurrency == "EUR");
        Assert.Equal(0.90m, rate.Rate);
    }

    [Fact]
    public async Task FxRateRefreshJob_WhenOneBaseCurrencyFails_StillUpsertsTheRest()
    {
        // The defect: rates accumulated into one list and were upserted only after the loop, so a
        // single unsupported pair or one transient 5xx threw past the upsert and discarded every
        // rate already fetched. One user tracking something exotic aged everybody's totals.
        var user = new User
        {
            Email = $"fx-partial-{Guid.NewGuid()}@example.com",
            PasswordHash = "not-a-real-hash",
            PreferredCurrency = "EUR",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        foreach (var currency in new[] { "USD", "ZZZ" })
        {
            _dbContext.UserSubscriptions.Add(new UserSubscription
            {
                UserId = user.Id,
                CustomName = $"{currency} Subscription",
                CostAmount = 10m,
                Currency = currency,
                CycleCadence = BillingCycleType.Monthly,
                PurchaseDate = new DateOnly(2026, 1, 1),
                NextBillingDate = new DateOnly(2026, 2, 1),
                AlertDaysAdvance = 3,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync();

        await BuildService(new SelectivelyFailingExchangeRateClient(failFor: "ZZZ", rate: 0.88m)).RefreshOnceAsync();

        var usd = await _dbContext.FxRates.AsNoTracking()
            .SingleOrDefaultAsync(r => r.BaseCurrency == "USD" && r.TargetCurrency == "EUR");
        Assert.NotNull(usd);
        Assert.Equal(0.88m, usd!.Rate);

        Assert.False(await _dbContext.FxRates.AsNoTracking()
            .AnyAsync(r => r.BaseCurrency == "ZZZ" && r.TargetCurrency == "EUR"));
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

    private async Task<IReadOnlyDictionary<string, CachedFxRate>> GetRatesAsync(IReadOnlyCollection<string> baseCurrencies, string targetCurrency)
    {
        using var scope = _serviceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IFxRateService>().GetRatesAsync(baseCurrencies, targetCurrency);
    }

    [Fact]
    public async Task GetRates_ReturnsEveryCachedPairInOneCall()
    {
        // Exercises the Contains translation against a real database - Npgsql turns it into
        // base_currency = ANY(...), which is the whole point of the batch.
        OnDemandClient = new FakeExchangeRateClient(1m);
        await _dbContext.FxRates.AddRangeAsync(
            new FxRate { BaseCurrency = "GBP", TargetCurrency = "INR", Rate = 105m, FetchedAt = DateTimeOffset.UtcNow },
            new FxRate { BaseCurrency = "JPY", TargetCurrency = "INR", Rate = 0.55m, FetchedAt = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync();

        var rates = await GetRatesAsync(["GBP", "JPY"], "INR");

        Assert.Equal(2, rates.Count);
        Assert.Equal(105m, rates["GBP"].Rate);
        Assert.Equal(0.55m, rates["JPY"].Rate);
    }

    [Fact]
    public async Task GetRates_DropsTheTargetCurrencyAndDeduplicates()
    {
        // Nothing stores an identity rate, so asking for one would be a guaranteed miss - and would
        // then trigger a pointless on-demand provider call.
        var countingClient = new FakeExchangeRateClient(1m);
        OnDemandClient = countingClient;
        await _dbContext.FxRates.AddAsync(
            new FxRate { BaseCurrency = "CHF", TargetCurrency = "INR", Rate = 92m, FetchedAt = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync();

        var rates = await GetRatesAsync(["CHF", "CHF", "INR"], "INR");

        Assert.Equal(92m, Assert.Single(rates).Value.Rate);
        Assert.False(rates.ContainsKey("INR"));
        Assert.Equal(0, countingClient.CallCount);
    }

    [Fact]
    public async Task GetRates_FetchesOnlyThePairsTheBatchMissed()
    {
        // The on-demand path has to survive batching: a user's first subscription in a new currency
        // must still count toward their totals before the next scheduled pass.
        await _dbContext.FxRates.AddAsync(
            new FxRate { BaseCurrency = "SEK", TargetCurrency = "INR", Rate = 8m, FetchedAt = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync();

        var countingClient = new FakeExchangeRateClient(3.5m);
        OnDemandClient = countingClient;

        var rates = await GetRatesAsync(["SEK", "NOK"], "INR");

        Assert.Equal(8m, rates["SEK"].Rate);
        Assert.Equal(3.5m, rates["NOK"].Rate);
        // Once, for NOK only - the cached SEK row must not trigger a provider call.
        Assert.Equal(1, countingClient.CallCount);

        var cachedNok = await _dbContext.FxRates.AsNoTracking().SingleAsync(r => r.BaseCurrency == "NOK" && r.TargetCurrency == "INR");
        Assert.Equal(3.5m, cachedNok.Rate);
    }

    [Fact]
    public async Task GetRates_OmitsPairsThatCannotBeResolvedAtAll()
    {
        // Absent from the dictionary is how the batch says "no rate", and it is what puts the
        // subscription into UnresolvedSubscriptionIds rather than silently valuing it at zero.
        OnDemandClient = new ThrowingExchangeRateClient();

        var rates = await GetRatesAsync(["ZZZ"], "INR");

        Assert.Empty(rates);
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

    /// <summary>Fails for one base currency and succeeds for every other - the partial-failure case.</summary>
    private class SelectivelyFailingExchangeRateClient : IExchangeRateClient
    {
        private readonly string _failFor;
        private readonly decimal _rate;

        public SelectivelyFailingExchangeRateClient(string failFor, decimal rate)
        {
            _failFor = failFor;
            _rate = rate;
        }

        public Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default)
        {
            if (baseCurrency == _failFor)
            {
                throw new InvalidOperationException($"Simulated provider refusal for {baseCurrency}.");
            }

            IReadOnlyList<ExchangeRate> rates = targetCurrencies.Select(target => new ExchangeRate(baseCurrency, target, _rate)).ToList();
            return Task.FromResult(rates);
        }
    }
}

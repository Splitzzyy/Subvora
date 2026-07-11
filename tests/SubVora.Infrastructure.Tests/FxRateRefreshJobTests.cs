using Microsoft.EntityFrameworkCore;
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
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

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
        return new FxRateRefreshBackgroundService(scopeFactory, client, logger);
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

    private class FakeExchangeRateClient : IExchangeRateClient
    {
        private readonly decimal _rate;

        public FakeExchangeRateClient(decimal rate)
        {
            _rate = rate;
        }

        public Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default)
        {
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

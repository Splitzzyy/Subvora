using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SubVora.Application.Billing;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Billing;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class BillingDateAdvanceTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly PostgresContainerFixture _fixture;
    private readonly List<string> _executedSql = [];
    private AppDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;

    public BillingDateAdvanceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _dbContext = new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString));
        await _dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>(AppDbContextOptionsFactory.Build(_fixture.ConnectionString))
                .LogTo(_executedSql.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)
                .Options));
        services.AddSingleton<IBillingDateScanner, BillingDateScanner>();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _dbContext.DisposeAsync();
    }

    private BillingDateAdvanceBackgroundService BuildService() => new(
        _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
        _serviceProvider.GetRequiredService<IBillingDateScanner>(),
        _serviceProvider.GetRequiredService<ILogger<BillingDateAdvanceBackgroundService>>(),
        new ConfigurationBuilder().Build());

    private async Task<UserSubscription> CreateSubscriptionAsync(
        DateOnly nextBillingDate,
        bool isActive = true,
        BillingCycleType cadence = BillingCycleType.Monthly)
    {
        var user = new User { Email = $"advance-{Guid.NewGuid()}@example.com", PasswordHash = "not-a-real-hash", PreferredCurrency = "USD", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var subscription = new UserSubscription
        {
            UserId = user.Id,
            CustomName = "Netflix",
            CostAmount = 15.49m,
            Currency = "USD",
            CycleCadence = cadence,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = nextBillingDate,
            AlertDaysAdvance = 3,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.UserSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        return subscription;
    }

    [Fact]
    public async Task SubscriptionPastItsBillingDate_AdvancesToAFutureDate()
    {
        // Three monthly cycles stale: advancing a single cycle would still leave it in the past.
        var subscription = await CreateSubscriptionAsync(Today.AddMonths(-3).AddDays(-2));

        await BuildService().ScanOnceAsync(Today);

        var stored = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);
        Assert.True(stored.NextBillingDate > Today, $"Expected a future billing date, got {stored.NextBillingDate}.");
    }

    [Fact]
    public async Task RunTwiceForTheSameDay_AdvancesOnlyOnce()
    {
        var subscription = await CreateSubscriptionAsync(Today.AddDays(-1));

        await BuildService().ScanOnceAsync(Today);
        var afterFirst = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);

        await BuildService().ScanOnceAsync(Today);
        var afterSecond = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);

        // Idempotent by construction: the second pass finds nothing left with a passed date.
        Assert.Equal(afterFirst.NextBillingDate, afterSecond.NextBillingDate);
    }

    [Fact]
    public async Task OneTimeSubscriptionPastItsBillingDate_IsRetiredNotAdvanced()
    {
        var subscription = await CreateSubscriptionAsync(Today.AddDays(-1), cadence: BillingCycleType.OneTime);

        await BuildService().ScanOnceAsync(Today);

        var stored = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);
        Assert.False(stored.IsActive);
        Assert.Equal(Today.AddDays(-1), stored.NextBillingDate);
    }

    [Fact]
    public async Task SubscriptionWithAFutureBillingDate_IsLeftAlone()
    {
        var subscription = await CreateSubscriptionAsync(Today.AddDays(20));

        await BuildService().ScanOnceAsync(Today);

        var stored = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(Today.AddDays(20), stored.NextBillingDate);
    }

    [Fact]
    public async Task InactiveSubscriptionPastItsBillingDate_IsLeftAlone()
    {
        var subscription = await CreateSubscriptionAsync(Today.AddDays(-30), isActive: false);

        await BuildService().ScanOnceAsync(Today);

        var stored = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(Today.AddDays(-30), stored.NextBillingDate);
    }

    [Fact]
    public async Task DoesNotLoadSubscriptionsThatAreNotDueForAdvance()
    {
        // Renews well into the future: the pass must filter it out in SQL, not read it back and
        // discard it in memory. Asserting on the emitted SQL is what stops a future refactor from
        // quietly restoring the full-table read.
        await CreateSubscriptionAsync(Today.AddDays(200));

        await BuildService().ScanOnceAsync(Today);

        var subscriptionQueries = _executedSql.Where(sql => sql.Contains("FROM user_subscriptions")).ToList();
        Assert.NotEmpty(subscriptionQueries);
        Assert.All(subscriptionQueries, sql => Assert.Contains("next_billing_date", sql));
    }
}

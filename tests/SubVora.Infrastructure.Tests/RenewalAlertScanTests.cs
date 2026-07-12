using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SubVora.Application.Alerts;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Alerts;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class RenewalAlertScanTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    // Must be the real current day: ScanOnceAsync stamps new notifications_log rows with
    // DateTimeOffset.UtcNow, and the idempotency re-check window is built from this same value.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;

    public RenewalAlertScanTests(PostgresContainerFixture fixture)
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
        services.AddSingleton<IRenewalAlertScanner, RenewalAlertScanner>();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _dbContext.DisposeAsync();
    }

    private RenewalAlertBackgroundService BuildService()
    {
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var scanner = _serviceProvider.GetRequiredService<IRenewalAlertScanner>();
        var logger = _serviceProvider.GetRequiredService<ILogger<RenewalAlertBackgroundService>>();
        return new RenewalAlertBackgroundService(scopeFactory, scanner, logger);
    }

    private async Task<UserSubscription> CreateSubscriptionAsync(int alertDaysAdvance, bool isActive = true, DateOnly? nextBillingDate = null)
    {
        var user = new User { Email = $"renewal-{Guid.NewGuid()}@example.com", PasswordHash = "not-a-real-hash", PreferredCurrency = "USD", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var subscription = new UserSubscription
        {
            UserId = user.Id,
            CustomName = "Netflix",
            CostAmount = 15.49m,
            Currency = "USD",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            // Default: due exactly today given alertDaysAdvance, unless the test overrides it.
            NextBillingDate = nextBillingDate ?? Today.AddDays(alertDaysAdvance),
            AlertDaysAdvance = alertDaysAdvance,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.UserSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        return subscription;
    }

    [Fact]
    public async Task RenewalScan_FindsSubscriptionsDueWithinAlertWindow_CreatesNotificationLogRow()
    {
        var subscription = await CreateSubscriptionAsync(alertDaysAdvance: 3);

        await BuildService().ScanOnceAsync(Today);

        var logs = await _dbContext.NotificationsLog.AsNoTracking()
            .Where(n => n.UserSubscriptionId == subscription.Id)
            .ToListAsync();
        var log = Assert.Single(logs);
        Assert.Equal(3, log.AlertDaysAdvance);
    }

    [Fact]
    public async Task RenewalScan_RunTwiceForSameDay_DoesNotCreateDuplicateNotificationLogRow()
    {
        var subscription = await CreateSubscriptionAsync(alertDaysAdvance: 3);

        await BuildService().ScanOnceAsync(Today);
        await BuildService().ScanOnceAsync(Today);

        var logs = await _dbContext.NotificationsLog.AsNoTracking()
            .Where(n => n.UserSubscriptionId == subscription.Id)
            .ToListAsync();
        Assert.Single(logs);
    }

    [Fact]
    public async Task RenewalScan_IgnoresInactiveSubscriptions()
    {
        var subscription = await CreateSubscriptionAsync(alertDaysAdvance: 3, isActive: false);

        await BuildService().ScanOnceAsync(Today);

        var hasLog = await _dbContext.NotificationsLog.AsNoTracking().AnyAsync(n => n.UserSubscriptionId == subscription.Id);
        Assert.False(hasLog);
    }

    [Fact]
    public async Task RenewalScan_IgnoresSubscriptionsOutsideTheAlertWindow()
    {
        // Renews in 20 days but alert window is 10 days out - not due today.
        var subscription = await CreateSubscriptionAsync(alertDaysAdvance: 10, nextBillingDate: Today.AddDays(20));

        await BuildService().ScanOnceAsync(Today);

        var hasLog = await _dbContext.NotificationsLog.AsNoTracking().AnyAsync(n => n.UserSubscriptionId == subscription.Id);
        Assert.False(hasLog);
    }
}

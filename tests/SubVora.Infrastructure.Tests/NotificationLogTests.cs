using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class NotificationLogTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public NotificationLogTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = AppDbContextOptionsFactory.Build(_fixture.ConnectionString);
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    private async Task<UserSubscription> CreateSubscriptionAsync()
    {
        var user = new User { Email = $"notif-{Guid.NewGuid()}@example.com", PasswordHash = "not-a-real-hash", PreferredCurrency = "USD", CreatedAt = DateTimeOffset.UtcNow };
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
            NextBillingDate = new DateOnly(2026, 8, 1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.UserSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        return subscription;
    }

    [Fact]
    public async Task NotificationLog_PersistsAndReferencesUserSubscription()
    {
        var subscription = await CreateSubscriptionAsync();

        var log = new NotificationLog
        {
            UserSubscriptionId = subscription.Id,
            AlertDaysAdvance = 3,
            SentAt = DateTimeOffset.UtcNow,
        };
        _dbContext.NotificationsLog.Add(log);
        await _dbContext.SaveChangesAsync();

        var reloaded = await _dbContext.NotificationsLog.AsNoTracking().SingleAsync(n => n.Id == log.Id);
        Assert.Equal(subscription.Id, reloaded.UserSubscriptionId);
        Assert.Equal(3, reloaded.AlertDaysAdvance);
    }

    [Fact]
    public async Task NotificationLog_DeletingUserSubscription_CascadesDelete()
    {
        var subscription = await CreateSubscriptionAsync();

        var log = new NotificationLog
        {
            UserSubscriptionId = subscription.Id,
            AlertDaysAdvance = 7,
            SentAt = DateTimeOffset.UtcNow,
        };
        _dbContext.NotificationsLog.Add(log);
        await _dbContext.SaveChangesAsync();

        _dbContext.UserSubscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync();

        var stillExists = await _dbContext.NotificationsLog.AsNoTracking().AnyAsync(n => n.Id == log.Id);
        Assert.False(stillExists);
    }

    [Fact]
    public async Task NotificationLog_DuplicateSubscriptionAlertDaysSentAt_ViolatesUniqueConstraint()
    {
        var subscription = await CreateSubscriptionAsync();
        var sentAt = DateTimeOffset.UtcNow;

        _dbContext.NotificationsLog.Add(new NotificationLog { UserSubscriptionId = subscription.Id, AlertDaysAdvance = 3, SentAt = sentAt });
        await _dbContext.SaveChangesAsync();

        _dbContext.NotificationsLog.Add(new NotificationLog { UserSubscriptionId = subscription.Id, AlertDaysAdvance = 3, SentAt = sentAt });

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }
}

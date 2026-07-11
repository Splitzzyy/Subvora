using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class UserSubscriptionTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public UserSubscriptionTests(PostgresContainerFixture fixture)
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

    [Fact]
    public async Task UserSubscription_PersistsAndResolvesAllFourForeignKeys()
    {
        var user = new User { Email = $"subs-{Guid.NewGuid()}@example.com", PasswordHash = "not-a-real-hash", PreferredCurrency = "USD", CreatedAt = DateTimeOffset.UtcNow };
        var category = new Category { UserId = null, Name = $"Cat-{Guid.NewGuid()}", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Users.Add(user);
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        var paymentSource = new PaymentSource { UserId = user.Id, Label = "Visa •1234", SourceType = PaymentSourceType.Card, CreatedAt = DateTimeOffset.UtcNow };
        var catalogItem = new SubscriptionCatalogItem { ProviderName = $"Netflix-{Guid.NewGuid()}", CategoryId = category.Id, CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.PaymentSources.Add(paymentSource);
        _dbContext.SubscriptionCatalog.Add(catalogItem);
        await _dbContext.SaveChangesAsync();

        var subscription = new UserSubscription
        {
            UserId = user.Id,
            CatalogId = catalogItem.Id,
            CategoryId = category.Id,
            PaymentSourceId = paymentSource.Id,
            CustomName = "Netflix Premium",
            CostAmount = 19.99m,
            Currency = "USD",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 8, 1),
            AlertDaysAdvance = 3,
            IsFreeTrial = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.UserSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        var reloaded = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(user.Id, reloaded.UserId);
        Assert.Equal(catalogItem.Id, reloaded.CatalogId);
        Assert.Equal(category.Id, reloaded.CategoryId);
        Assert.Equal(paymentSource.Id, reloaded.PaymentSourceId);
        Assert.Equal(BillingCycleType.Monthly, reloaded.CycleCadence);
    }

    [Fact]
    public async Task UserSubscription_DeletingCategoryCatalogOrPaymentSource_SetsFkNull_ButDeletingUserCascades()
    {
        var user = new User { Email = $"cascade-{Guid.NewGuid()}@example.com", PasswordHash = "not-a-real-hash", PreferredCurrency = "USD", CreatedAt = DateTimeOffset.UtcNow };
        var category = new Category { UserId = null, Name = $"Cat-{Guid.NewGuid()}", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Users.Add(user);
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        var paymentSource = new PaymentSource { UserId = user.Id, Label = "Visa •5678", SourceType = PaymentSourceType.Card, CreatedAt = DateTimeOffset.UtcNow };
        var catalogItem = new SubscriptionCatalogItem { ProviderName = $"Spotify-{Guid.NewGuid()}", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.PaymentSources.Add(paymentSource);
        _dbContext.SubscriptionCatalog.Add(catalogItem);
        await _dbContext.SaveChangesAsync();

        var subscription = new UserSubscription
        {
            UserId = user.Id,
            CatalogId = catalogItem.Id,
            CategoryId = category.Id,
            PaymentSourceId = paymentSource.Id,
            CustomName = "Spotify Family",
            CostAmount = 16.99m,
            Currency = "USD",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 8, 1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.UserSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        _dbContext.Categories.Remove(category);
        _dbContext.PaymentSources.Remove(paymentSource);
        _dbContext.SubscriptionCatalog.Remove(catalogItem);
        await _dbContext.SaveChangesAsync();

        var afterSetNulls = await _dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == subscription.Id);
        Assert.Null(afterSetNulls.CategoryId);
        Assert.Null(afterSetNulls.PaymentSourceId);
        Assert.Null(afterSetNulls.CatalogId);

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        var stillExists = await _dbContext.UserSubscriptions.AsNoTracking().AnyAsync(s => s.Id == subscription.Id);
        Assert.False(stillExists);
    }

    [Fact]
    public async Task Migration_CreatesUserIndexAndPartialNextBillingIndex()
    {
        var connection = (Npgsql.NpgsqlConnection)_dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'user_subscriptions' AND indexname IN ('idx_subs_user_id', 'idx_subs_next_billing')";
        var found = new Dictionary<string, string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                found[reader.GetString(0)] = reader.GetString(1);
            }
        }

        Assert.True(found.ContainsKey("idx_subs_user_id"));
        Assert.True(found.ContainsKey("idx_subs_next_billing"));
        Assert.Contains("WHERE (is_active = true)", found["idx_subs_next_billing"], StringComparison.OrdinalIgnoreCase);
    }
}

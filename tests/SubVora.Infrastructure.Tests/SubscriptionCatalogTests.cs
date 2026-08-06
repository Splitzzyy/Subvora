using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Migrations;

namespace SubVora.Infrastructure.Tests;

public class SubscriptionCatalogTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public SubscriptionCatalogTests(PostgresContainerFixture fixture)
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
    public async Task SubscriptionCatalog_CategoryDeleted_SetsCategoryIdNull()
    {
        _dbContext.Categories.Add(new Category { UserId = null, Name = $"TempCategory-{Guid.NewGuid()}", CreatedAt = DateTimeOffset.UtcNow });
        var category = _dbContext.Categories.Local.Single();
        await _dbContext.SaveChangesAsync();

        var catalogItem = new SubscriptionCatalogItem
        {
            ProviderName = $"Netflix-{Guid.NewGuid()}",
            CategoryId = category.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.Add(catalogItem);
        await _dbContext.SaveChangesAsync();

        _dbContext.Categories.Remove(category);
        await _dbContext.SaveChangesAsync();

        var reloaded = await _dbContext.SubscriptionCatalog
            .AsNoTracking()
            .SingleAsync(c => c.Id == catalogItem.Id);
        Assert.Null(reloaded.CategoryId);
    }

    [Fact]
    public async Task SeedMigration_PopulatesTheCatalogWithLogoAndCategoryForEverySeededProvider()
    {
        var seededIds = SeedSubscriptionCatalog.SeededIds;
        var seeded = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .Where(item => seededIds.Contains(item.Id))
            .ToListAsync();

        Assert.Equal(seededIds.Count, seeded.Count);
        Assert.All(seeded, item => Assert.False(string.IsNullOrWhiteSpace(item.LogoUrl)));
        Assert.All(seeded, item => Assert.NotNull(item.CategoryId));

        var categoryIds = await _dbContext.Categories.AsNoTracking().Select(category => category.Id).ToListAsync();
        Assert.All(seeded, item => Assert.Contains(item.CategoryId!.Value, categoryIds));
    }

    [Fact]
    public async Task SeedMigration_ProviderNamesAreUnique()
    {
        var seededIds = SeedSubscriptionCatalog.SeededIds;
        var providerNames = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .Where(item => seededIds.Contains(item.Id))
            .Select(item => item.ProviderName)
            .ToListAsync();

        Assert.Equal(providerNames.Count, providerNames.Distinct().Count());
    }

    [Fact]
    public async Task SeedMigration_LeavesEverySeededRowImmediatelyMatchable()
    {
        // The embedding era needed a backfill pass before a seeded row could be found at all.
        // Trigram matching reads provider_name directly, so seeding is the only step - nothing
        // asynchronous stands between the migration and a working match.
        var repository = new SubVora.Infrastructure.Repositories.SubscriptionCatalogSearchRepository(_dbContext);
        var seededNames = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .Where(item => SeedSubscriptionCatalog.SeededIds.Contains(item.Id))
            .Select(item => item.ProviderName)
            .ToListAsync();

        Assert.NotEmpty(seededNames);
        foreach (var name in seededNames)
        {
            var match = await repository.FindNearestAsync(name);
            Assert.Equal(name, match?.ProviderName);
        }
    }
}

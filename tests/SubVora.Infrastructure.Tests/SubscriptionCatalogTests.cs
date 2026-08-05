using Microsoft.EntityFrameworkCore;
using Pgvector;
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
    public async Task SubscriptionCatalog_CosineDistanceQuery_ReturnsClosestVectorFirst()
    {
        var queryVector = BuildVector((0, 1f));

        var exactMatch = new SubscriptionCatalogItem
        {
            ProviderName = $"ExactMatch-{Guid.NewGuid()}",
            SemanticEmbedding = BuildVector((0, 1f)),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var nearMatch = new SubscriptionCatalogItem
        {
            ProviderName = $"NearMatch-{Guid.NewGuid()}",
            SemanticEmbedding = BuildVector((0, 0.9f), (1, 0.1f)),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var orthogonal = new SubscriptionCatalogItem
        {
            ProviderName = $"Orthogonal-{Guid.NewGuid()}",
            SemanticEmbedding = BuildVector((1, 1f)),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Insert deliberately out of expected-result order to prove the DB, not insert order, drives ranking.
        _dbContext.SubscriptionCatalog.AddRange(orthogonal, exactMatch, nearMatch);
        await _dbContext.SaveChangesAsync();

        var results = await _dbContext.SubscriptionCatalog
            .FromSqlInterpolated($"SELECT * FROM subscription_catalog ORDER BY semantic_embedding <=> {queryVector} LIMIT 3")
            .ToListAsync();

        var orderedNames = results.Select(r => r.ProviderName).ToList();
        Assert.Equal(
            [exactMatch.ProviderName, nearMatch.ProviderName, orthogonal.ProviderName],
            orderedNames);
    }

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
    public async Task SeedMigration_LeavesEverySeededRowWithoutAnEmbedding()
    {
        // Deliberate and contained: migrations must not make network calls, so seeded rows are
        // invisible to FindNearestAsync (which filters out null embeddings) until
        // CatalogEmbeddingBackfillService has run - see CatalogEmbeddingBackfillTests.
        var seededIds = SeedSubscriptionCatalog.SeededIds;
        var withEmbedding = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .CountAsync(item => seededIds.Contains(item.Id) && item.SemanticEmbedding != null);

        Assert.Equal(0, withEmbedding);
    }

    private static Vector BuildVector(params (int index, float value)[] nonZeroEntries)
    {
        var values = new float[1536];
        foreach (var (index, value) in nonZeroEntries)
        {
            values[index] = value;
        }

        return new Vector(values);
    }
}

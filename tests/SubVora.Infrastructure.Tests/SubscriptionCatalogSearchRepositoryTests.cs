using Microsoft.EntityFrameworkCore;
using Pgvector;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Repositories;

namespace SubVora.Infrastructure.Tests;

public class SubscriptionCatalogSearchRepositoryTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;
    private SubscriptionCatalogSearchRepository _repository = null!;

    public SubscriptionCatalogSearchRepositoryTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = AppDbContextOptionsFactory.Build(_fixture.ConnectionString);
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();
        // IClassFixture reuses the same container/database across every test method in this
        // class, so start each test from a clean table rather than relying on insert order.
        await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE subscription_catalog CASCADE");
        _repository = new SubscriptionCatalogSearchRepository(_dbContext);
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task FindNearestAsync_ReturnsClosestCatalogRow_WithItsCosineDistance()
    {
        var exactMatch = new SubscriptionCatalogItem
        {
            ProviderName = $"ExactMatch-{Guid.NewGuid()}",
            SemanticEmbedding = new Vector(BuildVector((0, 1f))),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var farMatch = new SubscriptionCatalogItem
        {
            ProviderName = $"FarMatch-{Guid.NewGuid()}",
            SemanticEmbedding = new Vector(BuildVector((1, 1f))),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.AddRange(farMatch, exactMatch);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindNearestAsync(BuildVector((0, 1f)));

        Assert.NotNull(result);
        Assert.Equal(exactMatch.Id, result.CatalogId);
        Assert.Equal(exactMatch.ProviderName, result.ProviderName);
        Assert.Equal(0d, result.Distance, precision: 5);
    }

    [Fact]
    public async Task FindNearestAsync_EmptyCatalog_ReturnsNull()
    {
        var result = await _repository.FindNearestAsync(BuildVector((0, 1f)));

        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_PersistsNewCatalogRowWithEmbedding()
    {
        var providerName = $"NewProvider-{Guid.NewGuid()}";

        var id = await _repository.AddAsync(providerName, BuildVector((2, 1f)));

        var reloaded = await _dbContext.SubscriptionCatalog.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(providerName, reloaded.ProviderName);
        Assert.NotNull(reloaded.SemanticEmbedding);
    }

    private static float[] BuildVector(params (int index, float value)[] nonZeroEntries)
    {
        var values = new float[1536];
        foreach (var (index, value) in nonZeroEntries)
        {
            values[index] = value;
        }

        return values;
    }
}

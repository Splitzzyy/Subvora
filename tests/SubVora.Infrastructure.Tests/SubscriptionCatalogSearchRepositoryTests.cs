using Microsoft.EntityFrameworkCore;
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
        // The seeded providers are exercised separately in SubscriptionCatalogTrigramMatchTests.
        await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE subscription_catalog CASCADE");
        _repository = new SubscriptionCatalogSearchRepository(_dbContext);
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task FindNearestAsync_ReturnsBestScoringRow_WithItsFields()
    {
        var categoryId = (await _dbContext.Categories.FirstAsync()).Id;
        var match = new SubscriptionCatalogItem
        {
            ProviderName = "Spotify",
            CategoryId = categoryId,
            LogoUrl = "https://cdn.example/spotify.png",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var other = new SubscriptionCatalogItem { ProviderName = "Dropbox", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.SubscriptionCatalog.AddRange(other, match);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindNearestAsync("spotify");

        Assert.NotNull(result);
        Assert.Equal(match.Id, result.CatalogId);
        Assert.Equal("Spotify", result.ProviderName);
        Assert.Equal(categoryId, result.CategoryId);
        Assert.Equal("https://cdn.example/spotify.png", result.LogoUrl);
        Assert.Equal(1d, result.Score, precision: 5);
    }

    [Fact]
    public async Task FindNearestAsync_MatchesASubstringOfALongerProviderName()
    {
        // The direction plain similarity() gets wrong: "adobe" scores 0.300 against
        // "Adobe Creative Cloud", down among the wrong answers. word_similarity scores it 1.0.
        _dbContext.SubscriptionCatalog.Add(new SubscriptionCatalogItem
        {
            ProviderName = "Adobe Creative Cloud",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindNearestAsync("adobe");

        Assert.NotNull(result);
        Assert.Equal("Adobe Creative Cloud", result.ProviderName);
        Assert.Equal(1d, result.Score, precision: 5);
    }

    [Fact]
    public async Task FindNearestAsync_MatchesWhenTheInputIsLongerThanTheProviderName()
    {
        // The other direction: "Netflix Premium" contains "Netflix", so the reversed
        // word_similarity call is the one that scores 1.0 here.
        _dbContext.SubscriptionCatalog.Add(new SubscriptionCatalogItem
        {
            ProviderName = "Netflix",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindNearestAsync("Netflix Premium");

        Assert.NotNull(result);
        Assert.Equal("Netflix", result.ProviderName);
        Assert.Equal(1d, result.Score, precision: 5);
    }

    [Fact]
    public async Task FindNearestAsync_TiedScores_ResolveDeterministically()
    {
        // "youtube" scores 1.0 against both seeded YouTube rows. LIMIT 1 has to pick one, and the
        // provider_name tiebreak keeps that choice stable rather than left to the planner.
        _dbContext.SubscriptionCatalog.AddRange(
            new SubscriptionCatalogItem { ProviderName = "YouTube Premium", CreatedAt = DateTimeOffset.UtcNow },
            new SubscriptionCatalogItem { ProviderName = "YouTube Music", CreatedAt = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync();

        var first = await _repository.FindNearestAsync("youtube");
        var second = await _repository.FindNearestAsync("youtube");

        Assert.NotNull(first);
        Assert.Equal("YouTube Music", first.ProviderName);
        Assert.Equal(first.ProviderName, second?.ProviderName);
    }

    [Fact]
    public async Task FindNearestAsync_EmptyCatalog_ReturnsNull()
    {
        var result = await _repository.FindNearestAsync("netflix");

        Assert.Null(result);
    }
}

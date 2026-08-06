using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Catalog;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Repositories;

namespace SubVora.Infrastructure.Tests;

public class SubscriptionCatalogSyncTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public SubscriptionCatalogSyncTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _dbContext = new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString));
        await _dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public void CatalogFile_IsEmbeddedAndWellFormed()
    {
        var providers = SubscriptionCatalogFile.Read();

        Assert.NotEmpty(providers);
        Assert.All(providers, provider =>
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.ProviderName));
            Assert.False(string.IsNullOrWhiteSpace(provider.Category));
        });
    }

    [Fact]
    public void CatalogFile_HasNoDuplicateProviderNames()
    {
        // provider_name is uniquely indexed, so a duplicate here would be silently swallowed by
        // ON CONFLICT DO NOTHING - the second entry would simply never appear.
        var names = SubscriptionCatalogFile.Read().Select(provider => provider.ProviderName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task CatalogFile_OnlyNamesCategoriesThatExist()
    {
        // A provider naming a category the system does not seed still lands, but uncategorised -
        // this catches the typo before a user sees a brand with no category.
        var systemCategoryNames = await _dbContext.Categories.AsNoTracking()
            .Where(category => category.UserId == null)
            .Select(category => category.Name)
            .ToListAsync();

        var unknown = SubscriptionCatalogFile.Read()
            .Select(provider => provider.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(category => !systemCategoryNames.Contains(category, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void LogoUrl_IsNullWhenTheBrandHasNoIcon()
    {
        // simple-icons v13 dropped several brands for trademark reasons. Those still belong in the
        // catalog - matching does not need a logo, and the mobile list renders its placeholder.
        var withoutIcon = new SubscriptionCatalogProvider("Disney+", "Entertainment", IconSlug: null);
        var withIcon = new SubscriptionCatalogProvider("Netflix", "Entertainment", "netflix");

        Assert.Null(withoutIcon.LogoUrl);
        Assert.Equal("https://cdn.jsdelivr.net/npm/simple-icons@13/icons/netflix.svg", withIcon.LogoUrl);
    }

    [Fact]
    public async Task SyncAsync_InsertsEveryFileProvider_IntoAnEmptyCatalog()
    {
        await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE subscription_catalog CASCADE");
        var expected = SubscriptionCatalogFile.Read();

        var inserted = await SubscriptionCatalogSyncService.SyncAsync(_dbContext);

        Assert.Equal(expected.Count, inserted);

        var stored = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .Select(item => item.ProviderName)
            .ToListAsync();
        Assert.Equal(expected.Count, stored.Count);
        Assert.All(expected, provider => Assert.Contains(provider.ProviderName, stored));
    }

    [Fact]
    public async Task SyncAsync_IsIdempotent_AndCostsNothingOnceInSync()
    {
        await SubscriptionCatalogSyncService.SyncAsync(_dbContext);
        var countAfterFirst = await _dbContext.SubscriptionCatalog.CountAsync();

        var insertedOnSecondRun = await SubscriptionCatalogSyncService.SyncAsync(_dbContext);

        Assert.Equal(0, insertedOnSecondRun);
        Assert.Equal(countAfterFirst, await _dbContext.SubscriptionCatalog.CountAsync());
    }

    [Fact]
    public async Task SyncAsync_AddsOnlyTheNewBrand_AndLeavesEverythingElseAlone()
    {
        await SubscriptionCatalogSyncService.SyncAsync(_dbContext);

        var removed = await _dbContext.SubscriptionCatalog.SingleAsync(item => item.ProviderName == "Disney+");
        var untouchedBefore = await _dbContext.SubscriptionCatalog.CountAsync();
        _dbContext.SubscriptionCatalog.Remove(removed);
        await _dbContext.SaveChangesAsync();

        var inserted = await SubscriptionCatalogSyncService.SyncAsync(_dbContext);

        Assert.Equal(1, inserted);
        Assert.Equal(untouchedBefore, await _dbContext.SubscriptionCatalog.CountAsync());
    }

    [Fact]
    public async Task SyncAsync_NeverOverwritesARowThatAlreadyExists()
    {
        // A hand-edited logo or category in the database is not clobbered by a later start:
        // the file adds what is missing, it does not own what is already there.
        await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE subscription_catalog CASCADE");
        _dbContext.SubscriptionCatalog.Add(new SubscriptionCatalogItem
        {
            ProviderName = "Netflix",
            LogoUrl = "https://cdn.example/hand-picked-netflix.svg",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        await SubscriptionCatalogSyncService.SyncAsync(_dbContext);

        var netflix = await _dbContext.SubscriptionCatalog.AsNoTracking().SingleAsync(item => item.ProviderName == "Netflix");
        Assert.Equal("https://cdn.example/hand-picked-netflix.svg", netflix.LogoUrl);
    }

    [Fact]
    public async Task SyncAsync_ResolvesTheCategoryNamedInTheFile()
    {
        await SubscriptionCatalogSyncService.SyncAsync(_dbContext);

        var entertainment = await _dbContext.Categories.AsNoTracking()
            .SingleAsync(category => category.UserId == null && category.Name == "Entertainment");
        var hulu = await _dbContext.SubscriptionCatalog.AsNoTracking().SingleAsync(item => item.ProviderName == "Hulu");

        Assert.Equal(entertainment.Id, hulu.CategoryId);
    }

    [Fact]
    public async Task ABrandAddedToTheFile_IsMatchableImmediately()
    {
        // The whole point of the trigram design: the row is the index, so there is no backfill
        // step standing between "added to the file" and "a user can find it".
        await SubscriptionCatalogSyncService.SyncAsync(_dbContext);
        var repository = new SubscriptionCatalogSearchRepository(_dbContext);

        var result = await repository.FindNearestAsync("disney");

        Assert.NotNull(result);
        Assert.Equal("Disney+", result.ProviderName);
    }
}

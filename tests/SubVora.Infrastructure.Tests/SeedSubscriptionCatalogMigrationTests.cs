using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Migrations;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// Drives the seed migration's own Up/Down SQL against a database, rather than only observing the
/// state EF left behind. Its own class (and therefore its own container) because the round trip
/// deletes and re-inserts the seeded rows, which would sabotage the assertions in
/// <see cref="SubscriptionCatalogTests"/>.
/// </summary>
public class SeedSubscriptionCatalogMigrationTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public SeedSubscriptionCatalogMigrationTests(PostgresContainerFixture fixture)
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
    public async Task DownThenUp_RemovesOnlyTheSeededRowsAndReInsertsThemWithoutColliding()
    {
        var seededIds = SeedSubscriptionCatalog.SeededIds;

        // A Manual-tier row of the kind SubscriptionMatchService writes from raw free-text input.
        var userGenerated = new SubscriptionCatalogItem
        {
            ProviderName = $"UserGenerated-{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.Add(userGenerated);
        await _dbContext.SaveChangesAsync();

        await _dbContext.Database.ExecuteSqlRawAsync(MigrationSql("Down"));

        var remainingSeeded = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .CountAsync(item => seededIds.Contains(item.Id));
        Assert.Equal(0, remainingSeeded);
        Assert.True(
            await _dbContext.SubscriptionCatalog.AsNoTracking().AnyAsync(item => item.Id == userGenerated.Id),
            "Down must not remove user-generated catalog rows.");

        // Now claim a seeded provider's name for a user-generated row, so re-applying the seed hits
        // the unique provider_name index head-on.
        var squatter = new SubscriptionCatalogItem
        {
            ProviderName = "Netflix",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.Add(squatter);
        await _dbContext.SaveChangesAsync();

        await _dbContext.Database.ExecuteSqlRawAsync(MigrationSql("Up"));

        var reSeeded = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .CountAsync(item => seededIds.Contains(item.Id));
        // Every seeded row returns except Netflix, whose name the squatter now holds.
        Assert.Equal(seededIds.Count - 1, reSeeded);

        var survivingSquatter = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .SingleAsync(item => item.ProviderName == "Netflix");
        Assert.Equal(squatter.Id, survivingSquatter.Id);
    }

    /// <summary>Collects the raw SQL a migration method emits. Up/Down are protected, hence reflection.</summary>
    private static string MigrationSql(string methodName)
    {
        var migration = new SeedSubscriptionCatalog();
        var builder = new MigrationBuilder(activeProvider: null);
        typeof(SeedSubscriptionCatalog)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        return string.Join("\n", builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
    }
}

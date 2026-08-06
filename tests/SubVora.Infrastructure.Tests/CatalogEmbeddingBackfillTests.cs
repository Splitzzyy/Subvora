using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pgvector;
using SubVora.Application.Matching;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Ai;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Repositories;

namespace SubVora.Infrastructure.Tests;

public class CatalogEmbeddingBackfillTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;
    private FakeEmbeddingClient _embeddingClient = null!;
    private ServiceProvider _serviceProvider = null!;

    public CatalogEmbeddingBackfillTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = AppDbContextOptionsFactory.Build(_fixture.ConnectionString);
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();

        _embeddingClient = new FakeEmbeddingClient();
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString)));
        services.AddSingleton<IEmbeddingClient>(_embeddingClient);
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _dbContext.DisposeAsync();
    }

    private CatalogEmbeddingBackfillService BuildService(ServiceProvider? serviceProvider = null)
    {
        var provider = serviceProvider ?? _serviceProvider;
        return new CatalogEmbeddingBackfillService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<CatalogEmbeddingBackfillService>>());
    }

    [Fact]
    public async Task BackfillOnce_GivesEveryCatalogRowWithoutAnEmbeddingOne()
    {
        // A row in the state the seed migration leaves them in - the class fixture shares one
        // database across test methods, so this must not assume the seeded rows are still bare.
        var unembedded = new SubscriptionCatalogItem
        {
            ProviderName = $"Unembedded-{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.Add(unembedded);
        await _dbContext.SaveChangesAsync();

        await BuildService().BackfillOnceAsync();

        var withoutEmbedding = await _dbContext.SubscriptionCatalog.AsNoTracking()
            .CountAsync(item => item.SemanticEmbedding == null);
        Assert.Equal(0, withoutEmbedding);
        Assert.Contains(unembedded.ProviderName, _embeddingClient.EmbeddedTexts);
    }

    [Fact]
    public async Task BackfillOnce_RunTwice_MakesNoEmbeddingCallsOnTheSecondPass()
    {
        await BuildService().BackfillOnceAsync();
        var callsAfterFirstPass = _embeddingClient.CallCount;

        await BuildService().BackfillOnceAsync();

        Assert.Equal(callsAfterFirstPass, _embeddingClient.CallCount);
    }

    [Fact]
    public async Task BackfillOnce_LeavesRowsThatAlreadyHaveAnEmbeddingAlone()
    {
        var providerName = $"AlreadyEmbedded-{Guid.NewGuid()}";
        var existing = new SubscriptionCatalogItem
        {
            ProviderName = providerName,
            SemanticEmbedding = new Vector(FakeEmbeddingClient.Embed("something else entirely")),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.Add(existing);
        await _dbContext.SaveChangesAsync();
        var originalEmbedding = existing.SemanticEmbedding!.ToArray();

        await BuildService().BackfillOnceAsync();

        Assert.DoesNotContain(providerName, _embeddingClient.EmbeddedTexts);
        var reloaded = await _dbContext.SubscriptionCatalog.AsNoTracking().SingleAsync(item => item.Id == existing.Id);
        Assert.Equal(originalEmbedding, reloaded.SemanticEmbedding!.ToArray());
    }

    [Fact]
    public async Task BackfillOnce_WithNoEmbeddingClientRegistered_LogsAndDoesNotThrow()
    {
        // Mirrors an unconfigured OpenAI key: Program.cs's typed HttpClient factory throws on
        // resolution, and a hosted service must not take the host down over an optional integration.
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString)));
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await BuildService(provider).BackfillOnceAsync();

        Assert.Equal(0, _embeddingClient.CallCount);
    }

    [Fact]
    public async Task BackfillOnce_LeavingRowsUnembedded_ReportsNotDoneSoTheServiceRetries()
    {
        var throwingProvider = new ServiceCollection();
        throwingProvider.AddScoped(_ => new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString)));
        throwingProvider.AddSingleton<IEmbeddingClient>(new ThrowingEmbeddingClient());
        throwingProvider.AddLogging();
        await using var provider = throwingProvider.BuildServiceProvider();

        _dbContext.SubscriptionCatalog.Add(new SubscriptionCatalogItem
        {
            ProviderName = $"Unembeddable-{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => BuildService(provider).BackfillOnceAsync());

        // The row survives for the retry rather than being marked done or skipped.
        Assert.True(await _dbContext.SubscriptionCatalog.AsNoTracking().AnyAsync(item => item.SemanticEmbedding == null));

        // ...and the next pass, with a working client, finishes the job and reports done.
        Assert.True(await BuildService().BackfillOnceAsync());
    }

    [Fact]
    public async Task BackfillOnce_WhileAnotherInstanceHoldsTheLock_DoesNoEmbeddingWorkAndAsksToRetry()
    {
        _dbContext.SubscriptionCatalog.Add(new SubscriptionCatalogItem
        {
            ProviderName = $"Contended-{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        // Stand in for a second replica: hold the same advisory lock on a separate connection.
        await using var holder = new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString));
        await holder.Database.OpenConnectionAsync();
        try
        {
            await holder.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(4713100)");

            var callsBefore = _embeddingClient.CallCount;
            var done = await BuildService().BackfillOnceAsync();

            Assert.False(done);
            Assert.Equal(callsBefore, _embeddingClient.CallCount);
        }
        finally
        {
            await holder.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(4713100)");
            await holder.Database.CloseConnectionAsync();
        }

        // Lock released - the work still gets done on a later pass.
        Assert.True(await BuildService().BackfillOnceAsync());
    }

    [Fact]
    public async Task BackfillOnce_WithNothingLeftToEmbed_ReportsDoneSoTheServiceStops()
    {
        await BuildService().BackfillOnceAsync();

        Assert.True(await BuildService().BackfillOnceAsync());
    }

    private sealed class ThrowingEmbeddingClient : IEmbeddingClient
    {
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Simulated OpenAI outage.");
    }

    [Fact]
    public async Task AfterSeedAndBackfill_FindNearestAsync_ResolvesANearMissToTheSeededProvider()
    {
        await BuildService().BackfillOnceAsync();
        var repository = new SubscriptionCatalogSearchRepository(_dbContext);

        var result = await repository.FindNearestAsync(FakeEmbeddingClient.Embed("netflx"));

        Assert.NotNull(result);
        Assert.Equal("Netflix", result.ProviderName);
        Assert.NotNull(result.LogoUrl);
        Assert.NotNull(result.CategoryId);
    }
}

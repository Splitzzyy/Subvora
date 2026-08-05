using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pgvector;
using SubVora.Application.Matching;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Ai;

/// <summary>
/// Generates embeddings for subscription_catalog rows that lack them - notably the rows inserted by
/// the SeedSubscriptionCatalog migration, which cannot call OpenAI itself. Runs once at startup
/// rather than on a loop: rows only arrive with a null embedding from a migration, and everything
/// written at runtime by SubscriptionMatchService already carries one.
///
/// Safe to re-run: it only touches rows where semantic_embedding IS NULL, so a second pass after a
/// partial failure resumes rather than re-billing OpenAI for work already done.
/// </summary>
public class CatalogEmbeddingBackfillService : BackgroundService
{
    // Batched so a mid-run failure keeps the work already committed, rather than rolling back the
    // whole catalog and re-charging for it on the next start.
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogEmbeddingBackfillService> _logger;

    public CatalogEmbeddingBackfillService(IServiceScopeFactory scopeFactory, ILogger<CatalogEmbeddingBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await BackfillOnceAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A hosted service must never take the host down; the backfill retries on next start.
            _logger.LogError(ex, "Catalog embedding backfill failed; catalog rows without an embedding stay unmatchable until the next start.");
        }
    }

    /// <summary>Runs a single backfill pass. Public so tests can drive one pass directly.</summary>
    public async Task BackfillOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Resolved lazily and guarded, mirroring RenewalAlertBackgroundService's push-sender block:
        // the typed HttpClient factory throws when OpenAI:ApiKey is unset, and an unconfigured
        // optional integration must degrade to "skip the backfill", not crash startup.
        IEmbeddingClient embeddingClient;
        try
        {
            embeddingClient = scope.ServiceProvider.GetRequiredService<IEmbeddingClient>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Embedding client is not available; skipping the catalog embedding backfill.");
            return;
        }

        var embedded = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await dbContext.SubscriptionCatalog
                .Where(item => item.SemanticEmbedding == null)
                .OrderBy(item => item.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var item in batch)
            {
                var embedding = await embeddingClient.GetEmbeddingAsync(item.ProviderName, cancellationToken);
                item.SemanticEmbedding = new Vector(embedding);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            embedded += batch.Count;
        }

        var remaining = await dbContext.SubscriptionCatalog.CountAsync(item => item.SemanticEmbedding == null, cancellationToken);
        _logger.LogInformation("Catalog embedding backfill embedded {Embedded} row(s); {Remaining} still without an embedding.", embedded, remaining);
    }
}

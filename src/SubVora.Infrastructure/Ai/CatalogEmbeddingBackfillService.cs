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
/// the SeedSubscriptionCatalog migration, which cannot call OpenAI itself. Rows only arrive with a
/// null embedding from a migration, so there is normally nothing to do after the first pass.
///
/// Retries on an interval rather than running once at startup. Production migrations are a deploy
/// step, not startup, so the app can legitimately start before the rows exist; and a transient
/// OpenAI failure used to leave the catalog unmatchable until someone restarted the process, with
/// every resolve silently falling back to the Manual tier.
///
/// Safe to re-run: it only touches rows where semantic_embedding IS NULL, so a second pass after a
/// partial failure resumes rather than re-billing OpenAI for work already done. Across instances,
/// a Postgres advisory lock keeps that bill at one instance's worth rather than N.
/// </summary>
public class CatalogEmbeddingBackfillService : BackgroundService
{
    // Batched so a mid-run failure keeps the work already committed, rather than rolling back the
    // whole catalog and re-charging for it on the next start.
    private const int BatchSize = 20;

    /// <summary>Arbitrary but fixed - advisory lock keys are a global namespace, so this one is the catalog backfill's.</summary>
    private const long AdvisoryLockKey = 4_713_100L;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogEmbeddingBackfillService> _logger;

    public CatalogEmbeddingBackfillService(IServiceScopeFactory scopeFactory, ILogger<CatalogEmbeddingBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await BackfillOnceAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A hosted service must never take the host down.
                _logger.LogError(ex, "Catalog embedding backfill failed; retrying in {RetryMinutes} minute(s).", RetryInterval.TotalMinutes);
            }

            try
            {
                await Task.Delay(RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Runs a single backfill pass. Returns true when there is nothing further to do - either every
    /// row is embedded, or OpenAI is unconfigured and never will be by retrying. Public so tests can
    /// drive one pass directly.
    /// </summary>
    public async Task<bool> BackfillOnceAsync(CancellationToken cancellationToken = default)
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
            return true;
        }

        // Session-scoped advisory lock, held on one explicitly-opened connection for the whole pass.
        // Without the explicit open, EF would return the connection to the pool between commands and
        // the lock would ride along onto whoever borrowed it next.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryAcquireAdvisoryLockAsync(dbContext, cancellationToken))
            {
                _logger.LogInformation("Another instance holds the catalog embedding backfill lock; skipping this pass.");
                return false;
            }

            try
            {
                return await EmbedPendingRowsAsync(dbContext, embeddingClient, cancellationToken);
            }
            finally
            {
                // Raw, not interpolated: the key is a private compile-time constant, so there is
                // nothing to parameterize and no type for Npgsql to have to infer.
                await dbContext.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> TryAcquireAdvisoryLockAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        // SqlQuery<T> maps a scalar result from a column named "Value".
        var acquired = await dbContext.Database
            .SqlQueryRaw<bool>($"SELECT pg_try_advisory_lock({AdvisoryLockKey}) AS \"Value\"")
            .ToListAsync(cancellationToken);

        return acquired.Single();
    }

    private async Task<bool> EmbedPendingRowsAsync(AppDbContext dbContext, IEmbeddingClient embeddingClient, CancellationToken cancellationToken)
    {
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
        if (remaining > 0)
        {
            // Not a note: unembedded rows are invisible to FindNearestAsync, so every resolve falls
            // back to the Manual tier - the exact state the seed and backfill exist to prevent.
            _logger.LogError("Catalog embedding backfill embedded {Embedded} row(s); {Remaining} still without an embedding and unmatchable.", embedded, remaining);
            return false;
        }

        _logger.LogInformation("Catalog embedding backfill embedded {Embedded} row(s); none remain without an embedding.", embedded);
        return true;
    }
}

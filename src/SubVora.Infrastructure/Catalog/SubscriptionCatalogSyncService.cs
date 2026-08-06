using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Catalog;

/// <summary>
/// Inserts any provider in subscription-catalog.json that is not already in subscription_catalog,
/// once per start. Adding a brand is then one line of JSON - no migration, no id to hand-assign,
/// no code.
///
/// The rows a trigram search reads are the rows themselves, so a provider added this way is
/// matchable the moment it lands. (The embedding era needed a successful backfill pass first,
/// which is what made this shape painful enough to avoid.)
///
/// Deliberately small: no advisory lock, because ON CONFLICT (provider_name) DO NOTHING already
/// makes two instances starting together harmless; and no retry loop, because a failure here costs
/// nothing but a log line and the next start tries again.
///
/// The seed migration stays where it is for databases that already ran it. This file is the living
/// list; that migration is frozen history, and the two overlap without conflicting - every name it
/// inserted is already present, so the sync skips them.
/// </summary>
public class SubscriptionCatalogSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionCatalogSyncService> _logger;

    public SubscriptionCatalogSyncService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionCatalogSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var inserted = await SyncAsync(dbContext, stoppingToken);
            if (inserted > 0)
            {
                _logger.LogInformation("Added {InsertedCount} provider(s) to the subscription catalog.", inserted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A hosted service must never take the host down, and a stale catalog only costs
            // matches - the app is entirely usable without this having run.
            _logger.LogError(ex, "Subscription catalog sync failed; the catalog may be missing recently added providers.");
        }
    }

    /// <summary>Returns how many rows were added. Public so an integration test can drive it without a host.</summary>
    public static async Task<int> SyncAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var providers = SubscriptionCatalogFile.Read();

        // Steady state is this one query and nothing else: on almost every start the file and the
        // table already agree.
        var existingNames = await dbContext.SubscriptionCatalog
            .AsNoTracking()
            .Select(item => item.ProviderName)
            .ToListAsync(cancellationToken);

        var missing = providers
            .Where(provider => !existingNames.Contains(provider.ProviderName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
        {
            return 0;
        }

        // Categories are seeded without explicit ids, so they are resolved by name. An unknown
        // category name would silently drop the provider on a JOIN, so the lookup happens here
        // where a miss can be a null category_id instead - the row still matches, it just arrives
        // uncategorised rather than not at all.
        var systemCategories = await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.UserId == null)
            .ToDictionaryAsync(category => category.Name, category => category.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;
        foreach (var provider in missing)
        {
            systemCategories.TryGetValue(provider.Category, out var categoryId);

            // One statement per new provider. The list is a curated ~70 rows and this only runs
            // for names not already present, so the round trips are bounded by how many brands
            // were added since the last deploy - normally zero.
            // ponytail: row-at-a-time insert, batch into a single VALUES list if the catalog ever
            // grows to the point where a first-run insert is noticeably slow.
            inserted += await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO subscription_catalog (provider_name, category_id, logo_url)
                VALUES ({provider.ProviderName}, {(categoryId == Guid.Empty ? (Guid?)null : categoryId)}, {provider.LogoUrl})
                ON CONFLICT (provider_name) DO NOTHING
                """,
                cancellationToken);
        }

        return inserted;
    }
}

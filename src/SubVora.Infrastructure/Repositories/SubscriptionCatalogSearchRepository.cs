using Microsoft.EntityFrameworkCore;
using SubVora.Application.Matching;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class SubscriptionCatalogSearchRepository : ISubscriptionCatalogSearchRepository
{
    private readonly AppDbContext _dbContext;

    public SubscriptionCatalogSearchRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CatalogMatchCandidate>> FindTopAsync(string input, int limit, CancellationToken cancellationToken = default)
    {
        // word_similarity is directional - it asks how well its first argument matches some
        // substring of its second - so both directions are needed and the better one wins:
        //   "adobe" inside "Adobe Creative Cloud"  -> word_similarity(input, name) = 1.0
        //   "Netflix" inside "Netflix Premium"     -> word_similarity(name, input) = 1.0
        // Plain similarity() scores the first of those at 0.300, down among the wrong answers.
        //
        // No trigram index: the catalog is ~54 rows, where a sequential scan is microseconds.
        // ponytail: seq scan over the whole catalog, add a GiST gist_trgm_ops index (GIN cannot
        // accelerate this ORDER BY) if the catalog ever reaches a few thousand rows.
        //
        // Raw SQL (not LINQ) because the computed score is projected alongside entity columns,
        // which EF's entity-mapped FromSql cannot do. Column names stay snake_case to match the
        // EFCore.NamingConventions convention that SqlQuery<T> result mapping also goes through.
        var rows = await _dbContext.Database
            .SqlQuery<CatalogMatchRow>($"""
                SELECT id, provider_name, category_id, logo_url,
                       greatest(word_similarity({input}, provider_name),
                                word_similarity(provider_name, {input})) AS score
                FROM subscription_catalog
                ORDER BY score DESC, provider_name
                LIMIT {limit}
                """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new CatalogMatchCandidate(row.Id, row.ProviderName, row.CategoryId, row.LogoUrl, row.Score))];
    }

    public Task<bool> ExistsAsync(Guid catalogId, CancellationToken cancellationToken = default) =>
        _dbContext.SubscriptionCatalog.AsNoTracking().AnyAsync(item => item.Id == catalogId, cancellationToken);

    private class CatalogMatchRow
    {
        public Guid Id { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public Guid? CategoryId { get; set; }
        public string? LogoUrl { get; set; }
        public double Score { get; set; }
    }
}

using Microsoft.EntityFrameworkCore;
using Pgvector;
using SubVora.Application.Matching;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class SubscriptionCatalogSearchRepository : ISubscriptionCatalogSearchRepository
{
    private readonly AppDbContext _dbContext;

    public SubscriptionCatalogSearchRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatalogMatchCandidate?> FindNearestAsync(float[] embedding, CancellationToken cancellationToken = default)
    {
        var vector = new Vector(embedding);

        // Raw SQL (not FromSqlInterpolated) because we need the computed distance column
        // alongside the entity columns - EF's entity-mapped FromSql can't project extra scalars.
        // Column names stay snake_case (unaliased/lower) to match the EFCore.NamingConventions
        // model-wide convention that SqlQuery<T> result mapping also goes through.
        var rows = await _dbContext.Database
            .SqlQuery<CatalogMatchRow>($"""
                SELECT id, provider_name, category_id, logo_url,
                       (semantic_embedding <=> {vector}) AS distance
                FROM subscription_catalog
                WHERE semantic_embedding IS NOT NULL
                ORDER BY semantic_embedding <=> {vector}
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);

        var row = rows.SingleOrDefault();
        return row is null ? null : new CatalogMatchCandidate(row.Id, row.ProviderName, row.CategoryId, row.LogoUrl, row.Distance);
    }

    public async Task<Guid> AddAsync(string providerName, float[] embedding, CancellationToken cancellationToken = default)
    {
        var item = new SubscriptionCatalogItem
        {
            ProviderName = providerName,
            SemanticEmbedding = new Vector(embedding),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SubscriptionCatalog.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    private class CatalogMatchRow
    {
        public Guid Id { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public Guid? CategoryId { get; set; }
        public string? LogoUrl { get; set; }
        public double Distance { get; set; }
    }
}

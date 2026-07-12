namespace SubVora.Application.Matching;

public interface ISubscriptionCatalogSearchRepository
{
    /// <summary>Returns the closest subscription_catalog row by cosine distance, or null when the catalog is empty.</summary>
    Task<CatalogMatchCandidate?> FindNearestAsync(float[] embedding, CancellationToken cancellationToken = default);

    /// <summary>Creates a new subscription_catalog row for a provider name with no confident existing match, so future lookups can find it.</summary>
    Task<Guid> AddAsync(string providerName, float[] embedding, CancellationToken cancellationToken = default);
}

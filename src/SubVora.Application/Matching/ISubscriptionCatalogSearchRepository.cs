namespace SubVora.Application.Matching;

public interface ISubscriptionCatalogSearchRepository
{
    /// <summary>
    /// Returns up to <paramref name="limit"/> subscription_catalog rows ordered by trigram
    /// similarity, best first. Empty when the catalog is empty.
    /// </summary>
    Task<IReadOnlyList<CatalogMatchCandidate>> FindTopAsync(string input, int limit, CancellationToken cancellationToken = default);

    // No Add here on purpose: the catalog is global and unowned, so nothing user-typed goes into
    // it at runtime. Rows arrive only from the seed migration - see SubscriptionMatchService.

    /// <summary>Whether a catalog row exists, so a client-supplied reference can be rejected with a 400 rather than a foreign-key 500.</summary>
    Task<bool> ExistsAsync(Guid catalogId, CancellationToken cancellationToken = default);
}

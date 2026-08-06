namespace SubVora.Application.Matching;

public interface ISubscriptionCatalogSearchRepository
{
    /// <summary>Returns the best subscription_catalog row by trigram similarity, or null when the catalog is empty.</summary>
    Task<CatalogMatchCandidate?> FindNearestAsync(string input, CancellationToken cancellationToken = default);

    // No Add here on purpose: the catalog is global and unowned, so nothing user-typed goes into
    // it at runtime. Rows arrive only from the seed migration - see SubscriptionMatchService.

    /// <summary>Whether a catalog row exists, so a client-supplied reference can be rejected with a 400 rather than a foreign-key 500.</summary>
    Task<bool> ExistsAsync(Guid catalogId, CancellationToken cancellationToken = default);
}

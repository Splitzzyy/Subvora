namespace SubVora.Application.Matching;

/// <summary>
/// In-memory 3-tier confidence decision over an already-fetched nearest catalog candidate - same
/// "pure logic over injected data" pattern as <c>BurnRateCalculator</c>. No EF dependency here; the
/// pgvector cosine-distance query lives behind <see cref="ISubscriptionCatalogSearchRepository"/> in
/// SubVora.Infrastructure.
/// </summary>
public class SubscriptionMatchService : ISubscriptionMatchService
{
    public const double AutoFillSimilarityThreshold = 0.85;
    public const double SuggestConfirmSimilarityThreshold = 0.70;

    private readonly IEmbeddingClient _embeddingClient;
    private readonly ISubscriptionCatalogSearchRepository _catalogSearchRepository;

    public SubscriptionMatchService(IEmbeddingClient embeddingClient, ISubscriptionCatalogSearchRepository catalogSearchRepository)
    {
        _embeddingClient = embeddingClient;
        _catalogSearchRepository = catalogSearchRepository;
    }

    public async Task<ResolveSubscriptionResponse> ResolveAsync(string freeTextInput, CancellationToken cancellationToken = default)
    {
        var embedding = await _embeddingClient.GetEmbeddingAsync(freeTextInput, cancellationToken);
        var nearest = await _catalogSearchRepository.FindNearestAsync(embedding, cancellationToken);

        if (nearest is not null)
        {
            // pgvector's <=> operator is cosine distance (0 = identical, 2 = opposite); similarity is its complement.
            var similarity = 1 - nearest.Distance;

            if (similarity >= AutoFillSimilarityThreshold)
            {
                return ToResponse(MatchConfidenceTier.AutoFill, nearest);
            }

            if (similarity >= SuggestConfirmSimilarityThreshold)
            {
                return ToResponse(MatchConfidenceTier.SuggestConfirm, nearest);
            }
        }

        // No confident match. subscription_catalog is global and unowned, so writing the raw input
        // here would publish one user's typing ("alimony - Sarah") to every other user's fuzzy
        // match. Manual means "no catalog link" - user_subscriptions.catalog_id is nullable and the
        // client already saves the free-text name on its own.
        return new ResolveSubscriptionResponse { Tier = MatchConfidenceTier.Manual };
    }

    private static ResolveSubscriptionResponse ToResponse(MatchConfidenceTier tier, CatalogMatchCandidate candidate) => new()
    {
        Tier = tier,
        CatalogId = candidate.CatalogId,
        ProviderName = candidate.ProviderName,
        LogoUrl = candidate.LogoUrl,
        CategoryId = candidate.CategoryId,
    };
}

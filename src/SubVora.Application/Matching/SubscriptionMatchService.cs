namespace SubVora.Application.Matching;

/// <summary>
/// In-memory 3-tier confidence decision over an already-fetched best catalog candidate - same
/// "pure logic over injected data" pattern as <c>BurnRateCalculator</c>. No EF dependency here; the
/// pg_trgm similarity query lives behind <see cref="ISubscriptionCatalogSearchRepository"/> in
/// SubVora.Infrastructure.
/// </summary>
public class SubscriptionMatchService : ISubscriptionMatchService
{
    /// <remarks>
    /// Both thresholds were measured against the seeded 54-provider catalog rather than guessed.
    /// Correct matches scored 0.545 and up ("net flix" 0.545, "netflx" 0.714, "spotifyy" 0.875,
    /// exact and prefix matches 1.000); wrong matches topped out at 0.429 ("the mouse streaming
    /// service" -> Strava). The gap between those two bands is where the Manual floor sits.
    /// SubscriptionCatalogSearchRepositoryTests pins the measurements so a regression is visible.
    /// </remarks>
    public const double AutoFillSimilarityThreshold = 0.70;

    public const double SuggestConfirmSimilarityThreshold = 0.50;

    private readonly ISubscriptionCatalogSearchRepository _catalogSearchRepository;

    public SubscriptionMatchService(ISubscriptionCatalogSearchRepository catalogSearchRepository)
    {
        _catalogSearchRepository = catalogSearchRepository;
    }

    public async Task<ResolveSubscriptionResponse> ResolveAsync(string freeTextInput, CancellationToken cancellationToken = default)
    {
        var best = await _catalogSearchRepository.FindNearestAsync(freeTextInput, cancellationToken);

        if (best is not null)
        {
            if (best.Score >= AutoFillSimilarityThreshold)
            {
                return ToResponse(MatchConfidenceTier.AutoFill, best);
            }

            if (best.Score >= SuggestConfirmSimilarityThreshold)
            {
                return ToResponse(MatchConfidenceTier.SuggestConfirm, best);
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

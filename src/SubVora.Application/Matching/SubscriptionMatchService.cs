namespace SubVora.Application.Matching;

/// <summary>
/// In-memory 3-tier confidence decision over already-fetched catalog candidates - same
/// "pure logic over injected data" pattern as <c>BurnRateCalculator</c>. No EF dependency here; the
/// pg_trgm similarity query lives behind <see cref="ISubscriptionCatalogSearchRepository"/> in
/// SubVora.Infrastructure.
/// </summary>
public class SubscriptionMatchService
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

    /// <summary>
    /// How many candidates the picker offers. The same measured floor still gates every entry, so
    /// this only caps how many <em>plausible</em> rows are shown - a list longer than this is
    /// scrolling, not choosing.
    /// </summary>
    public const int MaxSuggestions = 5;

    private readonly ISubscriptionCatalogSearchRepository _catalogSearchRepository;

    public SubscriptionMatchService(ISubscriptionCatalogSearchRepository catalogSearchRepository)
    {
        _catalogSearchRepository = catalogSearchRepository;
    }

    public async Task<ResolveSubscriptionResponse> ResolveAsync(string freeTextInput, CancellationToken cancellationToken = default)
    {
        var candidates = await _catalogSearchRepository.FindTopAsync(freeTextInput, MaxSuggestions, cancellationToken);

        // The floor is per candidate, not just per best match: a list that pads a good match out to
        // five entries with sub-threshold noise is offering the user wrong answers to tap.
        var suggestions = candidates
            .Where(candidate => candidate.Score >= SuggestConfirmSimilarityThreshold)
            .ToList();

        if (suggestions.Count == 0)
        {
            // No confident match. subscription_catalog is global and unowned, so writing the raw
            // input here would publish one user's typing ("alimony - Sarah") to every other user's
            // fuzzy match. Manual means "no catalog link" - user_subscriptions.catalog_id is
            // nullable and the client already saves the free-text name on its own.
            return new ResolveSubscriptionResponse { Tier = MatchConfidenceTier.Manual };
        }

        return new ResolveSubscriptionResponse
        {
            Tier = suggestions[0].Score >= AutoFillSimilarityThreshold
                ? MatchConfidenceTier.AutoFill
                : MatchConfidenceTier.SuggestConfirm,
            Suggestions = suggestions,
        };
    }
}

using SubVora.Application.Matching;

namespace SubVora.Application.Tests;

public class SubscriptionMatchServiceTests
{
    private readonly FakeCatalogSearchRepository _catalogSearchRepository = new();
    private readonly SubscriptionMatchService _service;

    public SubscriptionMatchServiceTests()
    {
        _service = new SubscriptionMatchService(_catalogSearchRepository);
    }

    [Fact]
    public async Task ScoreAtOrAbove070_ReturnsAutoFillWithMatchedCatalogFields()
    {
        var categoryId = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        // 0.714 is the measured score for "netflx" against the seeded Netflix row.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(catalogId, "Netflix", categoryId, "netflix.png", Score: 0.714);

        var result = await _service.ResolveAsync("netflx");

        Assert.Equal(MatchConfidenceTier.AutoFill, result.Tier);
        Assert.Equal(catalogId, result.CatalogId);
        Assert.Equal("Netflix", result.ProviderName);
        Assert.Equal("netflix.png", result.LogoUrl);
        Assert.Equal(categoryId, result.CategoryId);
    }

    [Fact]
    public async Task ScoreBetween050And070_ReturnsSuggestConfirm()
    {
        // 0.545 is the measured score for "net flix" - a correct match, but close enough to the
        // wrong-answer band (which topped out at 0.429) that it deserves a confirmation tap.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, Score: 0.545);

        var result = await _service.ResolveAsync("net flix");

        Assert.Equal(MatchConfidenceTier.SuggestConfirm, result.Tier);
    }

    [Fact]
    public async Task ScoreBelow050_ReturnsManual_WithNoCatalogLink()
    {
        // 0.429 is the measured score for "the mouse streaming service" -> Strava: the highest a
        // wrong answer reached against the seeded catalog.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "Strava", null, null, Score: 0.429);

        var result = await _service.ResolveAsync("the mouse streaming service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        Assert.Null(result.ProviderName);
        // subscription_catalog is global and unowned - nothing the user typed may land in it.
        Assert.Null(result.CatalogId);
    }

    [Theory]
    [InlineData(SubscriptionMatchService.AutoFillSimilarityThreshold, MatchConfidenceTier.AutoFill)]
    [InlineData(SubscriptionMatchService.SuggestConfirmSimilarityThreshold, MatchConfidenceTier.SuggestConfirm)]
    public async Task ScoreExactlyOnAThreshold_TakesTheHigherTier(double score, MatchConfidenceTier expected)
    {
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, score);

        var result = await _service.ResolveAsync("netflix");

        Assert.Equal(expected, result.Tier);
    }

    [Fact]
    public async Task EmptyCatalog_ReturnsManual_WithNoCatalogLink()
    {
        _catalogSearchRepository.NextCandidate = null;

        var result = await _service.ResolveAsync("brand new service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        Assert.Null(result.CatalogId);
    }

    [Fact]
    public async Task RepeatedUnmatchedInput_ResolvesEveryTimeWithoutColliding()
    {
        // The old behaviour inserted the raw input against a unique provider_name index, so the
        // same text twice (two users, or one debounced keystroke stream) surfaced as a 500.
        _catalogSearchRepository.NextCandidate = null;

        var first = await _service.ResolveAsync("dr okonkwo therapy monthly");
        var second = await _service.ResolveAsync("dr okonkwo therapy monthly");

        Assert.Equal(MatchConfidenceTier.Manual, first.Tier);
        Assert.Equal(MatchConfidenceTier.Manual, second.Tier);
    }

    private sealed class FakeCatalogSearchRepository : ISubscriptionCatalogSearchRepository
    {
        public CatalogMatchCandidate? NextCandidate { get; set; }

        public Task<CatalogMatchCandidate?> FindNearestAsync(string input, CancellationToken cancellationToken = default) =>
            Task.FromResult(NextCandidate);

        public Task<bool> ExistsAsync(Guid catalogId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}

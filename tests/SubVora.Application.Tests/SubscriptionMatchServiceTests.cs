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
        _catalogSearchRepository.NextCandidates = [new CatalogMatchCandidate(catalogId, "Netflix", categoryId, "netflix.png", Score: 0.714)];

        var result = await _service.ResolveAsync("netflx");

        Assert.Equal(MatchConfidenceTier.AutoFill, result.Tier);
        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(catalogId, suggestion.CatalogId);
        Assert.Equal("Netflix", suggestion.ProviderName);
        Assert.Equal("netflix.png", suggestion.LogoUrl);
        Assert.Equal(categoryId, suggestion.CategoryId);
    }

    [Fact]
    public async Task ScoreBetween050And070_ReturnsSuggestConfirm()
    {
        // 0.545 is the measured score for "net flix" - a correct match, but close enough to the
        // wrong-answer band (which topped out at 0.429) that it deserves a confirmation tap.
        _catalogSearchRepository.NextCandidates = [new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, Score: 0.545)];

        var result = await _service.ResolveAsync("net flix");

        Assert.Equal(MatchConfidenceTier.SuggestConfirm, result.Tier);
    }

    [Fact]
    public async Task ScoreBelow050_ReturnsManual_WithNoCatalogLink()
    {
        // 0.429 is the measured score for "the mouse streaming service" -> Strava: the highest a
        // wrong answer reached against the seeded catalog.
        _catalogSearchRepository.NextCandidates = [new CatalogMatchCandidate(Guid.NewGuid(), "Strava", null, null, Score: 0.429)];

        var result = await _service.ResolveAsync("the mouse streaming service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        // subscription_catalog is global and unowned - nothing the user typed may land in it, and
        // nothing below the floor may be offered as though it were an answer.
        Assert.Empty(result.Suggestions);
    }

    [Theory]
    [InlineData(SubscriptionMatchService.AutoFillSimilarityThreshold, MatchConfidenceTier.AutoFill)]
    [InlineData(SubscriptionMatchService.SuggestConfirmSimilarityThreshold, MatchConfidenceTier.SuggestConfirm)]
    public async Task ScoreExactlyOnAThreshold_TakesTheHigherTier(double score, MatchConfidenceTier expected)
    {
        _catalogSearchRepository.NextCandidates = [new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, score)];

        var result = await _service.ResolveAsync("netflix");

        Assert.Equal(expected, result.Tier);
    }

    [Fact]
    public async Task EveryCandidateAboveTheFloor_IsOffered_BestFirst()
    {
        // The point of the pick list: "youtube" is three real products, and which one the user pays
        // for is not something a similarity score can decide.
        _catalogSearchRepository.NextCandidates =
        [
            new CatalogMatchCandidate(Guid.NewGuid(), "YouTube Music", null, null, Score: 1.0),
            new CatalogMatchCandidate(Guid.NewGuid(), "YouTube Premium", null, null, Score: 1.0),
            new CatalogMatchCandidate(Guid.NewGuid(), "YouTube TV", null, null, Score: 0.875),
        ];

        var result = await _service.ResolveAsync("youtube");

        Assert.Equal(MatchConfidenceTier.AutoFill, result.Tier);
        Assert.Equal(
            ["YouTube Music", "YouTube Premium", "YouTube TV"],
            result.Suggestions.Select(suggestion => suggestion.ProviderName));
    }

    [Fact]
    public async Task CandidatesBelowTheFloor_AreDroppedFromAnOtherwiseGoodList()
    {
        // A confident match must not drag sub-threshold noise onto the screen behind it: every row
        // in the list is a row the user can tap, so every row has to be a plausible answer.
        _catalogSearchRepository.NextCandidates =
        [
            new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, Score: 1.0),
            new CatalogMatchCandidate(Guid.NewGuid(), "Strava", null, null, Score: 0.429),
        ];

        var result = await _service.ResolveAsync("netflix");

        Assert.Equal(MatchConfidenceTier.AutoFill, result.Tier);
        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal("Netflix", suggestion.ProviderName);
    }

    [Fact]
    public async Task AsksTheRepositoryForNoMoreThanTheListCap()
    {
        _catalogSearchRepository.NextCandidates = [];

        await _service.ResolveAsync("netflix");

        Assert.Equal(SubscriptionMatchService.MaxSuggestions, _catalogSearchRepository.LastLimit);
    }

    [Fact]
    public async Task EmptyCatalog_ReturnsManual_WithNoCatalogLink()
    {
        _catalogSearchRepository.NextCandidates = [];

        var result = await _service.ResolveAsync("brand new service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public async Task RepeatedUnmatchedInput_ResolvesEveryTimeWithoutColliding()
    {
        // The old behaviour inserted the raw input against a unique provider_name index, so the
        // same text twice (two users, or one debounced keystroke stream) surfaced as a 500.
        _catalogSearchRepository.NextCandidates = [];

        var first = await _service.ResolveAsync("dr okonkwo therapy monthly");
        var second = await _service.ResolveAsync("dr okonkwo therapy monthly");

        Assert.Equal(MatchConfidenceTier.Manual, first.Tier);
        Assert.Equal(MatchConfidenceTier.Manual, second.Tier);
    }

    private sealed class FakeCatalogSearchRepository : ISubscriptionCatalogSearchRepository
    {
        public IReadOnlyList<CatalogMatchCandidate> NextCandidates { get; set; } = [];

        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<CatalogMatchCandidate>> FindTopAsync(string input, int limit, CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            return Task.FromResult(NextCandidates);
        }

        public Task<bool> ExistsAsync(Guid catalogId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}

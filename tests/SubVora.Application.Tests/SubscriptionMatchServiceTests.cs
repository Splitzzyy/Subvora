using SubVora.Application.Matching;

namespace SubVora.Application.Tests;

public class SubscriptionMatchServiceTests
{
    private readonly FakeEmbeddingClient _embeddingClient = new();
    private readonly FakeCatalogSearchRepository _catalogSearchRepository = new();
    private readonly SubscriptionMatchService _service;

    public SubscriptionMatchServiceTests()
    {
        _service = new SubscriptionMatchService(_embeddingClient, _catalogSearchRepository);
    }

    [Fact]
    public async Task SimilarityAtOrAbove085_ReturnsAutoFillWithMatchedCatalogFields()
    {
        var categoryId = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        // distance 0.10 -> similarity 0.90, above the 0.85 auto-fill threshold.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(catalogId, "Netflix", categoryId, "netflix.png", Distance: 0.10);

        var result = await _service.ResolveAsync("nflx");

        Assert.Equal(MatchConfidenceTier.AutoFill, result.Tier);
        Assert.Equal(catalogId, result.CatalogId);
        Assert.Equal("Netflix", result.ProviderName);
        Assert.Equal("netflix.png", result.LogoUrl);
        Assert.Equal(categoryId, result.CategoryId);
    }

    [Fact]
    public async Task SimilarityBetween070And085_ReturnsSuggestConfirm()
    {
        // distance 0.22 -> similarity 0.78, inside the 0.70-0.85 band.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, Distance: 0.22);

        var result = await _service.ResolveAsync("nflx mobile plan");

        Assert.Equal(MatchConfidenceTier.SuggestConfirm, result.Tier);
    }

    [Fact]
    public async Task SimilarityBelow070_ReturnsManual_WithNoCatalogLink()
    {
        // distance 0.50 -> similarity 0.50, below the 0.70 floor.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "SomethingElse", null, null, Distance: 0.50);

        var result = await _service.ResolveAsync("obscure service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        Assert.Null(result.ProviderName);
        // subscription_catalog is global and unowned - nothing the user typed may land in it.
        Assert.Null(result.CatalogId);
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

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[] { 1f });
    }

    private sealed class FakeCatalogSearchRepository : ISubscriptionCatalogSearchRepository
    {
        public CatalogMatchCandidate? NextCandidate { get; set; }

        public Task<CatalogMatchCandidate?> FindNearestAsync(float[] embedding, CancellationToken cancellationToken = default) =>
            Task.FromResult(NextCandidate);

        public Task<bool> ExistsAsync(Guid catalogId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}

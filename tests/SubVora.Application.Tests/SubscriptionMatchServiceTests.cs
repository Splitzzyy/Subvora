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
        Assert.False(_catalogSearchRepository.AddWasCalled);
    }

    [Fact]
    public async Task SimilarityBetween070And085_ReturnsSuggestConfirm()
    {
        // distance 0.22 -> similarity 0.78, inside the 0.70-0.85 band.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "Netflix", null, null, Distance: 0.22);

        var result = await _service.ResolveAsync("nflx mobile plan");

        Assert.Equal(MatchConfidenceTier.SuggestConfirm, result.Tier);
        Assert.False(_catalogSearchRepository.AddWasCalled);
    }

    [Fact]
    public async Task SimilarityBelow070_ReturnsManual_AndCreatesNewCatalogEntry()
    {
        // distance 0.50 -> similarity 0.50, below the 0.70 floor.
        _catalogSearchRepository.NextCandidate = new CatalogMatchCandidate(Guid.NewGuid(), "SomethingElse", null, null, Distance: 0.50);

        var result = await _service.ResolveAsync("obscure service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        Assert.Null(result.ProviderName);
        Assert.True(_catalogSearchRepository.AddWasCalled);
    }

    [Fact]
    public async Task EmptyCatalog_ReturnsManual_AndCreatesNewCatalogEntry()
    {
        _catalogSearchRepository.NextCandidate = null;

        var result = await _service.ResolveAsync("brand new service");

        Assert.Equal(MatchConfidenceTier.Manual, result.Tier);
        Assert.True(_catalogSearchRepository.AddWasCalled);
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[] { 1f });
    }

    private sealed class FakeCatalogSearchRepository : ISubscriptionCatalogSearchRepository
    {
        public CatalogMatchCandidate? NextCandidate { get; set; }
        public bool AddWasCalled { get; private set; }

        public Task<CatalogMatchCandidate?> FindNearestAsync(float[] embedding, CancellationToken cancellationToken = default) =>
            Task.FromResult(NextCandidate);

        public Task<Guid> AddAsync(string providerName, float[] embedding, CancellationToken cancellationToken = default)
        {
            AddWasCalled = true;
            return Task.FromResult(Guid.NewGuid());
        }
    }
}

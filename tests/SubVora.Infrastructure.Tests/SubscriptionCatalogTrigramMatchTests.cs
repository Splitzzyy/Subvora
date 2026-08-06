using Microsoft.EntityFrameworkCore;
using SubVora.Application.Matching;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Repositories;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// Pins the trigram thresholds to measured behaviour against the real seeded catalog, so the
/// numbers in <see cref="SubscriptionMatchService"/> stay evidence rather than folklore. Unlike
/// <see cref="SubscriptionCatalogSearchRepositoryTests"/> this class deliberately does not
/// truncate - the seeded 54 providers are the point.
/// </summary>
public class SubscriptionCatalogTrigramMatchTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;
    private SubscriptionCatalogSearchRepository _repository = null!;

    public SubscriptionCatalogTrigramMatchTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = AppDbContextOptionsFactory.Build(_fixture.ConnectionString);
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();
        _repository = new SubscriptionCatalogSearchRepository(_dbContext);
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    /// <summary>Input a user could plausibly type that must resolve to a specific provider.</summary>
    [Theory]
    [InlineData("netflix", "Netflix")]
    [InlineData("NETFLIX", "Netflix")]
    [InlineData("Netflix Premium", "Netflix")]
    [InlineData("netflx", "Netflix")]
    [InlineData("net flix", "Netflix")]
    [InlineData("spotify", "Spotify")]
    [InlineData("spotifyy", "Spotify")]
    [InlineData("spotify premium", "Spotify")]
    [InlineData("adobe", "Adobe Creative Cloud")]
    [InlineData("adobe creativ cloud", "Adobe Creative Cloud")]
    [InlineData("1password", "1Password")]
    [InlineData("1 password", "1Password")]
    [InlineData("amazon prime", "Amazon Prime Video")]
    [InlineData("prime video", "Amazon Prime Video")]
    [InlineData("yotube premium", "YouTube Premium")]
    [InlineData("icloud", "iCloud+")]
    [InlineData("chatgpt", "ChatGPT Plus")]
    [InlineData("hbo", "HBO Max")]
    public async Task RealisticInput_MatchesTheRightProvider_AboveTheManualFloor(string input, string expectedProvider)
    {
        var result = await _repository.FindNearestAsync(input);

        Assert.NotNull(result);
        Assert.Equal(expectedProvider, result.ProviderName);
        Assert.True(
            result.Score >= SubscriptionMatchService.SuggestConfirmSimilarityThreshold,
            $"'{input}' -> {result.ProviderName} scored {result.Score:F3}, below the " +
            $"{SubscriptionMatchService.SuggestConfirmSimilarityThreshold} floor that keeps it out of Manual.");
    }

    /// <summary>
    /// Input with no honest catalog answer. Trigrams always return *something* - LIMIT 1 over a
    /// non-empty table - so what matters is that the score stays under the Manual floor and the
    /// user is never auto-filled with a wrong provider.
    /// </summary>
    [Theory]
    [InlineData("MS Office")]
    [InlineData("G Suite")]
    [InlineData("Twitter")]
    [InlineData("ACC")]
    [InlineData("gym membership")]
    [InlineData("rent")]
    [InlineData("alimony - Sarah")]
    [InlineData("the mouse streaming service")]
    public async Task InputWithNoRealMatch_ScoresBelowTheManualFloor(string input)
    {
        var result = await _repository.FindNearestAsync(input);

        Assert.NotNull(result);
        Assert.True(
            result.Score < SubscriptionMatchService.SuggestConfirmSimilarityThreshold,
            $"'{input}' -> {result.ProviderName} scored {result.Score:F3}, at or above the " +
            $"{SubscriptionMatchService.SuggestConfirmSimilarityThreshold} floor - it would be offered as a match.");
    }

    [Fact]
    public async Task ExactProviderName_ScoresAtTheTop()
    {
        var result = await _repository.FindNearestAsync("Spotify");

        Assert.NotNull(result);
        Assert.Equal("Spotify", result.ProviderName);
        Assert.Equal(1d, result.Score, precision: 5);
    }
}

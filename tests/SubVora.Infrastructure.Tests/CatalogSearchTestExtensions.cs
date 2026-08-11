using SubVora.Application.Matching;

namespace SubVora.Infrastructure.Tests;

internal static class CatalogSearchTestExtensions
{
    /// <summary>
    /// The single best candidate. The repository returns a ranked list now that the client offers a
    /// pick list, but the scoring assertions - which direction of word_similarity wins, where the
    /// thresholds sit - are about the top row, and reading it back out here keeps them saying so.
    /// </summary>
    public static async Task<CatalogMatchCandidate?> FindNearestAsync(
        this ISubscriptionCatalogSearchRepository repository,
        string input,
        CancellationToken cancellationToken = default) =>
        (await repository.FindTopAsync(input, 1, cancellationToken)).FirstOrDefault();
}

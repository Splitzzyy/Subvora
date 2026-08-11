namespace SubVora.Application.Matching;

public class ResolveSubscriptionResponse
{
    /// <summary>
    /// How good the <em>best</em> candidate is. Wording only - the client offers the whole list
    /// either way, and <see cref="MatchConfidenceTier.Manual"/> always comes with an empty list.
    /// </summary>
    public MatchConfidenceTier Tier { get; set; }

    /// <summary>
    /// Every catalog row that cleared the suggest threshold, best first, so the user picks rather
    /// than being handed a single guess. Empty on <see cref="MatchConfidenceTier.Manual"/>.
    /// </summary>
    public IReadOnlyList<CatalogMatchCandidate> Suggestions { get; set; } = [];
}

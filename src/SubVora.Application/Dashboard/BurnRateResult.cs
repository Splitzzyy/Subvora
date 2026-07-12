namespace SubVora.Application.Dashboard;

public class BurnRateResult
{
    public decimal Weekly { get; set; }
    public decimal Monthly { get; set; }
    public decimal Yearly { get; set; }
    public decimal OneTimeThisYear { get; set; }
    public string HomeCurrency { get; set; } = string.Empty;

    /// <summary>Subscriptions excluded from the totals above because no cached fx_rates pair covers their currency.</summary>
    public IReadOnlyList<Guid> UnresolvedSubscriptionIds { get; set; } = [];
}

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

    /// <summary>
    /// When the oldest fx_rates row these totals lean on was fetched, or null when nothing needed
    /// converting. Lets a client distinguish "converted with fresh rates" from "converted with
    /// rates from N days ago" - and makes a stalled refresh job visible instead of silent.
    /// </summary>
    public DateTimeOffset? OldestRateFetchedAt { get; set; }

    /// <summary>Monthly recurring spend grouped by category, in home currency. Excludes one-time purchases and active trials.</summary>
    public IReadOnlyList<CategoryBreakdownItem> ByCategory { get; set; } = [];

    /// <summary>
    /// The same monthly recurring spend grouped by the card/account it is charged to, largest
    /// first - which account is actually carrying the burn rate. Same exclusions as
    /// <see cref="ByCategory"/>; subscriptions with no payment source assigned group under
    /// "Unassigned".
    /// </summary>
    public IReadOnlyList<PaymentSourceBreakdownItem> ByPaymentSource { get; set; } = [];
}

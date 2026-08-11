using System.Text.Json.Serialization;

namespace SubVora.Mobile.Api.Dtos;

public class BurnRateResult
{
    public decimal Weekly { get; set; }
    public decimal Monthly { get; set; }
    public decimal Yearly { get; set; }
    public decimal OneTimeThisYear { get; set; }
    public string HomeCurrency { get; set; } = string.Empty;
    public IReadOnlyList<Guid> UnresolvedSubscriptionIds { get; set; } = [];
    public DateTimeOffset? OldestRateFetchedAt { get; set; }
    public IReadOnlyList<CategoryBreakdownItem> ByCategory { get; set; } = [];
    public IReadOnlyList<PaymentSourceBreakdownItem> ByPaymentSource { get; set; } = [];
}

public class CategoryBreakdownItem
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }

    /// <summary>
    /// 0-1 bar length for the dashboard breakdown, computed on the client by DashboardViewModel.
    /// JsonIgnored so it never reaches the server's contract and never survives into the SQLite
    /// cache, where it would go stale the moment another category's amount moved.
    /// </summary>
    [JsonIgnore]
    public double Share { get; set; }
}

/// <summary>Monthly recurring spend charged to one card/account, in the user's home currency.</summary>
public class PaymentSourceBreakdownItem
{
    public Guid? PaymentSourceId { get; set; }
    public string PaymentSourceLabel { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }

    /// <summary>Bar length, computed client-side exactly as CategoryBreakdownItem.Share is.</summary>
    [JsonIgnore]
    public double Share { get; set; }
}

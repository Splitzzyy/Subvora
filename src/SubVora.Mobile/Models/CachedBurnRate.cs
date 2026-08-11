using System.Text.Json;
using SQLite;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Models;

/// <summary>
/// Flattened sqlite-net-pcl mirror of BurnRateResult. There is one burn-rate snapshot per
/// user-session, so Id is a constant singleton key - repeated upserts replace this one row.
/// </summary>
public class CachedBurnRate
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    public decimal Weekly { get; set; }
    public decimal Monthly { get; set; }
    public decimal Yearly { get; set; }
    public decimal OneTimeThisYear { get; set; }
    public string HomeCurrency { get; set; } = string.Empty;

    /// <summary>How many subscriptions the server left out of the totals for want of an FX rate.</summary>
    public int UnresolvedSubscriptionCount { get; set; }

    /// <summary>Fetch time of the oldest FX rate behind these totals; null when nothing was converted.</summary>
    public DateTimeOffset? OldestRateFetchedAt { get; set; }

    // sqlite-net-pcl doesn't map nested collections natively - store as a JSON column.
    public string ByCategoryJson { get; set; } = "[]";

    [Ignore]
    public List<CategoryBreakdownItem> ByCategory
    {
        get => JsonSerializer.Deserialize<List<CategoryBreakdownItem>>(ByCategoryJson) ?? [];
        set => ByCategoryJson = JsonSerializer.Serialize(value);
    }

    public string ByPaymentSourceJson { get; set; } = "[]";

    [Ignore]
    public List<PaymentSourceBreakdownItem> ByPaymentSource
    {
        // Defaulted rather than required: sqlite-net-pcl adds the column to an existing table on
        // CreateTableAsync but leaves it null on rows written before it existed, so the first
        // offline read after an upgrade lands here rather than throwing.
        get => string.IsNullOrEmpty(ByPaymentSourceJson)
            ? []
            : JsonSerializer.Deserialize<List<PaymentSourceBreakdownItem>>(ByPaymentSourceJson) ?? [];
        set => ByPaymentSourceJson = JsonSerializer.Serialize(value);
    }
}

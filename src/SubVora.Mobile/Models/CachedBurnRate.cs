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

    // sqlite-net-pcl doesn't map nested collections natively - store as a JSON column.
    public string ByCategoryJson { get; set; } = "[]";

    [Ignore]
    public List<CategoryBreakdownItem> ByCategory
    {
        get => JsonSerializer.Deserialize<List<CategoryBreakdownItem>>(ByCategoryJson) ?? [];
        set => ByCategoryJson = JsonSerializer.Serialize(value);
    }
}

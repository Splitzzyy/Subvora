namespace SubVora.Mobile.Api.Dtos;

public class BurnRateResult
{
    public decimal Weekly { get; set; }
    public decimal Monthly { get; set; }
    public decimal Yearly { get; set; }
    public decimal OneTimeThisYear { get; set; }
    public string HomeCurrency { get; set; } = string.Empty;
    public IReadOnlyList<Guid> UnresolvedSubscriptionIds { get; set; } = [];
    public IReadOnlyList<CategoryBreakdownItem> ByCategory { get; set; } = [];
}

public class CategoryBreakdownItem
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
}

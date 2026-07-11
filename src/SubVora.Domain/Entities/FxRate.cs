namespace SubVora.Domain.Entities;

public class FxRate
{
    public Guid Id { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

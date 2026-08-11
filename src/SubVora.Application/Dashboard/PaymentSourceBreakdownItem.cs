namespace SubVora.Application.Dashboard;

public class PaymentSourceBreakdownItem
{
    public Guid? PaymentSourceId { get; set; }
    public string PaymentSourceLabel { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
}

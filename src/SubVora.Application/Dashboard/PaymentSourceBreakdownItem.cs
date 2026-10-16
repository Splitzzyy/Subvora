using SubVora.Domain.Enums;

namespace SubVora.Application.Dashboard;

public class PaymentSourceBreakdownItem
{
    public Guid? PaymentSourceId { get; set; }
    public string PaymentSourceLabel { get; set; } = string.Empty;

    /// <summary>So a client can show a card, bank or wallet the way its own list does. Null for the unassigned bucket.</summary>
    public PaymentSourceType? PaymentSourceType { get; set; }

    public decimal MonthlyAmount { get; set; }
}

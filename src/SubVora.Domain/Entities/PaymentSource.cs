using SubVora.Domain.Enums;

namespace SubVora.Domain.Entities;

public class PaymentSource
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public PaymentSourceType SourceType { get; set; } = PaymentSourceType.Other;
    public DateTimeOffset CreatedAt { get; set; }
}

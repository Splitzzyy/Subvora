using SubVora.Domain.Enums;

namespace SubVora.Application.PaymentSources;

public class PaymentSourceDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public PaymentSourceType SourceType { get; set; }
}

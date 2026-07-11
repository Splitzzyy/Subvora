using SubVora.Domain.Enums;

namespace SubVora.Application.PaymentSources;

public class CreatePaymentSourceRequest
{
    public string Label { get; set; } = string.Empty;
    public PaymentSourceType SourceType { get; set; } = PaymentSourceType.Other;
}

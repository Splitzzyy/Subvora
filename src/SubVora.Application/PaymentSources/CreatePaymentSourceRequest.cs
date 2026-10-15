using SubVora.Domain.Enums;

namespace SubVora.Application.PaymentSources;

public class CreatePaymentSourceRequest
{
    /// <summary>
    /// Optional client-chosen id, used on create only. A client that picks it once per create form
    /// and sends it on every retry gets the original record back (200) instead of a duplicate when
    /// the first response was lost. Ignored on update, which takes the id from the route.
    /// </summary>
    public Guid? Id { get; set; }

    public string Label { get; set; } = string.Empty;
    public PaymentSourceType SourceType { get; set; } = PaymentSourceType.Other;
}

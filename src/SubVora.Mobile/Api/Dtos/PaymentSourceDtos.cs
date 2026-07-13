namespace SubVora.Mobile.Api.Dtos;

public enum PaymentSourceType
{
    Card,
    BankAccount,
    Wallet,
    Other,
}

public class PaymentSourceDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public PaymentSourceType SourceType { get; set; }
}

public class CreatePaymentSourceRequest
{
    public string Label { get; set; } = string.Empty;
    public PaymentSourceType SourceType { get; set; } = PaymentSourceType.Other;
}

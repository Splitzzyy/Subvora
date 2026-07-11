namespace SubVora.Application.Currency;

public record ExchangeRate(string BaseCurrency, string TargetCurrency, decimal Rate);

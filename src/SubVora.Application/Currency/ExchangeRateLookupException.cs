namespace SubVora.Application.Currency;

/// <summary>
/// The provider answered, but not with rates for <paramref name="baseCurrency"/>: an error in the
/// body (an unsupported code, a quota) or a body that could not be read. Distinct from the provider
/// being unreachable, which surfaces as whatever HttpClient throws - FxRateService gives up on just
/// this base for one, and on the whole batch for the other.
/// </summary>
public sealed class ExchangeRateLookupException(string baseCurrency, string? errorType, Exception? innerException = null)
    : Exception($"Exchange rate lookup for {baseCurrency} failed: {errorType ?? "unreadable response"}.", innerException);

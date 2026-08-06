namespace SubVora.Application.Currency;

/// <summary>
/// A cached fx_rates row: the rate plus when it was fetched. Callers need the age because the
/// refresh job catches and logs its own failures - without a timestamp, rates left behind by a
/// job that has been failing for a month are indistinguishable from rates fetched this morning.
/// </summary>
public record CachedFxRate(decimal Rate, DateTimeOffset FetchedAt);

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SubVora.Application.Currency;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Currency;

public class FxRateService : IFxRateService
{
    private static readonly TimeSpan FailedFetchCooldown = TimeSpan.FromHours(1);

    // ponytail: process-local, bounded by the number of currency pairs ever asked for. Move to a
    // shared cache only if enough replicas run that one on-demand call each per hour matters.
    private static readonly ConcurrentDictionary<(string Base, string Target), DateTimeOffset> FailedFetches = new();

    private readonly AppDbContext _dbContext;
    private readonly IExchangeRateClient _exchangeRateClient;
    private readonly ILogger<FxRateService> _logger;

    public FxRateService(AppDbContext dbContext, IExchangeRateClient exchangeRateClient, ILogger<FxRateService> logger)
    {
        _dbContext = dbContext;
        _exchangeRateClient = exchangeRateClient;
        _logger = logger;
    }

    public async Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default)
    {
        foreach (var rate in rates)
        {
            // Raw SQL upsert on the UNIQUE (base_currency, target_currency) constraint from
            // Slice 5 - a real DB-level upsert, not a racy check-then-insert/update.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO fx_rates (base_currency, target_currency, rate, fetched_at)
                VALUES ({rate.BaseCurrency}, {rate.TargetCurrency}, {rate.Rate}, now())
                ON CONFLICT (base_currency, target_currency)
                DO UPDATE SET rate = EXCLUDED.rate, fetched_at = EXCLUDED.fetched_at
                """,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, CachedFxRate>> GetRatesAsync(
        IReadOnlyCollection<string> baseCurrencies,
        string targetCurrency,
        CancellationToken cancellationToken = default)
    {
        var wanted = baseCurrencies
            .Where(currency => !string.Equals(currency, targetCurrency, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = new Dictionary<string, CachedFxRate>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return resolved;
        }

        // One round trip for every pair that is already cached, which in steady state is all of
        // them. Npgsql turns the Contains into `base_currency = ANY(@p)`.
        var cached = await _dbContext.FxRates.AsNoTracking()
            .Where(r => r.TargetCurrency == targetCurrency && wanted.Contains(r.BaseCurrency))
            .Select(r => new { r.BaseCurrency, r.Rate, r.FetchedAt })
            .ToListAsync(cancellationToken);

        foreach (var row in cached)
        {
            resolved[row.BaseCurrency] = new CachedFxRate(row.Rate, row.FetchedAt);
        }

        // Only pairs the batch missed, one call each. This is what lets a user's first subscription
        // in a new currency count toward their totals before the next scheduled refresh - so it
        // stays, cooldown and all, rather than being sacrificed to make the batch a single
        // statement.
        foreach (var missing in wanted.Where(currency => !resolved.ContainsKey(currency)))
        {
            if (await FetchMissingRateAsync(missing, targetCurrency, cancellationToken) is { } fetched)
            {
                resolved[missing] = fetched;
            }
        }

        return resolved;
    }

    private async Task<CachedFxRate?> FetchMissingRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken)
    {
        var pair = (baseCurrency, targetCurrency);

        // A pair the provider does not support would otherwise be re-fetched on every dashboard
        // load. The scheduled refresh stays the backstop for a pair that starts working later.
        if (FailedFetches.TryGetValue(pair, out var failedAt) && DateTimeOffset.UtcNow - failedAt < FailedFetchCooldown)
        {
            return null;
        }

        ExchangeRate? fetched = null;
        try
        {
            var rates = await _exchangeRateClient.GetLatestRatesAsync(baseCurrency, [targetCurrency], cancellationToken);
            fetched = rates.FirstOrDefault(r => r.TargetCurrency == targetCurrency);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "On-demand FX fetch failed for {BaseCurrency}->{TargetCurrency}; the scheduled refresh remains the backstop.", baseCurrency, targetCurrency);
        }

        if (fetched is null)
        {
            FailedFetches[pair] = DateTimeOffset.UtcNow;
            return null;
        }

        await UpsertRatesAsync([fetched], cancellationToken);
        FailedFetches.TryRemove(pair, out _);
        return new CachedFxRate(fetched.Rate, DateTimeOffset.UtcNow);
    }
}

using Microsoft.EntityFrameworkCore;
using SubVora.Application.Currency;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Currency;

public class FxRateService : IFxRateService
{
    private readonly AppDbContext _dbContext;

    public FxRateService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

    public async Task<decimal?> GetRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default) =>
        await _dbContext.FxRates.AsNoTracking()
            .Where(r => r.BaseCurrency == baseCurrency && r.TargetCurrency == targetCurrency)
            .Select(r => (decimal?)r.Rate)
            .SingleOrDefaultAsync(cancellationToken);
}

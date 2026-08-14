using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SubVora.Application.Currency;
using SubVora.Application.Scheduling;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Currency;

public class FxRateRefreshBackgroundService : BackgroundService
{
    /// <summary>An hour before the renewal scan, so the day's alerts and totals use rates fetched the same morning.</summary>
    private const int DefaultRefreshUtcHour = 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IExchangeRateClient _exchangeRateClient;
    private readonly ILogger<FxRateRefreshBackgroundService> _logger;
    private readonly int _refreshUtcHour;

    public FxRateRefreshBackgroundService(
        IServiceScopeFactory scopeFactory,
        IExchangeRateClient exchangeRateClient,
        ILogger<FxRateRefreshBackgroundService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _exchangeRateClient = exchangeRateClient;
        _logger = logger;
        _refreshUtcHour = DailyUtcSchedule.ReadUtcHour(configuration["FxRateRefresh:UtcHour"], DefaultRefreshUtcHour);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed refresh must never crash the host or corrupt already-cached rates -
                // UpsertRatesAsync only ever touches rows for pairs it successfully fetched.
                _logger.LogError(ex, "FX rate refresh failed; previously cached rates are unaffected.");
            }

            try
            {
                await Task.Delay(DailyUtcSchedule.DelayUntilNextRun(DateTimeOffset.UtcNow, _refreshUtcHour), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs a single refresh pass: query currencies in use, fetch rates, upsert. Public so tests can drive one iteration directly instead of the infinite ExecuteAsync loop.</summary>
    public async Task RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fxRateService = scope.ServiceProvider.GetRequiredService<IFxRateService>();

        var subscriptionCurrencies = await dbContext.UserSubscriptions
            .Select(s => s.Currency)
            .Distinct()
            .ToListAsync(cancellationToken);
        var preferredCurrencies = await dbContext.Users
            .Select(u => u.PreferredCurrency)
            .Distinct()
            .ToListAsync(cancellationToken);

        var targetCurrencies = preferredCurrencies.Distinct().ToList();
        if (targetCurrencies.Count == 0)
        {
            return;
        }

        var baseCurrencies = subscriptionCurrencies.Union(preferredCurrencies).Distinct();

        var allRates = new List<ExchangeRate>();
        var failedCurrencies = 0;

        foreach (var baseCurrency in baseCurrencies)
        {
            var targets = targetCurrencies.Where(t => t != baseCurrency).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            // Isolated per currency, deliberately: a partial pass beats none. These rates were
            // accumulated and upserted only after the loop, so a single unsupported pair or one
            // transient 5xx threw straight past the upsert and discarded every rate that had
            // already been fetched successfully - every user's totals aged by a day because one
            // user tracked something exotic. The next scheduled run retries whatever failed.
            try
            {
                var rates = await _exchangeRateClient.GetLatestRatesAsync(baseCurrency, targets, cancellationToken);
                allRates.AddRange(rates);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCurrencies++;
                _logger.LogWarning(ex, "FX rate fetch failed for base currency {BaseCurrency}; continuing with the rest of the pass.", baseCurrency);
            }
        }

        if (allRates.Count > 0)
        {
            await fxRateService.UpsertRatesAsync(allRates, cancellationToken);
        }

        // Otherwise a provider that has quietly dropped a currency is invisible until someone works
        // backwards from a total that stopped moving. Escalated to Error when nothing at all came
        // back: isolating failures per currency must not turn a total provider outage - which used
        // to throw and log at Error - into a handful of warnings nobody pages on.
        if (failedCurrencies > 0)
        {
            if (allRates.Count == 0)
            {
                _logger.LogError(
                    "FX rate refresh fetched nothing: all {FailedCurrencyCount} base currency/currencies failed. Previously cached rates are unaffected.",
                    failedCurrencies);
            }
            else
            {
                _logger.LogWarning(
                    "FX rate refresh completed with {FailedCurrencyCount} base currency/currencies failing; {UpsertedRateCount} rate(s) were still refreshed.",
                    failedCurrencies,
                    allRates.Count);
            }
        }
    }
}

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
        foreach (var baseCurrency in baseCurrencies)
        {
            var targets = targetCurrencies.Where(t => t != baseCurrency).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            var rates = await _exchangeRateClient.GetLatestRatesAsync(baseCurrency, targets, cancellationToken);
            allRates.AddRange(rates);
        }

        if (allRates.Count > 0)
        {
            await fxRateService.UpsertRatesAsync(allRates, cancellationToken);
        }
    }
}

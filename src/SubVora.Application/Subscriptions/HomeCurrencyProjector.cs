using SubVora.Application.Billing;
using SubVora.Application.Currency;

namespace SubVora.Application.Subscriptions;

/// <summary>
/// Fills each subscription's home-currency figures for display. A read-time projection with the
/// cached rates <see cref="Dashboard.BurnRateCalculator"/> uses, so a list row and the dashboard
/// totals always agree; the stored currency and amount are never touched.
/// </summary>
public class HomeCurrencyProjector
{
    private readonly IFxRateService _fxRateService;

    public HomeCurrencyProjector(IFxRateService fxRateService)
    {
        _fxRateService = fxRateService;
    }

    public async Task ProjectAsync(IReadOnlyCollection<SubscriptionDto> subscriptions, string homeCurrency, CancellationToken cancellationToken = default)
    {
        if (subscriptions.Count == 0)
        {
            return;
        }

        // One round trip for every pair, as the burn rate does; same-currency rows need none.
        var rates = await _fxRateService.GetRatesAsync(
            subscriptions.Select(subscription => subscription.Currency).ToList(),
            homeCurrency,
            cancellationToken);

        foreach (var subscription in subscriptions)
        {
            subscription.HomeCurrency = homeCurrency;

            decimal? rate = string.Equals(subscription.Currency, homeCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : rates.TryGetValue(subscription.Currency, out var cached) ? cached.Rate : null;
            if (rate is null)
            {
                continue;
            }

            var converted = subscription.CostAmount * rate.Value;
            subscription.HomeCurrencyAmount = Math.Round(converted, 2);

            if (BillingCycles.ChargesPerYear(subscription.CycleCadence) is int chargesPerYear)
            {
                var yearly = converted * chargesPerYear;
                subscription.HomeCurrencyYearly = Math.Round(yearly, 2);
                subscription.HomeCurrencyMonthly = Math.Round(yearly / BillingCycles.MonthsPerYear, 2);
            }
        }
    }
}

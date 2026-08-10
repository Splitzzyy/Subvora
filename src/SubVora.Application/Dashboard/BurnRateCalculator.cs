using SubVora.Application.Currency;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Dashboard;

/// <summary>
/// In-memory aggregation over already-fetched subscriptions, converting each one's native-currency
/// cost to the caller's home currency via <see cref="IFxRateService"/> before summing - reads cached
/// rates, and never a mutation of the subscription's stored currency/amount.
/// <para>
/// Every rate is resolved in one call before the loop starts, rather than per subscription - see
/// IFxRateService.GetRatesAsync. A pair with no cached rate is still fetched once behind the
/// interface; a pair that cannot be resolved at all leaves its subscription out of the totals and
/// named in UnresolvedSubscriptionIds.
/// </para>
/// </summary>
public class BurnRateCalculator
{
    private const int WeeklyDays = 7;
    private const int MonthlyDays = 30;
    private const int YearlyDays = 365;

    private readonly IFxRateService _fxRateService;

    public BurnRateCalculator(IFxRateService fxRateService)
    {
        _fxRateService = fxRateService;
    }

    public async Task<BurnRateResult> CalculateAsync(IEnumerable<SubscriptionDto> subscriptions, string homeCurrency, CancellationToken cancellationToken = default)
    {
        const string uncategorizedName = "Uncategorized";

        var currentYear = DateTime.UtcNow.Year;
        var dailyRateSum = 0m;
        var oneTimeThisYear = 0m;
        var unresolvedSubscriptionIds = new List<Guid>();
        var categoryDailyRates = new Dictionary<(Guid? CategoryId, string CategoryName), decimal>();
        DateTimeOffset? oldestRateFetchedAt = null;

        // Materialized because the currencies are collected in one pass and the amounts summed in
        // another, and the caller's sequence is not promised to survive being enumerated twice.
        var activeSubscriptions = subscriptions.Where(subscription => subscription.IsActive).ToList();

        // Every rate this calculation needs, in one round trip. Asking per subscription meant a
        // user with twenty USD subscriptions issued the same query twenty times, on the screen the
        // app opens to.
        var rates = await _fxRateService.GetRatesAsync(
            activeSubscriptions.Select(subscription => subscription.Currency).ToList(),
            homeCurrency,
            cancellationToken);

        foreach (var subscription in activeSubscriptions)
        {
            decimal rate;
            if (string.Equals(subscription.Currency, homeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                // Not a fetched rate, so it never ages the result reported below.
                rate = 1m;
            }
            else
            {
                if (!rates.TryGetValue(subscription.Currency, out var cachedRate))
                {
                    unresolvedSubscriptionIds.Add(subscription.Id);
                    continue;
                }

                rate = cachedRate.Rate;

                // A stale rate still converts - a slightly old total beats an unexplained hole in
                // it - but the oldest fetched_at rides along so the client can say how old.
                if (oldestRateFetchedAt is null || cachedRate.FetchedAt < oldestRateFetchedAt)
                {
                    oldestRateFetchedAt = cachedRate.FetchedAt;
                }
            }

            var convertedCost = subscription.CostAmount * rate;

            if (subscription.CycleCadence == BillingCycleType.OneTime)
            {
                if (subscription.PurchaseDate.Year == currentYear)
                {
                    oneTimeThisYear += convertedCost;
                }

                continue;
            }

            // Active free trials aren't being charged yet - excluded until IsFreeTrial flips
            // to false, at which point the same subscription joins the totals automatically.
            if (subscription.IsFreeTrial)
            {
                continue;
            }

            var cycleDays = subscription.CycleCadence switch
            {
                BillingCycleType.Weekly => WeeklyDays,
                BillingCycleType.Monthly => MonthlyDays,
                BillingCycleType.Yearly => YearlyDays,
                _ => throw new ArgumentOutOfRangeException(nameof(subscriptions), subscription.CycleCadence, "Unexpected billing cycle for a recurring subscription."),
            };

            var subscriptionDailyRate = convertedCost / cycleDays;
            dailyRateSum += subscriptionDailyRate;

            var categoryKey = (subscription.CategoryId, subscription.CategoryName ?? uncategorizedName);
            categoryDailyRates[categoryKey] = categoryDailyRates.GetValueOrDefault(categoryKey) + subscriptionDailyRate;
        }

        var byCategory = categoryDailyRates
            .Select(kvp => new CategoryBreakdownItem
            {
                CategoryId = kvp.Key.CategoryId,
                CategoryName = kvp.Key.CategoryName,
                MonthlyAmount = Math.Round(kvp.Value * MonthlyDays, 2),
            })
            .OrderByDescending(item => item.MonthlyAmount)
            .ToList();

        return new BurnRateResult
        {
            Weekly = Math.Round(dailyRateSum * WeeklyDays, 2),
            Monthly = Math.Round(dailyRateSum * MonthlyDays, 2),
            Yearly = Math.Round(dailyRateSum * YearlyDays, 2),
            OneTimeThisYear = Math.Round(oneTimeThisYear, 2),
            HomeCurrency = homeCurrency,
            UnresolvedSubscriptionIds = unresolvedSubscriptionIds,
            OldestRateFetchedAt = oldestRateFetchedAt,
            ByCategory = byCategory,
        };
    }
}

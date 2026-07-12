using SubVora.Application.Currency;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Dashboard;

/// <summary>
/// In-memory aggregation over already-fetched subscriptions, converting each one's native-currency
/// cost to the caller's home currency via cached <see cref="IFxRateService"/> rates before summing -
/// never a live API call, and never a mutation of the subscription's stored currency/amount.
/// </summary>
public class BurnRateCalculator : IBurnRateCalculator
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

        foreach (var subscription in subscriptions)
        {
            if (!subscription.IsActive)
            {
                continue;
            }

            var rate = await ResolveRateAsync(subscription.Currency, homeCurrency, cancellationToken);
            if (rate is null)
            {
                unresolvedSubscriptionIds.Add(subscription.Id);
                continue;
            }

            var convertedCost = subscription.CostAmount * rate.Value;

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
            ByCategory = byCategory,
        };
    }

    private async Task<decimal?> ResolveRateAsync(string subscriptionCurrency, string homeCurrency, CancellationToken cancellationToken)
    {
        if (string.Equals(subscriptionCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        return await _fxRateService.GetRateAsync(subscriptionCurrency, homeCurrency, cancellationToken);
    }
}

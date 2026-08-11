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
    private const int MonthsPerYear = 12;

    /// <summary>
    /// Weeks in a year for the purpose of the weekly headline. 52, not 365/7 - a weekly
    /// subscription should read back as exactly its own cost per week.
    /// </summary>
    private const int WeeksPerYear = 52;

    /// <summary>
    /// How many times a cycle is charged in a year. This is the whole calculation: a subscription's
    /// annual cost is its price times the number of times it is billed, and every other figure is
    /// derived from that sum.
    /// <para>
    /// It replaces normalising to a daily rate (<c>cost / cycle_days</c> with a 30-day month and a
    /// 365-day year), which quietly made a year 365/30 = 12.17 months: 1000/month reported 12166.67
    /// a year instead of 12000. The error was invisible at small numbers and grew with the total.
    /// Calendar cycles are counts, not spans of days, and counting them is also what a user checks
    /// the figure against.
    /// </para>
    /// </summary>
    private static int ChargesPerYear(BillingCycleType cadence) => cadence switch
    {
        BillingCycleType.Weekly => WeeksPerYear,
        BillingCycleType.Monthly => MonthsPerYear,
        BillingCycleType.Quarterly => 4,
        BillingCycleType.Yearly => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unexpected billing cycle for a recurring subscription."),
    };

    private readonly IFxRateService _fxRateService;

    public BurnRateCalculator(IFxRateService fxRateService)
    {
        _fxRateService = fxRateService;
    }

    public async Task<BurnRateResult> CalculateAsync(IEnumerable<SubscriptionDto> subscriptions, string homeCurrency, CancellationToken cancellationToken = default)
    {
        const string uncategorizedName = "Uncategorized";
        const string unassignedPaymentSourceLabel = "Unassigned";

        var currentYear = DateTime.UtcNow.Year;
        var annualSum = 0m;
        var oneTimeThisYear = 0m;
        var unresolvedSubscriptionIds = new List<Guid>();
        var categoryAnnualAmounts = new Dictionary<(Guid? CategoryId, string CategoryName), decimal>();
        var paymentSourceAnnualAmounts = new Dictionary<(Guid? PaymentSourceId, string Label), decimal>();
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

            // What this subscription costs over a year: its price times how often it is billed.
            var subscriptionAnnual = convertedCost * ChargesPerYear(subscription.CycleCadence);
            annualSum += subscriptionAnnual;

            var categoryKey = (subscription.CategoryId, subscription.CategoryName ?? uncategorizedName);
            categoryAnnualAmounts[categoryKey] = categoryAnnualAmounts.GetValueOrDefault(categoryKey) + subscriptionAnnual;

            var paymentSourceKey = (subscription.PaymentSourceId, subscription.PaymentSourceLabel ?? unassignedPaymentSourceLabel);
            paymentSourceAnnualAmounts[paymentSourceKey] = paymentSourceAnnualAmounts.GetValueOrDefault(paymentSourceKey) + subscriptionAnnual;
        }

        var byCategory = categoryAnnualAmounts
            .Select(kvp => new CategoryBreakdownItem
            {
                CategoryId = kvp.Key.CategoryId,
                CategoryName = kvp.Key.CategoryName,
                MonthlyAmount = Math.Round(kvp.Value / MonthsPerYear, 2),
            })
            .OrderByDescending(item => item.MonthlyAmount)
            .ToList();

        var byPaymentSource = paymentSourceAnnualAmounts
            .Select(kvp => new PaymentSourceBreakdownItem
            {
                PaymentSourceId = kvp.Key.PaymentSourceId,
                PaymentSourceLabel = kvp.Key.Label,
                MonthlyAmount = Math.Round(kvp.Value / MonthsPerYear, 2),
            })
            .OrderByDescending(item => item.MonthlyAmount)
            .ToList();

        return new BurnRateResult
        {
            // All three derived from the annual total, so they stay consistent with each other:
            // Monthly x 12 and Weekly x 52 both come back to Yearly, up to rounding.
            Weekly = Math.Round(annualSum / WeeksPerYear, 2),
            Monthly = Math.Round(annualSum / MonthsPerYear, 2),
            Yearly = Math.Round(annualSum, 2),
            OneTimeThisYear = Math.Round(oneTimeThisYear, 2),
            HomeCurrency = homeCurrency,
            UnresolvedSubscriptionIds = unresolvedSubscriptionIds,
            OldestRateFetchedAt = oldestRateFetchedAt,
            ByCategory = byCategory,
            ByPaymentSource = byPaymentSource,
        };
    }
}

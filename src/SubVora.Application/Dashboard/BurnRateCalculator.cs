using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Dashboard;

/// <summary>
/// Pure in-memory aggregation, no EF/DB dependency - the caller (DashboardController) is
/// responsible for fetching the subscriptions first. Kept this way so the burn-rate math is
/// unit-testable without a database.
/// </summary>
public class BurnRateCalculator : IBurnRateCalculator
{
    private const int WeeklyDays = 7;
    private const int MonthlyDays = 30;
    private const int YearlyDays = 365;

    public BurnRateResult Calculate(IEnumerable<SubscriptionDto> subscriptions)
    {
        var currentYear = DateTime.UtcNow.Year;
        var dailyRateSum = 0m;
        var oneTimeThisYear = 0m;

        foreach (var subscription in subscriptions)
        {
            if (!subscription.IsActive)
            {
                continue;
            }

            if (subscription.CycleCadence == BillingCycleType.OneTime)
            {
                if (subscription.PurchaseDate.Year == currentYear)
                {
                    oneTimeThisYear += subscription.CostAmount;
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

            dailyRateSum += subscription.CostAmount / cycleDays;
        }

        return new BurnRateResult
        {
            Weekly = Math.Round(dailyRateSum * WeeklyDays, 2),
            Monthly = Math.Round(dailyRateSum * MonthlyDays, 2),
            Yearly = Math.Round(dailyRateSum * YearlyDays, 2),
            OneTimeThisYear = oneTimeThisYear,
        };
    }
}

using SubVora.Domain.Entities;

namespace SubVora.Application.Billing;

/// <summary>
/// Pure logic over already-fetched subscriptions - same "no EF in Application" pattern as
/// <c>BurnRateCalculator</c>.
/// </summary>
public class BillingDateScanner : IBillingDateScanner
{
    public IReadOnlyList<UserSubscription> FindDueForAdvance(DateOnly today, IEnumerable<UserSubscription> activeSubscriptions) =>
        activeSubscriptions
            .Where(s => s.IsActive)
            .Where(s => s.NextBillingDate < today)
            .ToList();
}

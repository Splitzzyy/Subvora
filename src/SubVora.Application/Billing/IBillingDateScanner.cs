using SubVora.Domain.Entities;

namespace SubVora.Application.Billing;

public interface IBillingDateScanner
{
    /// <summary>
    /// Returns the active subscriptions whose <c>next_billing_date</c> is already in the past and
    /// therefore needs rolling forward.
    /// </summary>
    IReadOnlyList<UserSubscription> FindDueForAdvance(DateOnly today, IEnumerable<UserSubscription> activeSubscriptions);
}

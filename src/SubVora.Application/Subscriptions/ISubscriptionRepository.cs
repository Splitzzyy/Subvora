using SubVora.Domain.Entities;

namespace SubVora.Application.Subscriptions;

public interface ISubscriptionRepository
{
    Task<UserSubscription> AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<SubscriptionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>How many the user has, for the create quota in <see cref="UserQuotas"/>.</summary>
    Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the user owns a row with this id - the cheap check for a replayed create.</summary>
    Task<bool> ExistsForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<SubscriptionUpdateResult> UpdateAsync(Guid id, Guid userId, CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the current billing date as paid and moves the subscription on one cycle.
    /// <para>
    /// With <paramref name="billingDate"/>, only that charge is settled, and only once: a replay
    /// for a charge already settled is <see cref="MarkPaidStatus.AlreadySettled"/> and writes
    /// nothing, and a date that is not the one due is <see cref="MarkPaidStatus.NotDue"/>. Without
    /// it, whatever is due now is settled - every call advances one cycle.
    /// </para>
    /// </summary>
    Task<MarkPaidResult> MarkPaidAsync(Guid id, Guid userId, DateOnly? billingDate, CancellationToken cancellationToken = default);
}

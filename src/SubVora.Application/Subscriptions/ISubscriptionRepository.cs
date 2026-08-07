using SubVora.Domain.Entities;

namespace SubVora.Application.Subscriptions;

public interface ISubscriptionRepository
{
    Task<UserSubscription> AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<SubscriptionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<SubscriptionDto?> UpdateAsync(Guid id, Guid userId, CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the current billing date as paid and moves the subscription on one cycle. Returns
    /// null when the subscription does not exist for this user.
    /// </summary>
    Task<SubscriptionDto?> MarkPaidAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

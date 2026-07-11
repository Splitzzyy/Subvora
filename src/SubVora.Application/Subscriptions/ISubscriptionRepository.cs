using SubVora.Domain.Entities;

namespace SubVora.Application.Subscriptions;

public interface ISubscriptionRepository
{
    Task<UserSubscription> AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default);
}

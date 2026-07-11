using SubVora.Application.Subscriptions;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly AppDbContext _dbContext;

    public SubscriptionRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserSubscription> AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default)
    {
        _dbContext.UserSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }
}

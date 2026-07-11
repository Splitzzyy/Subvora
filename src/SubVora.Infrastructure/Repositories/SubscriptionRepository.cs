using Microsoft.EntityFrameworkCore;
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

    public async Task<IReadOnlyList<SubscriptionDto>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await BuildDtoQuery(userId).ToListAsync(cancellationToken);

    public async Task<SubscriptionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        await BuildDtoQuery(userId).SingleOrDefaultAsync(dto => dto.Id == id, cancellationToken);

    private IQueryable<SubscriptionDto> BuildDtoQuery(Guid userId) =>
        from s in _dbContext.UserSubscriptions.AsNoTracking()
        where s.UserId == userId
        join category in _dbContext.Categories.AsNoTracking() on s.CategoryId equals category.Id into categoryJoin
        from category in categoryJoin.DefaultIfEmpty()
        join paymentSource in _dbContext.PaymentSources.AsNoTracking() on s.PaymentSourceId equals paymentSource.Id into paymentSourceJoin
        from paymentSource in paymentSourceJoin.DefaultIfEmpty()
        join catalogItem in _dbContext.SubscriptionCatalog.AsNoTracking() on s.CatalogId equals catalogItem.Id into catalogJoin
        from catalogItem in catalogJoin.DefaultIfEmpty()
        select new SubscriptionDto
        {
            Id = s.Id,
            CustomName = s.CustomName,
            CostAmount = s.CostAmount,
            Currency = s.Currency,
            CycleCadence = s.CycleCadence,
            PurchaseDate = s.PurchaseDate,
            NextBillingDate = s.NextBillingDate,
            AlertDaysAdvance = s.AlertDaysAdvance,
            CategoryId = s.CategoryId,
            CategoryName = category != null ? category.Name : null,
            PaymentSourceId = s.PaymentSourceId,
            PaymentSourceLabel = paymentSource != null ? paymentSource.Label : null,
            CatalogLogoUrl = catalogItem != null ? catalogItem.LogoUrl : null,
            IsFreeTrial = s.IsFreeTrial,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt,
        };
}

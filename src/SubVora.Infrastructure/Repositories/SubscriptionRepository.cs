using Microsoft.EntityFrameworkCore;
using SubVora.Application.Billing;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
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

    public async Task<SubscriptionDto?> UpdateAsync(Guid id, Guid userId, CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.UserSubscriptions
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        subscription.CustomName = request.CustomName;
        subscription.CostAmount = request.CostAmount;
        subscription.Currency = request.Currency.ToUpperInvariant();
        subscription.CycleCadence = request.CycleCadence;
        subscription.PurchaseDate = request.PurchaseDate;
        subscription.NextBillingDate = request.NextBillingDate;
        // Both fields are nullable on the shared create/update request precisely so that omitting
        // them on update preserves what's stored: the global alert-days default applies at create
        // time only, and a partial update must not silently deactivate a live subscription.
        if (request.AlertDaysAdvance is int alertDaysAdvance)
        {
            subscription.AlertDaysAdvance = alertDaysAdvance;
        }

        if (request.IsActive is bool isActive)
        {
            subscription.IsActive = isActive;
        }

        subscription.CategoryId = request.CategoryId;
        subscription.PaymentSourceId = request.PaymentSourceId;
        subscription.CatalogId = request.CatalogId;
        subscription.IsFreeTrial = request.IsFreeTrial;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildDtoQuery(userId).SingleOrDefaultAsync(dto => dto.Id == id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.UserSubscriptions
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        _dbContext.UserSubscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SubscriptionDto?> MarkPaidAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.UserSubscriptions
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        // The date being settled is the one currently outstanding, not today: paying a 23 Apr charge
        // on 7 Aug still settles 23 Apr, and the next one is due a cycle after that date rather than
        // a cycle after the user got round to recording it.
        subscription.LastPaidDate = subscription.NextBillingDate;

        if (subscription.CycleCadence == BillingCycleType.OneTime)
        {
            // Nothing further to bill, so the subscription is done rather than perpetually due.
            subscription.IsActive = false;
        }
        else
        {
            subscription.NextBillingDate = BillingCycleAdvancer.AdvanceOneCycle(subscription.NextBillingDate, subscription.CycleCadence);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, userId, cancellationToken);
    }

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
            LastPaidDate = s.LastPaidDate,
            AlertDaysAdvance = s.AlertDaysAdvance,
            CategoryId = s.CategoryId,
            CategoryName = category != null ? category.Name : null,
            PaymentSourceId = s.PaymentSourceId,
            PaymentSourceLabel = paymentSource != null ? paymentSource.Label : null,
            CatalogId = s.CatalogId,
            CatalogLogoUrl = catalogItem != null ? catalogItem.LogoUrl : null,
            IsFreeTrial = s.IsFreeTrial,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt,
        };
}

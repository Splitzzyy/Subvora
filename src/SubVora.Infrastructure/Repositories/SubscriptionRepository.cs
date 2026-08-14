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

    public async Task<SubscriptionUpdateResult> UpdateAsync(Guid id, Guid userId, CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.UserSubscriptions
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            return SubscriptionUpdateResult.NotFound;
        }

        // The version the client read, not the one just loaded. Comparing against the freshly read
        // value would compare the row with itself and never conflict; substituting the client's
        // makes the UPDATE's WHERE clause assert that nothing has changed since they saw it.
        if (request.Version is uint clientVersion)
        {
            _dbContext.Entry(subscription).Property("xmin").OriginalValue = clientVersion;
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

        // Force the row to be written even when nothing above actually differs. Without this, an
        // unchanged payload leaves the entity Unchanged, EF issues no UPDATE at all, and there is no
        // statement for the xmin predicate to be part of - so SaveChangesAsync cannot raise a
        // concurrency exception and the call returns 200 against a row that has moved on.
        //
        // That is not a corner case: the conflict this check exists for is an edit screen opened
        // before a mark-paid and then saved. A user who changed nothing is exactly the user whose
        // save would silently roll the billing date back.
        if (request.Version is not null)
        {
            _dbContext.Entry(subscription).State = EntityState.Modified;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Zero rows matched, so the row moved on between the client reading it and saving.
            // Detached because this context still holds the doomed entity, and anything reusing the
            // scope afterwards would otherwise re-attempt the same failed write.
            _dbContext.Entry(subscription).State = EntityState.Detached;
            return SubscriptionUpdateResult.VersionConflict;
        }

        return SubscriptionUpdateResult.Success(
            await BuildDtoQuery(userId).SingleOrDefaultAsync(dto => dto.Id == id, cancellationToken));
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

    // The category and payment-source sides are filtered to what this user may see before they are
    // joined, so a subscription pointing at someone else's row resolves to a null name/label rather
    // than disclosing it. The controller rejects such a reference on write; this makes the read side
    // safe on its own, including for rows written before that check existed. The catalog is global
    // and unowned, so it joins unfiltered.
    private IQueryable<SubscriptionDto> BuildDtoQuery(Guid userId) =>
        from s in _dbContext.UserSubscriptions.AsNoTracking()
        where s.UserId == userId
        join category in _dbContext.Categories.AsNoTracking().Where(c => c.UserId == null || c.UserId == userId)
            on s.CategoryId equals category.Id into categoryJoin
        from category in categoryJoin.DefaultIfEmpty()
        join paymentSource in _dbContext.PaymentSources.AsNoTracking().Where(p => p.UserId == userId)
            on s.PaymentSourceId equals paymentSource.Id into paymentSourceJoin
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
            // Shadow property, so it has to be read through EF.Property rather than off the entity.
            Version = EF.Property<uint>(s, "xmin"),
        };
}

using Microsoft.EntityFrameworkCore;
using SubVora.Application.PaymentSources;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class PaymentSourceRepository : IPaymentSourceRepository
{
    private readonly AppDbContext _dbContext;

    public PaymentSourceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PaymentSourceDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _dbContext.PaymentSources.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Label)
            .Select(p => new PaymentSourceDto { Id = p.Id, Label = p.Label, SourceType = p.SourceType })
            .ToListAsync(cancellationToken);

    public async Task<PaymentSourceDto> AddAsync(Guid userId, CreatePaymentSourceRequest request, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            UserId = userId,
            Label = request.Label,
            SourceType = request.SourceType,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.PaymentSources.Add(paymentSource);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentSourceDto { Id = paymentSource.Id, Label = paymentSource.Label, SourceType = paymentSource.SourceType };
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var paymentSource = await _dbContext.PaymentSources
            .SingleOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken);
        if (paymentSource is null)
        {
            return false;
        }

        _dbContext.PaymentSources.Remove(paymentSource);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

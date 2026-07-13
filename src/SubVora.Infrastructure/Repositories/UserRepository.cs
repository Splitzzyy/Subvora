using Microsoft.EntityFrameworkCore;
using SubVora.Application.Users;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _dbContext;

    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string?> GetPreferredCurrencyAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredCurrency)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserProfileDto
            {
                Id = u.Id,
                Email = u.Email,
                PreferredCurrency = u.PreferredCurrency,
                DefaultAlertDaysAdvance = u.DefaultAlertDaysAdvance,
            })
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<UserProfileDto?> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.PreferredCurrency = request.PreferredCurrency.ToUpperInvariant();
        user.DefaultAlertDaysAdvance = request.DefaultAlertDaysAdvance;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UserProfileDto
        {
            Id = user.Id,
            Email = user.Email,
            PreferredCurrency = user.PreferredCurrency,
            DefaultAlertDaysAdvance = user.DefaultAlertDaysAdvance,
        };
    }
}

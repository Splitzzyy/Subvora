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
}

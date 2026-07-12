using Microsoft.EntityFrameworkCore;
using SubVora.Application.Devices;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly AppDbContext _dbContext;

    public DeviceTokenRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DeviceTokenDto> UpsertAsync(Guid userId, RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.DeviceTokens
            .SingleOrDefaultAsync(d => d.UserId == userId && d.Token == request.Token, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            existing.LastSeenAt = now;
            existing.Platform = request.Platform;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new DeviceTokenDto { Id = existing.Id, Token = existing.Token, Platform = existing.Platform, LastSeenAt = existing.LastSeenAt };
        }

        var deviceToken = new DeviceToken
        {
            UserId = userId,
            Token = request.Token,
            Platform = request.Platform,
            CreatedAt = now,
            LastSeenAt = now,
        };
        _dbContext.DeviceTokens.Add(deviceToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DeviceTokenDto { Id = deviceToken.Id, Token = deviceToken.Token, Platform = deviceToken.Platform, LastSeenAt = deviceToken.LastSeenAt };
    }
}

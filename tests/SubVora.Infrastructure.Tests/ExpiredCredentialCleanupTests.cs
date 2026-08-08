using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Auth;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class ExpiredCredentialCleanupTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;

    public ExpiredCredentialCleanupTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _dbContext = new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString));
        await _dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString)));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _dbContext.DisposeAsync();
    }

    private ExpiredCredentialCleanupBackgroundService BuildService() =>
        new(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<ILogger<ExpiredCredentialCleanupBackgroundService>>(),
            new ConfigurationBuilder().Build());

    private async Task<Guid> SeedUserAsync()
    {
        var user = new User
        {
            Email = $"cleanup-{Guid.NewGuid()}@example.com",
            PasswordHash = "not-a-real-hash",  // pragma: allowlist secret
            PreferredCurrency = "INR",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedRefreshTokenAsync(Guid userId, DateTimeOffset expiresAt)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = expiresAt,
            CreatedAt = expiresAt.AddDays(-30),
        };
        _dbContext.RefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync();
        return token.Id;
    }

    private async Task<Guid> SeedPasswordResetCodeAsync(Guid userId, DateTimeOffset expiresAt)
    {
        var code = new PasswordResetCode
        {
            UserId = userId,
            CodeHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = expiresAt,
            CreatedAt = expiresAt.AddMinutes(-15),
        };
        _dbContext.PasswordResetCodes.Add(code);
        await _dbContext.SaveChangesAsync();
        return code.Id;
    }

    [Fact]
    public async Task CleanupOnceAsync_DeletesRefreshTokensExpiredBeyondTheRetentionWindow()
    {
        var userId = await SeedUserAsync();
        var longExpired = await SeedRefreshTokenAsync(userId, DateTimeOffset.UtcNow.AddDays(-30));

        await BuildService().CleanupOnceAsync();

        Assert.False(await _dbContext.RefreshTokens.AsNoTracking().AnyAsync(t => t.Id == longExpired));
    }

    [Fact]
    public async Task CleanupOnceAsync_KeepsRecentlyExpiredRefreshTokensSoReuseDetectionStillWorks()
    {
        var userId = await SeedUserAsync();

        // Inside the 7-day window. Deleting this would make a replayed token look like one that
        // never existed, and RefreshAsync distinguishes those two cases.
        var recentlyExpired = await SeedRefreshTokenAsync(userId, DateTimeOffset.UtcNow.AddDays(-1));

        await BuildService().CleanupOnceAsync();

        Assert.True(await _dbContext.RefreshTokens.AsNoTracking().AnyAsync(t => t.Id == recentlyExpired));
    }

    [Fact]
    public async Task CleanupOnceAsync_KeepsLiveRefreshTokens()
    {
        var userId = await SeedUserAsync();
        var live = await SeedRefreshTokenAsync(userId, DateTimeOffset.UtcNow.AddDays(30));

        await BuildService().CleanupOnceAsync();

        Assert.True(await _dbContext.RefreshTokens.AsNoTracking().AnyAsync(t => t.Id == live));
    }

    [Fact]
    public async Task CleanupOnceAsync_DeletesExpiredPasswordResetCodesButKeepsLiveOnes()
    {
        var userId = await SeedUserAsync();
        var longExpired = await SeedPasswordResetCodeAsync(userId, DateTimeOffset.UtcNow.AddDays(-3));
        var live = await SeedPasswordResetCodeAsync(userId, DateTimeOffset.UtcNow.AddMinutes(15));

        await BuildService().CleanupOnceAsync();

        Assert.False(await _dbContext.PasswordResetCodes.AsNoTracking().AnyAsync(c => c.Id == longExpired));
        Assert.True(await _dbContext.PasswordResetCodes.AsNoTracking().AnyAsync(c => c.Id == live));
    }

    [Fact]
    public async Task CleanupOnceAsync_WithNothingToDelete_IsANoOp()
    {
        var userId = await SeedUserAsync();
        var live = await SeedRefreshTokenAsync(userId, DateTimeOffset.UtcNow.AddDays(30));

        await BuildService().CleanupOnceAsync();
        await BuildService().CleanupOnceAsync();

        Assert.True(await _dbContext.RefreshTokens.AsNoTracking().AnyAsync(t => t.Id == live));
    }
}

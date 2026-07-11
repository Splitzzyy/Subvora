using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class FxRateAndRefreshTokenTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public FxRateAndRefreshTokenTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = AppDbContextOptionsFactory.Build(_fixture.ConnectionString);
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task FxRates_DuplicateBaseTargetCurrencyPair_ViolatesUniqueConstraint()
    {
        _dbContext.FxRates.Add(new FxRate { BaseCurrency = "USD", TargetCurrency = "EUR", Rate = 0.92m, FetchedAt = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync();

        _dbContext.FxRates.Add(new FxRate { BaseCurrency = "USD", TargetCurrency = "EUR", Rate = 0.93m, FetchedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task FxRates_DifferentCurrencyPair_DoesNotViolateUniqueConstraint()
    {
        _dbContext.FxRates.Add(new FxRate { BaseCurrency = "USD", TargetCurrency = "INR", Rate = 83.1m, FetchedAt = DateTimeOffset.UtcNow });
        _dbContext.FxRates.Add(new FxRate { BaseCurrency = "INR", TargetCurrency = "USD", Rate = 0.012m, FetchedAt = DateTimeOffset.UtcNow });

        await _dbContext.SaveChangesAsync();

        var count = await _dbContext.FxRates.CountAsync(f => f.BaseCurrency == "USD" && f.TargetCurrency == "INR"
            || f.BaseCurrency == "INR" && f.TargetCurrency == "USD");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RefreshTokens_DeletingUser_CascadesDeleteOfRefreshToken()
    {
        var user = new User { Email = $"refresh-{Guid.NewGuid()}@example.com", PasswordHash = "not-a-real-hash", PreferredCurrency = "USD", CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "not-a-real-token-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.RefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync();

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        var stillExists = await _dbContext.RefreshTokens.AsNoTracking().AnyAsync(t => t.Id == token.Id);
        Assert.False(stillExists);
    }

    [Fact]
    public async Task Migration_CreatesRefreshTokensUserIdIndex()
    {
        var connection = (Npgsql.NpgsqlConnection)_dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_indexes WHERE tablename = 'refresh_tokens' AND indexname = 'idx_refresh_tokens_user_id'";
        var result = await cmd.ExecuteScalarAsync();

        Assert.NotNull(result);
    }
}

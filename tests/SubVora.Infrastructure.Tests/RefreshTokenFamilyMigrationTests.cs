using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// Seeds refresh-token chains on the schema before AddRefreshTokenFamilies, then applies it. Its own
/// class, and so its own container, because it starts from a part-migrated database.
/// </summary>
public class RefreshTokenFamilyMigrationTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public RefreshTokenFamilyMigrationTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _dbContext = new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString));
        await _dbContext.GetService<IMigrator>().MigrateAsync("HardenRefreshAndResetCodes");
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task ExistingChainsBecomeFamilies_StartedByTheirOldestRow()
    {
        var userId = Guid.NewGuid();
        var (root, middle, live, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var rootCreated = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        await _dbContext.Database.ExecuteSqlAsync($"INSERT INTO users (id, email, password_hash, created_at) VALUES ({userId}, {$"m-{userId}@example.com"}, 'x', now())");
        // root -> middle -> live, inserted out of order; other is a separate sign-in.
        await InsertTokenAsync(live, userId, rootCreated.AddDays(2), replacedBy: null);
        await InsertTokenAsync(middle, userId, rootCreated.AddDays(1), replacedBy: live);
        await InsertTokenAsync(root, userId, rootCreated, replacedBy: middle);
        await InsertTokenAsync(other, userId, rootCreated.AddDays(5), replacedBy: null);

        await _dbContext.Database.MigrateAsync();

        var families = await _dbContext.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToDictionaryAsync(t => t.Id, t => (t.FamilyId, t.FamilyStartedAt));
        Assert.Equal((root, rootCreated), families[root]);
        Assert.Equal((root, rootCreated), families[middle]);
        Assert.Equal((root, rootCreated), families[live]);
        Assert.Equal((other, rootCreated.AddDays(5)), families[other]);
    }

    private Task InsertTokenAsync(Guid id, Guid userId, DateTimeOffset createdAt, Guid? replacedBy) =>
        _dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at, created_at, revoked_at, replaced_by_token_id)
            VALUES ({id}, {userId}, {id.ToString("N")}, {createdAt.AddDays(30)}, {createdAt},
                    {(replacedBy is null ? null : createdAt.AddDays(1))}, {replacedBy})
            """);
}

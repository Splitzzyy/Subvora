using Microsoft.EntityFrameworkCore;
using Npgsql;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SubVora.Infrastructure.Tests;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("subvora_test")
        .WithUsername("subvora_test")
        .WithPassword("subvora_test_password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public class SchemaMigrationTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly string[] SeededSystemCategoryNames =
    [
        "Entertainment", "Productivity", "Fitness", "Utilities", "Finance", "Other", "Food", "Travel"
    ];

    private readonly PostgresContainerFixture _fixture;
    private AppDbContext _dbContext = null!;

    public SchemaMigrationTests(PostgresContainerFixture fixture)
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
    public async Task Migration_PutsAUniqueIndexOnTheColumnRefreshTokensAreLookedUpBy()
    {
        // AuthService.RefreshAsync and LogoutAsync both filter on token_hash, and until
        // AddRefreshTokenHashUniqueIndex it was the one column on that path with no index - a
        // sequential scan every ~15 minutes per active client, over the fastest-growing table.
        //
        // Unique because SingleOrDefaultAsync already depends on it: two matching rows would throw
        // rather than return either.
        var indexes = new List<(string Name, bool IsUnique)>();

        await using var command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT i.relname, ix.indisunique
            FROM pg_index ix
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            WHERE t.relname = 'refresh_tokens' AND a.attname = 'token_hash'
            """;

        await _dbContext.Database.OpenConnectionAsync();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add((reader.GetString(0), reader.GetBoolean(1)));
            }
        }

        Assert.NotEmpty(indexes);
        Assert.Contains(indexes, index => index.IsUnique);
    }

    [Fact]
    public async Task Migration_CreatesUsersCategoriesPaymentSourcesTablesWithExpectedColumns()
    {
        var expectedColumnsByTable = new Dictionary<string, string[]>
        {
            ["users"] = ["id", "email", "password_hash", "preferred_currency", "created_at"],
            ["categories"] = ["id", "user_id", "name", "created_at"],
            ["payment_sources"] = ["id", "user_id", "label", "source_type", "created_at"],
        };

        foreach (var (table, expectedColumns) in expectedColumnsByTable)
        {
            var actualColumns = await GetColumnNamesAsync(table);

            Assert.True(actualColumns.Count > 0, $"Expected table '{table}' to exist with columns.");
            foreach (var expectedColumn in expectedColumns)
            {
                Assert.Contains(expectedColumn, actualColumns);
            }
        }
    }

    [Fact]
    public async Task Migration_EnforcesUniqueConstraintOnCategoriesUserIdAndName()
    {
        var user = new User
        {
            Email = $"unique-constraint-{Guid.NewGuid()}@example.com",
            PasswordHash = "not-a-real-hash",
            PreferredCurrency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _dbContext.Categories.Add(new Category { UserId = user.Id, Name = "Streaming", CreatedAt = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync();

        _dbContext.Categories.Add(new Category { UserId = user.Id, Name = "Streaming", CreatedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Migration_SeedsSystemDefaultCategories()
    {
        var systemCategoryNames = await _dbContext.Categories
            .Where(c => c.UserId == null)
            .Select(c => c.Name)
            .ToListAsync();

        foreach (var expectedName in SeededSystemCategoryNames)
        {
            Assert.Contains(expectedName, systemCategoryNames);
        }
    }

    [Fact]
    public async Task Migration_PersistsAndReadsBackAPaymentSourceWithSourceType()
    {
        var user = new User
        {
            Email = $"payment-source-{Guid.NewGuid()}@example.com",
            PasswordHash = "not-a-real-hash",
            PreferredCurrency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _dbContext.PaymentSources.Add(new PaymentSource
        {
            UserId = user.Id,
            Label = "Chase Visa •4021",
            SourceType = PaymentSourceType.Card,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        var stored = await _dbContext.PaymentSources.SingleAsync(p => p.UserId == user.Id);
        Assert.Equal(PaymentSourceType.Card, stored.SourceType);
    }

    private async Task<List<string>> GetColumnNamesAsync(string table)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table";
        cmd.Parameters.AddWithValue("table", table);

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}

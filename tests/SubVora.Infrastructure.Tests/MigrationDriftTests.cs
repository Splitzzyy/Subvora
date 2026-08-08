using Microsoft.EntityFrameworkCore;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// Guards the gap between the entity configurations and the migrations beside them. SchemaMigrationTests
/// applies the history and asserts the resulting columns against a hardcoded list, which passes happily
/// when a configuration changes and no migration is generated to match - the tests build the old schema
/// and assert the old columns. The mismatch then surfaces only in a deployed environment, as a
/// query-time failure.
/// </summary>
public class MigrationDriftTests
{
    [Fact]
    public void Model_HasNoChangesThatAreNotCapturedInAMigration()
    {
        // No database is touched: this compares the model built from the entity configurations
        // against AppDbContextModelSnapshot, so it belongs with the pure tests rather than the
        // Testcontainers-backed ones. The connection string is never opened.
        using var dbContext = new AppDbContext(AppDbContextOptionsFactory.Build(
            "Host=drift-check;Database=drift-check;Username=drift-check;Password=drift-check"));  // pragma: allowlist secret

        Assert.False(
            dbContext.Database.HasPendingModelChanges(),
            "The EF Core model no longer matches the migration snapshot. Generate a migration: "
                + "dotnet ef migrations add <Name> --project src/SubVora.Infrastructure --startup-project src/SubVora.Infrastructure");
    }
}

using Microsoft.EntityFrameworkCore.Design;

namespace SubVora.Infrastructure.Data;

/// <summary>
/// Used by `dotnet ef` at design time only (migrations add/update). Runtime DI
/// registration of AppDbContext happens separately in SubVora.Api's Program.cs.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SUBVORA_DB_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=subvora_dev;Username=subvora;Password=subvora_dev_password";

        var options = AppDbContextOptionsFactory.Build(connectionString);
        return new AppDbContext(options);
    }
}

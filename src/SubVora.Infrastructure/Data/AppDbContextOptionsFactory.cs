using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Enums;

namespace SubVora.Infrastructure.Data;

public static class AppDbContextOptionsFactory
{
    public static DbContextOptions<AppDbContext> Build(string connectionString)
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.MapEnum<PaymentSourceType>("payment_source_type"))
            .UseSnakeCaseNamingConvention()
            .Options;
    }
}

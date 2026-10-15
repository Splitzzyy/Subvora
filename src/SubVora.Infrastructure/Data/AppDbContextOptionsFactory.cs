using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Enums;

namespace SubVora.Infrastructure.Data;

public static class AppDbContextOptionsFactory
{
    /// <summary>
    /// The one place a context is configured, for the API and for design-time tooling alike.
    /// <para>
    /// Transient faults are retried: Neon's free tier scales to zero and restarts compute on its own
    /// schedule, so a pooled connection killed underneath a request, or one refused while compute
    /// wakes, is routine rather than exceptional. Only errors Npgsql classifies as transient are
    /// retried. The catch: a retrying strategy refuses a bare <c>BeginTransaction</c>. Code that needs
    /// a transaction must run it inside <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// so the whole unit is retried rather than half of it.
    /// </para>
    /// </summary>
    public static DbContextOptions<AppDbContext> Build(string connectionString)
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o
                .MapEnum<PaymentSourceType>("payment_source_type")
                .MapEnum<BillingCycleType>("billing_cycle_type")
                .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention()
            .Options;
    }
}

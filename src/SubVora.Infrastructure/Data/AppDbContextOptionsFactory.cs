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
    /// <para>
    /// Every write is retried, not only transactions: each <c>SaveChanges</c> and each
    /// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> as well. A fault can land after the server
    /// committed, with only the reply lost, and the strategy cannot tell that from "never ran" - so
    /// the write runs a second time. A new write path must be safe to replay: it may answer an
    /// error for a write that landed (the client re-reads and sees the truth), but must never apply
    /// twice. Hence a create needs something unique for its replay to collide with instead of
    /// inserting a second row - a key chosen in the API rather than the database default
    /// (subscriptions, payment sources) or a unique column (categories, users, refresh tokens) -
    /// conditional updates are
    /// compare-and-swaps, and a transaction that would not re-apply cleanly hands the strategy a
    /// "did it commit?" check (AuthService.InTransactionAsync). The one accepted double-apply is
    /// the reset-code attempt counter, which errs towards fewer guesses.
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

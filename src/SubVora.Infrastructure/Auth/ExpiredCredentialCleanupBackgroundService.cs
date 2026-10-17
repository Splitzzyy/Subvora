using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SubVora.Application.Scheduling;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Auth;

/// <summary>
/// Deletes refresh tokens and password-reset codes that are far enough past their expiry to be
/// useless. Nothing else ever removed a row from either table: AuthService only sets RevokedAt/UsedAt
/// and filters on ExpiresAt at read time, so both grew without bound.
///
/// <para>
/// refresh_tokens is the fastest-growing table in the schema. Access tokens live 15 minutes and
/// refresh tokens rotate on every use, so one active client writes a row roughly every 15 minutes -
/// a few thousand per user per month against a 0.5 GB database. The rows are also retained
/// credential material: a SHA-256 hash of a dead token is not a live secret, but keeping every one
/// ever issued enlarges what a database compromise yields, for no benefit.
/// </para>
/// </summary>
public class ExpiredCredentialCleanupBackgroundService : BackgroundService
{
    /// <summary>After the FX refresh (01:00) and the day's other work, since nothing depends on it.</summary>
    private const int DefaultCleanupUtcHour = 3;

    /// <summary>
    /// Expired refresh tokens are kept this long before deletion, and the window counts from expiry,
    /// not from revocation. A rotated token is dead, but it is the one the real device still holds if
    /// an attacker rotated a stolen copy first; presenting it is what ends the attacker's chain, so
    /// RefreshAsync treats a rotated token as theft even once it has expired. Deleting it a week
    /// after rotation would let a phone idle for longer fail quietly instead, leaving the attacker
    /// signed in (#249). Past this window the replay fails quietly too, and what still ends the chain
    /// is the absolute session lifetime (AuthService.AbsoluteSessionLifetime).
    /// </summary>
    private static readonly TimeSpan RefreshTokenRetention = TimeSpan.FromDays(7);

    /// <summary>
    /// Shorter, because the codes are weaker: six digits is a 10^6 keyspace, cheap to reverse from a
    /// hash if the table ever leaks. They expire in 15 minutes and a day is ample for diagnosis.
    /// </summary>
    private static readonly TimeSpan PasswordResetCodeRetention = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredCredentialCleanupBackgroundService> _logger;
    private readonly int _cleanupUtcHour;

    public ExpiredCredentialCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredCredentialCleanupBackgroundService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cleanupUtcHour = DailyUtcSchedule.ReadUtcHour(configuration["CredentialCleanup:UtcHour"], DefaultCleanupUtcHour);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Housekeeping must never take the host down, and a skipped pass costs only a day
                // of rows that the next pass collects anyway.
                _logger.LogError(ex, "Expired credential cleanup failed; rows remain and the next pass will retry.");
            }

            try
            {
                await Task.Delay(DailyUtcSchedule.DelayUntilNextRun(DateTimeOffset.UtcNow, _cleanupUtcHour), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs a single cleanup pass. Public so tests can drive one iteration directly instead of the infinite ExecuteAsync loop.</summary>
    public async Task CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;

        // ExecuteDelete: one DELETE per table, no entities loaded. The row count is unbounded by
        // definition here, so materialising it would be the one way this job could exhaust memory.
        var refreshTokensDeleted = await dbContext.RefreshTokens
            .Where(t => t.ExpiresAt < now - RefreshTokenRetention)
            .ExecuteDeleteAsync(cancellationToken);

        var resetCodesDeleted = await dbContext.PasswordResetCodes
            .Where(c => c.ExpiresAt < now - PasswordResetCodeRetention)
            .ExecuteDeleteAsync(cancellationToken);

        if (refreshTokensDeleted > 0 || resetCodesDeleted > 0)
        {
            _logger.LogInformation(
                "Credential cleanup removed {RefreshTokenCount} expired refresh token(s) and {ResetCodeCount} expired password reset code(s).",
                refreshTokensDeleted,
                resetCodesDeleted);
        }
    }
}

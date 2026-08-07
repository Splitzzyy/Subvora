using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SubVora.Application.Billing;
using SubVora.Application.Scheduling;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Billing;

/// <summary>
/// Rolls every passed billing date forward to its next future occurrence, and retires OneTime
/// subscriptions instead of advancing them. Without this a subscription's date stays in the past
/// forever: the burn rate keeps counting a one-off purchase, and the app shows a renewal that
/// already happened.
///
/// This used to also scan for due alerts and send them via FCM. Reminders are now scheduled
/// on-device by the mobile client, which knows the same dates from its local mirror, so nothing
/// server-side needs to decide when to notify.
/// </summary>
public class BillingDateAdvanceBackgroundService : BackgroundService
{
    /// <summary>Small hours UTC by default: quiet enough not to compete with daytime traffic.</summary>
    private const int DefaultScanUtcHour = 2;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBillingDateScanner _scanner;
    private readonly ILogger<BillingDateAdvanceBackgroundService> _logger;
    private readonly int _scanUtcHour;

    public BillingDateAdvanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IBillingDateScanner scanner,
        ILogger<BillingDateAdvanceBackgroundService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _logger = logger;
        _scanUtcHour = DailyUtcSchedule.ReadUtcHour(configuration["RenewalScan:UtcHour"], DefaultScanUtcHour);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Billing date advance failed; will retry on the next interval.");
            }

            try
            {
                // The scan on startup above is deliberate - it is a catch-up for the skipped-day
                // case a drifting schedule used to cause, and it is safe because a second run on
                // the same day finds nothing left with a passed date.
                await Task.Delay(DailyUtcSchedule.DelayUntilNextRun(DateTimeOffset.UtcNow, _scanUtcHour), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs a single pass for a given day. Public so tests can drive one iteration directly instead of the infinite ExecuteAsync loop.</summary>
    public async Task ScanOnceAsync(DateOnly? today = null, CancellationToken cancellationToken = default)
    {
        var scanDay = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Only rows already past their billing date matter here; everything else is a future date
        // the job would load and immediately discard.
        var dueForAdvance = await dbContext.UserSubscriptions
            .Where(s => s.IsActive && s.NextBillingDate < scanDay)
            .ToListAsync(cancellationToken);

        var toAdvance = _scanner.FindDueForAdvance(scanDay, dueForAdvance);
        if (toAdvance.Count == 0)
        {
            return;
        }

        foreach (var subscription in toAdvance)
        {
            if (subscription.CycleCadence == BillingCycleType.OneTime)
            {
                subscription.IsActive = false;
            }
            else
            {
                subscription.NextBillingDate = BillingCycleAdvancer.AdvanceTo(subscription.NextBillingDate, subscription.CycleCadence, scanDay);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Advanced {Count} subscription(s) past their billing date.", toAdvance.Count);
    }
}

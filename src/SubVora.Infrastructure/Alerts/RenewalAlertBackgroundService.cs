using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SubVora.Application.Alerts;
using SubVora.Application.Notifications;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Alerts;

public class RenewalAlertBackgroundService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRenewalAlertScanner _scanner;
    private readonly ILogger<RenewalAlertBackgroundService> _logger;

    public RenewalAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        IRenewalAlertScanner scanner,
        ILogger<RenewalAlertBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _logger = logger;
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
                _logger.LogError(ex, "Renewal alert scan failed; will retry on the next interval.");
            }

            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs a single scan pass for a given day. Public so tests can drive one iteration directly instead of the infinite ExecuteAsync loop.</summary>
    public async Task ScanOnceAsync(DateOnly? today = null, CancellationToken cancellationToken = default)
    {
        var scanDay = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var dayStartUtc = new DateTimeOffset(scanDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEndUtc = dayStartUtc.AddDays(1);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Only two kinds of row matter to a scan: those already past their billing date (advance)
        // and those renewing exactly alert_days_advance from today (alert). Everything else is a
        // future date the job would load and immediately discard.
        var activeSubscriptions = await dbContext.UserSubscriptions
            .Where(s => s.IsActive)
            .Where(s => s.NextBillingDate < scanDay || s.NextBillingDate.AddDays(-s.AlertDaysAdvance) == scanDay)
            .ToListAsync(cancellationToken);

        // Only the (subscription, lead time) pair drives the idempotency guard, so project rather
        // than materializing whole NotificationLog entities.
        var existingLogsForToday = await dbContext.NotificationsLog
            .Where(n => n.SentAt >= dayStartUtc && n.SentAt < dayEndUtc)
            .Select(n => new NotificationLog { UserSubscriptionId = n.UserSubscriptionId, AlertDaysAdvance = n.AlertDaysAdvance })
            .ToListAsync(cancellationToken);

        // Advancement runs on every pass and *before* the alert scan: a stale date most needs
        // repairing precisely on a day when nothing is due to alert, and a date repaired into
        // today's alert window should alert on this same pass rather than being skipped by the
        // scanner's exact-day predicate. activeSubscriptions is already loaded and tracked here,
        // so this mutates in place rather than re-querying.
        await AdvancePassedBillingDatesAsync(dbContext, scanDay, activeSubscriptions, cancellationToken);

        var dueSubscriptions = _scanner.Scan(scanDay, activeSubscriptions, existingLogsForToday);
        if (dueSubscriptions.Count == 0)
        {
            return;
        }

        foreach (var subscription in dueSubscriptions)
        {
            dbContext.NotificationsLog.Add(new NotificationLog
            {
                UserSubscriptionId = subscription.Id,
                AlertDaysAdvance = subscription.AlertDaysAdvance,
                SentAt = DateTimeOffset.UtcNow,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Resolved lazily (not constructor-injected) and guarded here: if push isn't configured
        // yet (no Firebase credentials - see technical_requirements.backend-hardening.md [HITL]),
        // this must degrade to "skip push delivery" rather than crash the whole host at startup,
        // since RenewalAlertBackgroundService is a singleton hosted service.
        IPushNotificationSender pushNotificationSender;
        try
        {
            pushNotificationSender = scope.ServiceProvider.GetRequiredService<IPushNotificationSender>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Push notification sender is not available; notifications_log was still written, but no push was sent this scan.");
            return;
        }

        foreach (var subscription in dueSubscriptions)
        {
            await SendPushForSubscriptionAsync(dbContext, pushNotificationSender, subscription, cancellationToken);
        }
    }

    /// <summary>
    /// Rolls every already-passed billing date forward to its next future occurrence, and retires
    /// OneTime subscriptions instead of advancing them. Idempotent: a second run on the same day
    /// finds nothing left with a passed date and writes nothing.
    /// </summary>
    private async Task AdvancePassedBillingDatesAsync(AppDbContext dbContext, DateOnly scanDay, IReadOnlyList<UserSubscription> activeSubscriptions, CancellationToken cancellationToken)
    {
        var dueForAdvance = _scanner.FindDueForAdvance(scanDay, activeSubscriptions);
        if (dueForAdvance.Count == 0)
        {
            return;
        }

        foreach (var subscription in dueForAdvance)
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
        _logger.LogInformation("Advanced {Count} subscription(s) past their billing date.", dueForAdvance.Count);
    }

    private async Task SendPushForSubscriptionAsync(AppDbContext dbContext, IPushNotificationSender pushNotificationSender, UserSubscription subscription, CancellationToken cancellationToken)
    {
        var deviceTokens = await dbContext.DeviceTokens
            .Where(d => d.UserId == subscription.UserId)
            .ToListAsync(cancellationToken);

        foreach (var deviceToken in deviceTokens)
        {
            try
            {
                var result = await pushNotificationSender.SendAsync(
                    deviceToken.Token,
                    "Subscription renewing soon",
                    $"{subscription.CustomName} renews on {subscription.NextBillingDate:yyyy-MM-dd}.",
                    cancellationToken);

                if (result == PushSendResult.TokenInvalid)
                {
                    dbContext.DeviceTokens.Remove(deviceToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad device token/transient FCM failure must not block delivery to the
                // user's other devices or the rest of this scan's due subscriptions.
                _logger.LogWarning(ex, "Push send failed for a device token; will retry on the next scan.");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

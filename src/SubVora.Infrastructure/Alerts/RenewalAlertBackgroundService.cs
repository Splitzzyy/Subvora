using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SubVora.Application.Alerts;
using SubVora.Application.Notifications;
using SubVora.Domain.Entities;
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

        var activeSubscriptions = await dbContext.UserSubscriptions
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);
        var existingLogsForToday = await dbContext.NotificationsLog
            .Where(n => n.SentAt >= dayStartUtc && n.SentAt < dayEndUtc)
            .ToListAsync(cancellationToken);

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

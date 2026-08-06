using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Notifications;

/// <summary>
/// Hands the planned reminders to the OS, which then delivers them whether or not the app is
/// running - a scheduled local notification appears on the lock screen exactly like a pushed one.
/// The app is only needed to *decide* the schedule, not to show it.
/// </summary>
public class LocalRenewalNotificationScheduler : IRenewalNotificationScheduler
{
    private readonly ILogger<LocalRenewalNotificationScheduler> _logger;

    public LocalRenewalNotificationScheduler(ILogger<LocalRenewalNotificationScheduler> logger)
    {
        _logger = logger;
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await LocalNotificationCenter.Current.AreNotificationsEnabled())
            {
                return true;
            }

            return await LocalNotificationCenter.Current.RequestNotificationPermission();
        }
        catch (Exception ex)
        {
            // Reminders are a bonus, never a precondition for using the app.
            _logger.LogWarning(ex, "Could not determine or request notification permission.");
            return false;
        }
    }

    public async Task SyncAsync(IEnumerable<SubscriptionDto> subscriptions, CancellationToken cancellationToken = default)
    {
        var planned = RenewalNotificationPlanner.Plan(subscriptions, DateTime.Now);

        try
        {
            // Cancel first, unconditionally: an edited date, a changed lead time and a deleted
            // subscription all have to remove their old reminder, and re-deriving the whole set is
            // cheaper than working out which of those happened.
            LocalNotificationCenter.Current.CancelAll();

            foreach (var notification in planned)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await LocalNotificationCenter.Current.Show(new NotificationRequest
                {
                    NotificationId = notification.Id,
                    Title = notification.Title,
                    Description = notification.Body,
                    Schedule = new NotificationRequestSchedule { NotifyTime = notification.NotifyAt },
                });
            }

            _logger.LogInformation("Scheduled {Count} renewal reminder(s).", planned.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not schedule renewal reminders.");
        }
    }
}

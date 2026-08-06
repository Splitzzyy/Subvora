using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Notifications;

/// <summary>
/// Wraps the platform's local-notification API the way <c>IConnectivityService</c> and
/// <c>ITokenStore</c> wrap theirs, so the view models can be tested without one.
/// </summary>
public interface IRenewalNotificationScheduler
{
    /// <summary>
    /// Replaces every reminder this app has pending with the set implied by <paramref name="subscriptions"/>.
    /// Full replace rather than a diff: the list is at most 64 entries, and reconciling additions,
    /// date edits, deletions and lead-time changes separately is far more code than re-deriving it.
    /// </summary>
    Task SyncAsync(IEnumerable<SubscriptionDto> subscriptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for notification permission if it has not been granted. Returns false when the user
    /// declines - callers must carry on regardless, since the app is fully usable without reminders.
    /// </summary>
    Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default);
}

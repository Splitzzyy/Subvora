namespace SubVora.Application.Subscriptions;

/// <summary>Why an update did or did not happen. Distinguishes the two failures so the controller can answer 404 or 409 rather than guessing from a null.</summary>
public enum SubscriptionUpdateStatus
{
    Updated,

    /// <summary>No such subscription owned by the caller.</summary>
    NotFound,

    /// <summary>
    /// The caller sent a version, and the stored row has moved on since they read it. Their edit
    /// was written against a state that no longer exists, so applying it would silently discard
    /// whatever happened in between.
    /// </summary>
    VersionConflict,
}

/// <summary>The outcome of an update, plus the updated record when there is one.</summary>
public sealed record SubscriptionUpdateResult(SubscriptionUpdateStatus Status, SubscriptionDto? Subscription)
{
    public static SubscriptionUpdateResult Success(SubscriptionDto? subscription) => new(SubscriptionUpdateStatus.Updated, subscription);

    public static SubscriptionUpdateResult NotFound { get; } = new(SubscriptionUpdateStatus.NotFound, null);

    public static SubscriptionUpdateResult VersionConflict { get; } = new(SubscriptionUpdateStatus.VersionConflict, null);
}

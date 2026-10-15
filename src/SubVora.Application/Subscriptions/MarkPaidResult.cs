namespace SubVora.Application.Subscriptions;

/// <summary>What a mark-paid did. Lets the controller tell a replay (200) from a stale tap (409) and a missing row (404).</summary>
public enum MarkPaidStatus
{
    /// <summary>The charge was settled by this call.</summary>
    Settled,

    /// <summary>The named charge had already been settled - a retry or a second device. Nothing was written.</summary>
    AlreadySettled,

    /// <summary>The named charge is neither the one currently due nor one already settled.</summary>
    NotDue,

    /// <summary>No such subscription owned by the caller.</summary>
    NotFound,
}

/// <summary>The outcome of a mark-paid, plus the subscription as it now stands when there is one.</summary>
public sealed record MarkPaidResult(MarkPaidStatus Status, SubscriptionDto? Subscription)
{
    public static MarkPaidResult Settled(SubscriptionDto? subscription) => new(MarkPaidStatus.Settled, subscription);

    public static MarkPaidResult AlreadySettled(SubscriptionDto? subscription) => new(MarkPaidStatus.AlreadySettled, subscription);

    public static MarkPaidResult NotDue { get; } = new(MarkPaidStatus.NotDue, null);

    public static MarkPaidResult NotFound { get; } = new(MarkPaidStatus.NotFound, null);
}

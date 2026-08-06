namespace SubVora.Mobile.Messages;

/// <summary>
/// Published whenever something that feeds the burn rate changes - a subscription created, edited
/// or deleted, or the home currency switched in Settings. The always-visible banner listens for
/// this and refreshes, which is what makes the headline figure follow the user's own edits.
///
/// Refresh-on-mutation rather than polling or a push channel: this account's spend only changes
/// when this user acts, so there is no second writer to hear about.
/// </summary>
public sealed record SubscriptionsChangedMessage;

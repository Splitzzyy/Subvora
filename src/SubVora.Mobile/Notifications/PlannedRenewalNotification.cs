namespace SubVora.Mobile.Notifications;

/// <summary>One reminder the OS should fire on our behalf, already resolved to a local wall-clock time.</summary>
/// <param name="Id">Stable only within a batch - every sync cancels the previous set and reschedules from scratch.</param>
public record PlannedRenewalNotification(int Id, string Title, string Body, DateTime NotifyAt);

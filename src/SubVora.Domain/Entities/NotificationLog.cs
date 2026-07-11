namespace SubVora.Domain.Entities;

public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid UserSubscriptionId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public int AlertDaysAdvance { get; set; }
}

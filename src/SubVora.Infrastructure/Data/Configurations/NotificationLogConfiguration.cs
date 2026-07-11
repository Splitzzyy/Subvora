using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notifications_log");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(n => n.SentAt).HasDefaultValueSql("now()");
        builder.Property(n => n.AlertDaysAdvance).IsRequired();

        builder.HasOne<UserSubscription>()
            .WithMany()
            .HasForeignKey(n => n.UserSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(n => new { n.UserSubscriptionId, n.AlertDaysAdvance, n.SentAt }).IsUnique();
    }
}

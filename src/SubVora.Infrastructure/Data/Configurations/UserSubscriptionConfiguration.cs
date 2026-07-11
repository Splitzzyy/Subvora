using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("user_subscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.CustomName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.CostAmount).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();

        // ValueGeneratedNever: same reasoning as PaymentSource.SourceType (Slice 2) - the
        // app always supplies CycleCadence explicitly, and a HasDefaultValueSql property
        // is otherwise treated as store-generated, causing EF to overwrite the explicitly
        // set value with the column default after insert.
        builder.Property(s => s.CycleCadence)
            .HasDefaultValueSql("'monthly'::billing_cycle_type")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(s => s.PurchaseDate).IsRequired();
        builder.Property(s => s.NextBillingDate).IsRequired();

        builder.Property(s => s.AlertDaysAdvance).HasDefaultValue(3).IsRequired();
        builder.Property(s => s.IsFreeTrial).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.IsActive).HasDefaultValue(true).IsRequired();

        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<SubscriptionCatalogItem>()
            .WithMany()
            .HasForeignKey(s => s.CatalogId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PaymentSource>()
            .WithMany()
            .HasForeignKey(s => s.PaymentSourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.UserId).HasDatabaseName("idx_subs_user_id");

        builder.HasIndex(s => s.NextBillingDate)
            .HasDatabaseName("idx_subs_next_billing")
            .HasFilter("is_active = TRUE");
    }
}

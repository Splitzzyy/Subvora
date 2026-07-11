using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class PaymentSourceConfiguration : IEntityTypeConfiguration<PaymentSource>
{
    public void Configure(EntityTypeBuilder<PaymentSource> builder)
    {
        builder.ToTable("payment_sources");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.Label).HasMaxLength(100).IsRequired();

        // ValueGeneratedNever: without this, EF treats a property with HasDefaultValueSql
        // as store-generated and overwrites the explicitly-set value with the column's
        // default after every insert (verified - Card silently became Other on read-back).
        // The app always supplies SourceType explicitly (defaults to Other in the entity).
        builder.Property(p => p.SourceType)
            .HasDefaultValueSql("'other'::payment_source_type")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}

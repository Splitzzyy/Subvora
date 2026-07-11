using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class SubscriptionCatalogItemConfiguration : IEntityTypeConfiguration<SubscriptionCatalogItem>
{
    public void Configure(EntityTypeBuilder<SubscriptionCatalogItem> builder)
    {
        builder.ToTable("subscription_catalog");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.ProviderName).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.ProviderName).IsUnique();

        builder.Property(s => s.LogoUrl).HasMaxLength(512);

        builder.Property(s => s.SemanticEmbedding).HasColumnType("vector(1536)");

        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("fx_rates");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(f => f.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(f => f.TargetCurrency).HasMaxLength(3).IsRequired();
        builder.Property(f => f.Rate).HasColumnType("numeric(18,8)").IsRequired();

        builder.Property(f => f.FetchedAt).HasDefaultValueSql("now()");

        builder.HasIndex(f => new { f.BaseCurrency, f.TargetCurrency }).IsUnique();
    }
}

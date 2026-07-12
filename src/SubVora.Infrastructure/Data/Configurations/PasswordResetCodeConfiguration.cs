using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class PasswordResetCodeConfiguration : IEntityTypeConfiguration<PasswordResetCode>
{
    public void Configure(EntityTypeBuilder<PasswordResetCode> builder)
    {
        builder.ToTable("password_reset_codes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.CodeHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(p => p.ExpiresAt).IsRequired();
        builder.Property(p => p.AttemptCount).HasDefaultValue(0);

        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(p => p.UserId).HasDatabaseName("ix_password_reset_codes_user_id");
    }
}

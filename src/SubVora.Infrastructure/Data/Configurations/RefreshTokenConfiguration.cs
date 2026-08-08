using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.TokenHash).HasMaxLength(512).IsRequired();
        builder.Property(r => r.ExpiresAt).IsRequired();

        builder.Property(r => r.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(r => r.UserId).HasDatabaseName("idx_refresh_tokens_user_id");

        // The column every refresh and logout looks a token up by, and until now the one column on
        // the hottest query with no index at all - refresh tokens rotate on every use, so an active
        // client ran a sequential scan of this table roughly every 15 minutes.
        //
        // Unique rather than a plain index: the value is a SHA-256 of 32 cryptographically random
        // bytes, so uniqueness is the invariant AuthService already relies on - SingleOrDefaultAsync
        // throws rather than picking one if two rows ever match. This makes the database enforce
        // what the code assumes.
        builder.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
    }
}

using Microsoft.EntityFrameworkCore;
using SubVora.Domain.Entities;

namespace SubVora.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<PaymentSource> PaymentSources => Set<PaymentSource>();
    public DbSet<SubscriptionCatalogItem> SubscriptionCatalog => Set<SubscriptionCatalogItem>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<NotificationLog> NotificationsLog => Set<NotificationLog>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Trigram similarity backs the free-text catalog match (word_similarity in
        // SubscriptionCatalogSearchRepository). The former pgvector extension is gone with the
        // embeddings that used it - see the ReplaceCatalogEmbeddingWithTrigram migration.
        modelBuilder.HasPostgresExtension("pg_trgm");

        // Native enum mapping (payment_source_type) is registered via MapEnum() inside
        // UseNpgsql() in AppDbContextOptionsFactory - not needed here for EF 9+.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

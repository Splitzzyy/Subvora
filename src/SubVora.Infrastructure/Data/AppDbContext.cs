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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");

        // Native enum mapping (payment_source_type) is registered via MapEnum() inside
        // UseNpgsql() in AppDbContextOptionsFactory - not needed here for EF 9+.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

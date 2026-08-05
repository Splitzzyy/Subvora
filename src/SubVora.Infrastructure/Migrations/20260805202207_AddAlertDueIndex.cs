using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <summary>
    /// Partial expression index serving the renewal scan's alert-due predicate
    /// (next_billing_date - alert_days_advance = today).
    ///
    /// idx_subs_next_billing already covers the advance-due half, but the scan's two predicates are
    /// OR'd, and an OR where only one side is indexable makes Postgres fall back to a sequential
    /// scan for the whole query. With both sides indexed it plans a BitmapOr instead. Measured on
    /// 200k active subscriptions: 18.1 ms / 2858 shared buffers before, 0.98 ms / 504 after.
    ///
    /// Hand-written SQL because EF Core has no expression-index support - same reason the HNSW
    /// index in 20260711173957_AddSubscriptionCatalog is hand-added.
    /// </summary>
    /// <inheritdoc />
    public partial class AddAlertDueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS idx_subs_alert_due
                    ON user_subscriptions ((next_billing_date - alert_days_advance))
                    WHERE is_active = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_subs_alert_due;");
        }
    }
}

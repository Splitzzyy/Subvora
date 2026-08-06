using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <summary>
    /// Adds Food and Travel to the system categories seeded by SeedSystemCategories.
    ///
    /// Food delivery and quick-commerce memberships (Swiggy One, Zomato Gold, Zepto Pass,
    /// BigBasket Star) are among the most commonly held recurring subscriptions in India, and
    /// filing them under "Other" makes the dashboard's per-category breakdown useless for exactly
    /// the spend a user most wants to see.
    ///
    /// Travel ships with no catalog providers of its own - Indian travel subscriptions are thin
    /// enough that inventing entries would be worse than leaving the bucket empty. It earns its
    /// place anyway: categories are assignable per subscription, so a user tracking a travel pass
    /// manually can file it correctly instead of dropping it into "Other".
    /// </summary>
    /// <inheritdoc />
    public partial class SeedFoodAndTravelCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ON CONFLICT is not available here: categories has no unique index on (user_id, name),
            // and adding one is a schema change this migration has no business making. The NOT
            // EXISTS guard keeps it re-runnable instead.
            migrationBuilder.Sql("""
                INSERT INTO categories (user_id, name)
                SELECT NULL, seed.name
                FROM (VALUES ('Food'), ('Travel')) AS seed (name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM categories existing
                    WHERE existing.user_id IS NULL AND existing.name = seed.name
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Subscriptions and catalog rows pointing at these categories have category_id set to
            // NULL by the ON DELETE SET NULL foreign keys, so a rollback costs a category label and
            // nothing else.
            migrationBuilder.Sql("""
                DELETE FROM categories
                WHERE user_id IS NULL AND name IN ('Food', 'Travel');
                """);
        }
    }
}

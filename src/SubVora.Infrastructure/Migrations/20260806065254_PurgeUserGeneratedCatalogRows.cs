using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <summary>
    /// Removes the subscription_catalog rows written from free-text resolve input before
    /// SubscriptionMatchService stopped writing them.
    ///
    /// subscription_catalog is global and has no owner column, so every one of those rows is a
    /// piece of one user's typing that FindNearestAsync could return to any other user as a
    /// suggestion. Nulling the embedding would hide them from matching, but
    /// CatalogEmbeddingBackfillService fills null embeddings back in on the next start, so they
    /// have to go.
    ///
    /// Seeded rows carry the deterministic 5eedca70-… ids from SeedSubscriptionCatalog; anything
    /// else in the table came from the runtime write path. Subscriptions pointing at a purged row
    /// have catalog_id set to NULL first (it is nullable, and the same state a Manual-tier save
    /// produces) - they keep their custom_name and only lose a logo that came from another user's
    /// typo anyway.
    /// </summary>
    /// <inheritdoc />
    public partial class PurgeUserGeneratedCatalogRows : Migration
    {
        private static string SeededIdList() =>
            string.Join(", ", SeedSubscriptionCatalog.SeededIds.Select(id => $"'{id}'::uuid"));

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seededIds = SeededIdList();

            migrationBuilder.Sql($"""
                UPDATE user_subscriptions
                SET catalog_id = NULL
                WHERE catalog_id IS NOT NULL AND catalog_id NOT IN ({seededIds});
                """);

            migrationBuilder.Sql($"DELETE FROM subscription_catalog WHERE id NOT IN ({seededIds});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately irreversible: the deleted rows were user-entered text that should never
            // have been in a shared table, and re-creating them is the opposite of the fix.
        }
    }
}

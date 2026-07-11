using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedSystemCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Placeholder system default categories (user_id IS NULL). Content is a
            // product decision pending sign-off (technical_requirements.md §5) -
            // adjustable via a later migration without breaking the schema.
            migrationBuilder.Sql("""
                INSERT INTO categories (user_id, name) VALUES
                    (NULL, 'Entertainment'),
                    (NULL, 'Productivity'),
                    (NULL, 'Fitness'),
                    (NULL, 'Utilities'),
                    (NULL, 'Finance'),
                    (NULL, 'Other');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM categories
                WHERE user_id IS NULL
                    AND name IN ('Entertainment', 'Productivity', 'Fitness', 'Utilities', 'Finance', 'Other');
                """);
        }
    }
}

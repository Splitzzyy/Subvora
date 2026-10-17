using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "family_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "family_started_at",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // Existing chains become families: each token without a predecessor starts one, and every
            // rotation (replaced_by_token_id) passes it on. Left at the column defaults, every row would
            // be its own family, so replaying a token rotated before this migration would end only
            // itself and leave the chain it was rotated into alive. The start is the oldest surviving
            // row's created_at; cleanup has long deleted anything older than ~37 days, so no chain is
            // past the absolute lifetime on deploy and nobody is signed out by it.
            migrationBuilder.Sql("""
                WITH RECURSIVE chain AS (
                    SELECT t.id, t.id AS family_id, t.created_at AS family_started_at
                    FROM refresh_tokens t
                    WHERE NOT EXISTS (SELECT 1 FROM refresh_tokens p WHERE p.replaced_by_token_id = t.id)
                    UNION ALL
                    SELECT next.id, chain.family_id, chain.family_started_at
                    FROM chain
                    JOIN refresh_tokens cur ON cur.id = chain.id
                    JOIN refresh_tokens next ON next.id = cur.replaced_by_token_id
                )
                UPDATE refresh_tokens t
                SET family_id = chain.family_id, family_started_at = chain.family_started_at
                FROM chain
                WHERE t.id = chain.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "family_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "family_started_at",
                table: "refresh_tokens");
        }
    }
}

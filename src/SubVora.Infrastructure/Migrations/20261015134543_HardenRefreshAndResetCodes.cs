using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenRefreshAndResetCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_token_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            // Before the unique index can exist, retire all but the newest live code per user.
            // Concurrent forgot-password requests could leave two; only the newest was ever checked.
            migrationBuilder.Sql("""
                UPDATE password_reset_codes p SET used_at = now()
                WHERE p.used_at IS NULL AND EXISTS (
                    SELECT 1 FROM password_reset_codes q
                    WHERE q.user_id = p.user_id AND q.used_at IS NULL
                      AND (q.created_at > p.created_at OR (q.created_at = p.created_at AND q.id > p.id)));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_password_reset_codes_live_per_user",
                table: "password_reset_codes",
                column: "user_id",
                unique: true,
                filter: "used_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_password_reset_codes_live_per_user",
                table: "password_reset_codes");

            migrationBuilder.DropColumn(
                name: "replaced_by_token_id",
                table: "refresh_tokens");
        }
    }
}

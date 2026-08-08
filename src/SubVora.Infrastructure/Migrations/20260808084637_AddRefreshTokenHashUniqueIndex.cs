using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenHashUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_refresh_tokens_token_hash",
                table: "refresh_tokens");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DefaultPreferredCurrencyInr : Migration
    {
        /// <inheritdoc />
        // Column default only - existing rows keep whatever the user already chose. Backfilling
        // would silently overwrite a deliberate setting with a new default.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "preferred_currency",
                table: "users",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "INR",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldDefaultValue: "USD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "preferred_currency",
                table: "users",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldDefaultValue: "INR");
        }
    }
}

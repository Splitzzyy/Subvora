using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDefaultAlertDaysAdvance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "default_alert_days_advance",
                table: "users",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_alert_days_advance",
                table: "users");
        }
    }
}

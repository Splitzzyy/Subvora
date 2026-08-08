using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Maps Postgres' xmin system column as a concurrency token on user_subscriptions.
    /// <para>
    /// Reads as if it adds a column, and does not: xmin already exists on every table, and Npgsql's
    /// SQL generator recognises it and emits no DDL at all. `dotnet ef migrations script` for this
    /// migration produces only the __EFMigrationsHistory insert. So there is no column to add, no
    /// backfill, no lock taken on an existing table - the migration exists purely to record the
    /// model change.
    /// </para>
    /// </summary>
    public partial class MapUserSubscriptionRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "user_subscriptions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "user_subscriptions");
        }
    }
}

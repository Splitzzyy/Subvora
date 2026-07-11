using System;
using Microsoft.EntityFrameworkCore.Migrations;
using SubVora.Domain.Enums;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "user_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    custom_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cost_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cycle_cadence = table.Column<BillingCycleType>(type: "billing_cycle_type", nullable: false, defaultValueSql: "'monthly'::billing_cycle_type"),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    next_billing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    alert_days_advance = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    is_free_trial = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_subscriptions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_subscriptions_payment_sources_payment_source_id",
                        column: x => x.payment_source_id,
                        principalTable: "payment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_subscriptions_subscription_catalog_catalog_id",
                        column: x => x.catalog_id,
                        principalTable: "subscription_catalog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_subs_next_billing",
                table: "user_subscriptions",
                column: "next_billing_date",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_subs_user_id",
                table: "user_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_catalog_id",
                table: "user_subscriptions",
                column: "catalog_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_category_id",
                table: "user_subscriptions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_payment_source_id",
                table: "user_subscriptions",
                column: "payment_source_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_subscriptions");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}

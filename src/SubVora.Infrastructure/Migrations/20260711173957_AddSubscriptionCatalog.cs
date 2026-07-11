using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet");

            migrationBuilder.CreateTable(
                name: "subscription_catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    provider_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    logo_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    semantic_embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_catalog", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_catalog_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_catalog_category_id",
                table: "subscription_catalog",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_catalog_provider_name",
                table: "subscription_catalog",
                column: "provider_name",
                unique: true);

            // EF Core has no native HNSW support - hand-added per technical_requirements.md §2.
            migrationBuilder.Sql(
                "CREATE INDEX ON subscription_catalog USING hnsw (semantic_embedding vector_cosine_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_catalog");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}

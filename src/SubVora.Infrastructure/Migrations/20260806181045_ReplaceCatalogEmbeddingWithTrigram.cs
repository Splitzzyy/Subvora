using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCatalogEmbeddingWithTrigram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "semantic_embedding",
                table: "subscription_catalog");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<string>(
                name: "semantic_embedding",
                table: "subscription_catalog",
                type: "vector(1536)",
                nullable: true);
        }
    }
}

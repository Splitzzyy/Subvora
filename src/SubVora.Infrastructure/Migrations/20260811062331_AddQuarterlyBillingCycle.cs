using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <summary>
    /// Adds 'quarterly' to the native billing_cycle_type enum. Generates
    /// <c>ALTER TYPE billing_cycle_type ADD VALUE 'quarterly' AFTER 'one_time'</c>.
    /// <para>
    /// Runs inside the migration's transaction, which is only legal from PostgreSQL 12 - and only
    /// because nothing here <em>uses</em> the new label. Neon and the pgvector/pg16 test container
    /// are both well past that. A future migration that inserts or defaults a row to 'quarterly'
    /// cannot be combined with an ADD VALUE in the same migration.
    /// </para>
    /// <para>
    /// <c>Down</c> is deliberately empty: PostgreSQL cannot drop an enum label, and Npgsql throws
    /// rather than generating SQL for it. Rolling this back means recreating the type and rewriting
    /// the column - roll forward instead. The generated body was removed so a rollback is a no-op
    /// rather than an exception at script-generation time.
    /// </para>
    /// <para>
    /// Ordering: deploys must apply this <em>before</em> the code that can emit 'quarterly', which is
    /// already the flow - render.yaml sets autoDeploy: false and .github/workflows/db-migrate.yml
    /// fires the deploy hook only after migrating. An instance running old code against the new
    /// enum is fine; it simply never sends the new label.
    /// </para>
    /// </summary>
    public partial class AddQuarterlyBillingCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,quarterly,weekly,yearly")
                .Annotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:Enum:billing_cycle_type", "monthly,one_time,weekly,yearly")
                .OldAnnotation("Npgsql:Enum:payment_source_type", "bank_account,card,other,wallet")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty - see the class summary. PostgreSQL has no DROP VALUE, so the
            // generated AlterDatabase call threw NotSupportedException before the SQL was ever run.
        }
    }
}

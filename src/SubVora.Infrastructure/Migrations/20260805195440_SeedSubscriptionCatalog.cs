using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubVora.Infrastructure.Migrations
{
    /// <summary>
    /// Seeds subscription_catalog with commonly-held providers, so a fresh database can match
    /// free-text input instead of falling through to the Manual tier and filling the catalog with
    /// the user's typos.
    ///
    /// Two things this migration deliberately does not do:
    /// - It leaves semantic_embedding NULL. Migrations must not make network calls, and
    ///   SubscriptionCatalogSearchRepository.FindNearestAsync filters out null-embedding rows, so
    ///   these rows stay invisible to matching until CatalogEmbeddingBackfillService has run.
    /// - It does not reference categories by hardcoded id. 20260711170536_SeedSystemCategories
    ///   inserts them without explicit ids (categories.id defaults to gen_random_uuid()), so there
    ///   is no stable id to reference - the join below resolves them by name instead.
    ///
    /// Logo source: Simple Icons via the jsDelivr CDN, pinned to major v13. The icon set is CC0;
    /// the brand marks themselves remain their owners' and are used nominatively.
    /// </summary>
    /// <inheritdoc />
    public partial class SeedSubscriptionCatalog : Migration
    {
        // Deterministic, hand-assigned ids so the seed is identical across every environment and
        // Down can delete exactly what Up inserted. The 5eedca70 prefix ("seed catalog") makes a
        // seeded row obvious at a glance next to a gen_random_uuid() user-generated one.
        private const string IdPrefix = "5eedca70-0000-4000-8000-0000000000";

        private static readonly (string Suffix, string ProviderName, string Category, string IconSlug)[] SeedProviders =
        [
            ("01", "Netflix", "Entertainment", "netflix"),
            ("02", "Amazon Prime Video", "Entertainment", "primevideo"),
            ("03", "HBO Max", "Entertainment", "hbo"),
            ("04", "Apple TV+", "Entertainment", "appletv"),
            ("05", "Paramount+", "Entertainment", "paramountplus"),
            ("06", "Crunchyroll", "Entertainment", "crunchyroll"),
            ("07", "YouTube Premium", "Entertainment", "youtube"),
            ("08", "Spotify", "Entertainment", "spotify"),
            ("09", "Apple Music", "Entertainment", "applemusic"),
            ("0a", "YouTube Music", "Entertainment", "youtubemusic"),
            ("0b", "Tidal", "Entertainment", "tidal"),
            ("0c", "Audible", "Entertainment", "audible"),
            ("0d", "PlayStation Plus", "Entertainment", "playstation"),
            ("0e", "Nintendo Switch Online", "Entertainment", "nintendoswitch"),
            ("0f", "Steam", "Entertainment", "steam"),

            ("10", "Notion", "Productivity", "notion"),
            ("11", "Slack", "Productivity", "slack"),
            ("12", "Zoom", "Productivity", "zoom"),
            ("13", "Trello", "Productivity", "trello"),
            ("14", "Asana", "Productivity", "asana"),
            ("15", "Todoist", "Productivity", "todoist"),
            ("16", "Evernote", "Productivity", "evernote"),
            ("17", "Grammarly", "Productivity", "grammarly"),
            ("18", "Canva", "Productivity", "canva"),
            ("19", "Figma", "Productivity", "figma"),
            ("1a", "Adobe Creative Cloud", "Productivity", "adobecreativecloud"),
            ("1b", "GitHub", "Productivity", "github"),
            ("1c", "JetBrains", "Productivity", "jetbrains"),
            ("1d", "LinkedIn Premium", "Productivity", "linkedin"),
            ("1e", "ChatGPT Plus", "Productivity", "openai"),

            ("1f", "Dropbox", "Utilities", "dropbox"),
            ("20", "Google One", "Utilities", "google"),
            ("21", "iCloud+", "Utilities", "icloud"),
            ("22", "Cloudflare", "Utilities", "cloudflare"),
            ("23", "NordVPN", "Utilities", "nordvpn"),
            ("24", "ExpressVPN", "Utilities", "expressvpn"),
            ("25", "Proton", "Utilities", "proton"),
            ("26", "1Password", "Utilities", "1password"),
            ("27", "LastPass", "Utilities", "lastpass"),

            ("28", "Strava", "Fitness", "strava"),
            ("29", "Peloton", "Fitness", "peloton"),
            ("2a", "Fitbit Premium", "Fitness", "fitbit"),
            ("2b", "Headspace", "Fitness", "headspace"),

            ("2c", "QuickBooks", "Finance", "quickbooks"),
            ("2d", "Robinhood Gold", "Finance", "robinhood"),
            ("2e", "Coinbase One", "Finance", "coinbase"),

            ("2f", "The New York Times", "Other", "newyorktimes"),
            ("30", "Medium", "Other", "medium"),
            ("31", "Substack", "Other", "substack"),
            ("32", "Duolingo Super", "Other", "duolingo"),
            ("33", "Coursera Plus", "Other", "coursera"),
            ("34", "Udemy", "Other", "udemy"),
            ("35", "Uber One", "Other", "uber"),
            ("36", "DoorDash DashPass", "Other", "doordash"),
        ];

        /// <summary>The ids this migration inserts, exposed so tests can assert on exactly the seeded set.</summary>
        public static IReadOnlyList<Guid> SeededIds { get; } =
            SeedProviders.Select(provider => Guid.Parse(IdPrefix + provider.Suffix)).ToList();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var rows = string.Join(",\n                        ", SeedProviders.Select(provider =>
                $"('{IdPrefix}{provider.Suffix}'::uuid, '{Escape(provider.ProviderName)}', '{provider.Category}', 'https://cdn.jsdelivr.net/npm/simple-icons@13/icons/{provider.IconSlug}.svg')"));

            // ON CONFLICT DO NOTHING because ix_subscription_catalog_provider_name is unique and the
            // table may already hold a user-generated Manual-tier row with one of these names, from
            // SubscriptionMatchService writing raw free-text input before this seed landed.
            migrationBuilder.Sql($"""
                INSERT INTO subscription_catalog (id, provider_name, category_id, logo_url)
                SELECT seed.id, seed.provider_name, category.id, seed.logo_url
                FROM (VALUES
                        {rows}
                    ) AS seed (id, provider_name, category_name, logo_url)
                    JOIN categories category
                        ON category.user_id IS NULL AND category.name = seed.category_name
                ON CONFLICT (provider_name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Delete precisely the seeded ids - by this point the table may also hold
            // user-generated Manual-tier rows that must survive a rollback.
            var ids = string.Join(", ", SeededIds.Select(id => $"'{id}'::uuid"));

            migrationBuilder.Sql($"DELETE FROM subscription_catalog WHERE id IN ({ids});");
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}

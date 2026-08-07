# SubVora 🚀
### Your Intelligent Cross-Platform Subscription Tracker & Optimizer
Built using single-codebase **.NET MAUI** (iOS & Android), backed by a high-performance **ASP.NET Core Web API**, and powered by **PostgreSQL + `pg_trgm`** for in-database fuzzy provider matching.

---

## 🌟 Executive Summary
**SubWise** is a cross-platform mobile application designed to eliminate the financial leak of forgotten subscriptions and un-cancelled trial periods. By using a unified .NET stack, developers write business logic once in C# to target both iOS and Android natively. 

The application automates mundane workflows like logo provisioning, smart categorization, and calculating predictive monthly financial burn rates.

---

## 📋 System Requirements & Technical Specifications

```
                     ┌───────────────────────────────────┐
                     │       Mobile Client Layer         │
                     │  ┌─────────────────────────────┐  │
                     │  │      .NET MAUI Mobile       │  │
                     │  │   (Single C# UI Codebase)   │  │
                     │  └──────────────┬──────────────┘  │
                     └─────────────────┼─────────────────┘
                                       │
                                       │ HTTPS (JWT Authenticated)
                                       ▼
                     ┌───────────────────────────────────┐
                     │          Backend Layer            │
                     │  ┌─────────────────────────────┐  │
                     │  │    ASP.NET Core Web API     │  │
                     │  └──────────────┬──────────────┘  │
                     └─────────────────┼─────────────────┘
                                       │
                    ┌──────────────────┴──────────────────┐
                    ▼                                     ▼
                            ┌───────────────────────────┐
                            │      Database Layer       │
                            │ ┌───────────────────────┐ │
                            │ │      PostgreSQL       │ │
                            │ │   (with pg_trgm)      │ │
                            │ └───────────────────────┘ │
                            └───────────────────────────┘

      No AI provider sits on any request path. Provider matching resolves inside
      the same query that reads the catalog - see "Provider Matching Flow" below.
```

### 🧠 Functional Capabilities (Non-Technical Requirements)
1. **Unified Multi-Platform Onboarding:** Users experience a consistent, smooth native app experience across both Android and iOS devices.
2. **Subscription Lifecycle Visualization:** Clear tracking of billing cycle cadences (Weekly, Monthly, Yearly, One-time) alongside proactive expiration warnings.
3. **Adaptive Alert Preferences:** Configurable user-defined alert thresholds (e.g., 7 days, 3 days, or 1 day prior to auto-renewal deduction).
4. **Financial Overhead Aggregation:** An active operational dashboard that computes multi-currency overall financial liabilities into a single localized "Burn Rate Summary" (Weekly / Monthly / Annual totals).
5. **Trial Vulnerability Management:** Special indicators highlighting free trials to guarantee timely structural opt-outs before commercial charge conversions.
6. **Self-Service Exit Execution:** Curated, context-sensitive instructions or deep links targeting the respective provider's cancellation terminal.

### 💻 System Engineering Architecture (Technical Requirements)
1. **Cross-Platform Mobile Component (.NET MAUI):**
   * **Local State Caching:** Embedded `SQLite` context database providing sub-second runtime latency and offline access capabilities.
   * **Renewal Reminders:** Local notifications scheduled on-device from the local mirror. The OS delivers them with the app closed, so no push service, vendor project or API key is involved.
2. **Microservice Backend API (ASP.NET Core):**
   * **Authentication Matrix:** Secure stateless JWT (JSON Web Tokens) handling verification flows via industry-grade encryption frameworks.
   * **Background Orchestration:** An automated `.NET BackgroundService` running asynchronously on a rolling midnight chronometer to compute upcoming expiration matrices and enqueue notifications.
3. **Storage Layout (PostgreSQL + `pg_trgm`):**
   * **Unified Relational Topology:** One store for financial rows and the provider catalog they link to, with no separate vector or search service to keep in sync.
   * **Fuzzy Provider Retrieval:** Trigram similarity (`word_similarity`, scored in both directions) tolerates typos and partial names without a network call or an API key.

---

## 🗄️ Database Schema Blueprint

Below is the production-ready PostgreSQL layout initialization script:

```sql
-- 1. Initialize and enable the trigram extension used by provider matching
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- 2. Define standard lookup Enum constructs for recurring models
CREATE TYPE billing_cycle_type AS ENUM ('Weekly', 'Monthly', 'Yearly', 'OneTime');

-- 3. Users Collection Context
CREATE TABLE users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(512) NOT NULL,
    preferred_currency VARCHAR(3) DEFAULT 'INR',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 4. Master Catalog for intelligent matching and icon lookup
CREATE TABLE subscription_catalog (
    catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_name VARCHAR(100) UNIQUE NOT NULL,
    standard_category VARCHAR(100) NOT NULL,
    logo_url VARCHAR(512),
    -- Matching reads provider_name directly via pg_trgm. The former semantic_embedding
    -- vector(1536) column was dropped in ReplaceCatalogEmbeddingWithTrigram.
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 5. Active User Subscription Profiles
CREATE TABLE user_subscriptions (
    subscription_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    catalog_id UUID REFERENCES subscription_catalog(catalog_id) ON DELETE SET NULL,
    custom_name VARCHAR(150) NOT NULL,
    cost_amount NUMERIC(12, 2) NOT NULL,
    currency VARCHAR(3) DEFAULT 'INR',
    cycle_cadence billing_cycle_type NOT NULL DEFAULT 'Monthly',
    purchase_date DATE NOT NULL,
    next_billing_date DATE NOT NULL,
    -- Null until the user first marks a charge paid. Nothing advances next_billing_date on a
    -- timer, so a past next_billing_date means the charge is genuinely outstanding.
    last_paid_date DATE,
    alert_days_advance INT DEFAULT 3,
    deduction_source VARCHAR(100), -- Example: 'Chase Card ending in 4021'
    is_free_trial BOOLEAN DEFAULT FALSE,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 6. Optimize Search with high-performance indices
CREATE INDEX IF NOT EXISTS idx_subs_user_id ON user_subscriptions(user_id);
CREATE INDEX IF NOT EXISTS idx_subs_next_billing ON user_subscriptions(next_billing_date) WHERE is_active = TRUE;
-- Serves the renewal scan's alert-due half. Its two predicates are OR'd, and Postgres falls back to
-- a sequential scan unless both sides are indexable.
CREATE INDEX IF NOT EXISTS idx_subs_alert_due ON user_subscriptions((next_billing_date - alert_days_advance)) WHERE is_active = TRUE;

-- Trigram similarity backs the free-text provider match.
CREATE EXTENSION IF NOT EXISTS pg_trgm;
-- No trigram index: the catalog is ~54 rows, where a sequential scan is microseconds. Add a
-- GiST gist_trgm_ops index (GIN cannot accelerate an ORDER BY on similarity) past a few thousand.
```

### Additional Tables (added post-v1, kept in sync with `src/SubVora.Infrastructure/Migrations/`)

The blueprint above predates several tables that shipped afterward. This section is the authoritative DDL for those - transcribed directly from the EF Core migrations, not re-derived, so column names/types match the real schema exactly (including the `id`/`snake_case` naming EF Core's Npgsql provider generates, which differs from the illustrative `user_id`-as-PK style above).

```sql
-- 7. User-defined and system-default subscription categories
CREATE TYPE payment_source_type AS ENUM ('bank_account', 'card', 'other', 'wallet');

CREATE TABLE categories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE CASCADE, -- NULL = system default category
    name VARCHAR(100) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
CREATE UNIQUE INDEX ix_categories_user_id_name ON categories(user_id, name);

-- 8. A user's own payment methods (cards/accounts/wallets), attachable to subscriptions
CREATE TABLE payment_sources (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    label VARCHAR(100) NOT NULL,
    source_type payment_source_type NOT NULL DEFAULT 'other',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
CREATE INDEX ix_payment_sources_user_id ON payment_sources(user_id);

-- 9. Cached FX conversion rates - burn-rate totals are converted at read time from this
-- cache, never by mutating a subscription's stored native currency/amount (see CLAUDE.md).
CREATE TABLE fx_rates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    base_currency VARCHAR(3) NOT NULL,
    target_currency VARCHAR(3) NOT NULL,
    rate NUMERIC(18, 8) NOT NULL,
    fetched_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
CREATE UNIQUE INDEX ix_fx_rates_base_currency_target_currency ON fx_rates(base_currency, target_currency);

-- 10. Opaque refresh tokens (JWT access tokens are stateless and not stored) - only the
-- SHA-256 hash is persisted, never the plaintext token.
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(512) NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    revoked_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens(user_id);

-- Renewal reminders are scheduled on-device by the mobile client from its local mirror, so there
-- are no notifications_log or device_tokens tables: nothing server-side decides when to notify,
-- and there is no push token to store. See DropPushNotificationTables.
```

---

## 🔎 Provider Matching Flow

Free-text entry is standardized in the database, with no external service on the path:

```
[User Entry: "netflx"]
       │
       ▼
[.NET Backend API] ──(single SQL query, no network hop)──► [PostgreSQL + pg_trgm]
       │                                                            │
       ◄────────────(best provider_name + similarity score)─────────┘
       │
       ▼
[3-tier decision: >= 0.70 AutoFill | >= 0.50 SuggestConfirm | else Manual]
```

### Core Matching Implementation (C# API code snippet)

```csharp
// word_similarity is directional, so both directions are scored and the better one wins:
//   "adobe" inside "Adobe Creative Cloud" -> word_similarity(input, name) = 1.0
//   "Netflix" inside "Netflix Premium"    -> word_similarity(name, input) = 1.0
var rows = await context.Database.SqlQuery<CatalogMatchRow>($"""
    SELECT id, provider_name, category_id, logo_url,
           greatest(word_similarity({input}, provider_name),
                    word_similarity(provider_name, {input})) AS score
    FROM subscription_catalog
    ORDER BY score DESC, provider_name
    LIMIT 1
    """).ToListAsync();
```

### Keeping the catalog current

`subscription-catalog.json` (embedded in `SubVora.Infrastructure`) is the living provider list;
`SubscriptionCatalogSyncService` inserts anything the table lacks on start, keyed on the unique
`provider_name`. Adding a brand is one JSON entry — no migration, no id to assign — and it is
matchable the moment it lands, because with trigram scoring the row *is* the index.

The `SeedSubscriptionCatalog` migration remains for databases that already applied it. The two
overlap without conflicting: every name it inserted is already present, so the sync skips it.

### Thresholds

Measured against the seeded 54-provider catalog rather than guessed. Correct matches scored 0.545
and up (`net flix` 0.545, `netflx` 0.714, `spotifyy` 0.875, exact and substring matches 1.000);
wrong answers topped out at 0.429 (`the mouse streaming service` → Strava). `Manual` sits in that
gap at 0.50, `AutoFill` at 0.70. `SubscriptionCatalogTrigramMatchTests` pins both bands.

What trigrams do not cover is rebrands and pure semantics — `G Suite` → Google Workspace,
`MS Office` → Microsoft 365. Those score below the floor and fall through to `Manual`.

---

## 🚀 Strategic Architecture Advantages
* **Single Core Repository:** Avoid writing discrete Swift/Kotlin layers. Features, visual design elements, and local configurations are completed entirely in C#.
* **Frictionless Onboarding:** Manual tracking drops drastically. Raw string entries resolve into standard categories, logos, and a catalog link without leaving the database.
* **Unified Financial Intelligence:** Database-centric architecture guarantees real-time notification synchronization, data security compliance, and platform independence.
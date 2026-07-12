# SubVora 🚀
### Your Intelligent Cross-Platform Subscription Tracker & Optimizer
Built using single-codebase **.NET MAUI** (iOS & Android), backed by a high-performance **ASP.NET Core Web API**, and powered by **PostgreSQL + `pgvector`** for semantic AI capabilities.

---

## 🌟 Executive Summary
**SubWise** is a cross-platform mobile application designed to eliminate the financial leak of forgotten subscriptions and un-cancelled trial periods. By using a unified .NET stack, developers write business logic once in C# to target both iOS and Android natively. 

The application integrates an AI-assisted orchestration engine to automate mundane workflows like logo provisioning, smart categorization, text/receipt parsing, and calculating predictive monthly financial burn rates.

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
      ┌───────────────────────────┐         ┌───────────────────────────┐
      │   AI Framework Layer      │         │      Database Layer       │
      │ ┌───────────────────────┐ │         │ ┌───────────────────────┐ │
      │ │   OpenAI Embeddings   │ │         │ │      PostgreSQL       │ │
      │ │   / LLM Service       │ │         │ │   (with pgvector)     │ │
      │ └───────────────────────┘ │         │ └───────────────────────┘ │
      └───────────────────────────┘         └───────────────────────────┘
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
   * **Push Notification Framework:** Abstracted handler communicating natively with Apple Push Notification service (APNs) for iOS and Firebase Cloud Messaging (FCM) for Android targets.
2. **Microservice Backend API (ASP.NET Core):**
   * **Authentication Matrix:** Secure stateless JWT (JSON Web Tokens) handling verification flows via industry-grade encryption frameworks.
   * **Background Orchestration:** An automated `.NET BackgroundService` running asynchronously on a rolling midnight chronometer to compute upcoming expiration matrices and enqueue notifications.
3. **Optimized AI Storage Layout (PostgreSQL + `pgvector`):**
   * **Unified Relational Topology:** Strongly structured indexing layouts combining conventional financial rows alongside high-dimensional floating-point vectors.
   * **Hybrid Semantic Retrieval Engines:** Utilizing vector similarity measurements (`<=>` cosine distance operator) combined with native keyword relational filters.

---

## 🗄️ Database Schema Blueprint

Below is the production-ready PostgreSQL layout initialization script showcasing the implementation of the `pgvector` framework for AI similarity capabilities:

```sql
-- 1. Initialize and enable the pgvector extension
CREATE EXTENSION IF NOT EXISTS pgvector;

-- 2. Define standard lookup Enum constructs for recurring models
CREATE TYPE billing_cycle_type AS ENUM ('Weekly', 'Monthly', 'Yearly', 'OneTime');

-- 3. Users Collection Context
CREATE TABLE users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(512) NOT NULL,
    preferred_currency VARCHAR(3) DEFAULT 'USD',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 4. Master Catalog for intelligent matching and icon lookup
CREATE TABLE subscription_catalog (
    catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_name VARCHAR(100) UNIQUE NOT NULL,
    standard_category VARCHAR(100) NOT NULL,
    logo_url VARCHAR(512),
    -- 1536 dimensions matches standard text-embedding-3-small OpenAI vectors
    semantic_embedding vector(1536), 
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 5. Active User Subscription Profiles
CREATE TABLE user_subscriptions (
    subscription_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    catalog_id UUID REFERENCES subscription_catalog(catalog_id) ON DELETE SET NULL,
    custom_name VARCHAR(150) NOT NULL,
    cost_amount NUMERIC(12, 2) NOT NULL,
    currency VARCHAR(3) DEFAULT 'USD',
    cycle_cadence billing_cycle_type NOT NULL DEFAULT 'Monthly',
    purchase_date DATE NOT NULL,
    next_billing_date DATE NOT NULL,
    alert_days_advance INT DEFAULT 3,
    deduction_source VARCHAR(100), -- Example: 'Chase Card ending in 4021'
    is_free_trial BOOLEAN DEFAULT FALSE,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 6. Optimize Search with high-performance indices
CREATE INDEX IF NOT EXISTS idx_subs_user_id ON user_subscriptions(user_id);
CREATE INDEX IF NOT EXISTS idx_subs_next_billing ON user_subscriptions(next_billing_date) WHERE is_active = TRUE;

-- Create an HNSW vector index for extremely fast AI semantic similarity lookups
CREATE INDEX ON subscription_catalog 
USING hnsw (semantic_embedding vector_cosine_ops);
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

-- 11. Idempotency guard for the renewal-alert background job - one row per (subscription,
-- alert_days_advance, day) prevents duplicate push notifications on a re-run.
CREATE TABLE notifications_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_subscription_id UUID NOT NULL REFERENCES user_subscriptions(id) ON DELETE CASCADE,
    sent_at TIMESTAMP WITH TIME ZONE DEFAULT now(),
    alert_days_advance INT NOT NULL
);
CREATE UNIQUE INDEX ix_notifications_log_user_subscription_id_alert_days_advance_s
    ON notifications_log(user_subscription_id, alert_days_advance, sent_at);

-- 12. Password-reset codes - the 6-digit code is never stored in plaintext, only its
-- SHA-256 hash, mirroring refresh_tokens. 15-minute expiry, max 5 verify attempts.
CREATE TABLE password_reset_codes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash CHAR(64) NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    attempt_count INT NOT NULL DEFAULT 0,
    used_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
CREATE INDEX ix_password_reset_codes_user_id ON password_reset_codes(user_id);

-- 13. Push-notification device tokens - one row per device, supports multiple
-- simultaneous devices per user. Pruned when FCM reports a token as unregistered.
CREATE TABLE device_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token TEXT NOT NULL,
    platform VARCHAR(10) NOT NULL, -- 'Android' | 'iOS'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now(),
    last_seen_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
CREATE UNIQUE INDEX ix_device_tokens_user_id_token ON device_tokens(user_id, token);
```

---

## 🤖 AI Capability Flow

The integration of `pgvector` solves the friction point of human entry. When a user creates a record, the backend runs an automated standardization pipeline:

```
[User Entry: "nflx mobile plan"] 
       │
       ▼
[.NET Backend API] ───(Sends to OpenAI)───► [Generates 1536-dim Embedding Vector]
                                                         │
                                                         ▼
[PostgreSQL Database] ◄───(Executes Semantic Query)──────┘
```

### Core Semantic Search Implementation Example (C# API Code snippet)

```csharp
public async Task<SubscriptionCatalogItem> MatchSubscriptionAsync(string userTypedInput, float[] openAiEmbeddingVector)
{
    // The pgvector extension enables us to use the '<=>' cosine distance syntax directly via EF Core or Raw SQL
    using var context = new AppDbContext();
    
    var matchedItem = await context.SubscriptionCatalog
        .FromSqlRaw("SELECT * FROM subscription_catalog ORDER BY semantic_embedding <=> {0}::vector LIMIT 1", openAiEmbeddingVector)
        .FirstOrDefaultAsync();

    return matchedItem; 
    // Automatically returns the structured entity containing "Netflix", "Entertainment" category, and the verified Logo URL!
}
```

---

## 🚀 Strategic Architecture Advantages
* **Single Core Repository:** Avoid writing discrete Swift/Kotlin layers. Features, visual design elements, and local configurations are completed entirely in C#.
* **Frictionless Onboarding via AI:** Manual tracking drops drastically. Receipt context scraping or raw string entries automatically resolve into standard categories.
* **Unified Financial Intelligence:** Database-centric architecture guarantees real-time notification synchronization, data security compliance, and platform independence.
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
   * **Screens Load Once, Not Per Tab Switch:** Shell raises `OnAppearing` on every tab selection, so each page's `OnAppearing` calls `EnsureLoadedCommand` — a first-visit fetch, then nothing. Freshness comes from invalidation, not repetition: a write publishes `SubscriptionsChangedMessage`, signing out publishes `SessionEndedMessage`, and pull-to-refresh (`LoadCommand`) always fetches. A load that ended with nothing on screen is not "loaded", so an error screen still retries on the next visit.
2. **Microservice Backend API (ASP.NET Core):**
   * **Authentication Matrix:** Secure stateless JWT (JSON Web Tokens) handling verification flows via industry-grade encryption frameworks.
   * **Background Work:** Only what a client cannot do for itself — refreshing cached FX rates, syncing the provider catalog, and dispatching queued email. Nothing advances a billing date on a timer: that moves when the user marks a charge paid, so a date left in the past means the charge is genuinely outstanding.
3. **Storage Layout (PostgreSQL + `pg_trgm`):**
   * **Unified Relational Topology:** One store for financial rows and the provider catalog they link to, with no separate vector or search service to keep in sync.
   * **Fuzzy Provider Retrieval:** Trigram similarity (`word_similarity`, scored in both directions) tolerates typos and partial names without a network call or an API key.

---

## 🗄️ Database Schema

**The schema itself lives in the code**, not here: the EF Core entity configurations under
`src/SubVora.Infrastructure/Data/Configurations/` define it, and the migrations under
`src/SubVora.Infrastructure/Migrations/` are the record of how it got that way. This section
describes what each table is *for* and the decisions behind it — column names, types and defaults
are deliberately not repeated, because a second copy only ever drifts from the first.

To see the live schema: `docker compose exec db psql -U subvora -d subvora_dev -c "\d user_subscriptions"`.

| Table | Holds | Worth knowing |
|---|---|---|
| `users` | Account and home currency | Home currency is a display preference; it never rewrites what a subscription is billed in |
| `subscription_catalog` | Canonical provider list, category, logo URL | Matched on `provider_name` by `pg_trgm`. Seeded from `subscription-catalog.json` on start; existing rows are never overwritten |
| `user_subscriptions` | One row per tracked subscription | Stores its own currency and amount, unconverted. `next_billing_date` moves only when the user marks a charge paid, so a past date means the charge is outstanding — see `last_paid_date` |
| `categories` | System and per-user categories | `user_id IS NULL` marks a system default. Deleting a user cascades to theirs |
| `payment_sources` | A user's cards, accounts and wallets | Optional on a subscription; the burn-rate response groups monthly spend by it so the dashboard can name the account carrying the most |
| `fx_rates` | Cached conversion rates | Burn-rate totals are converted at read time from this cache, never by mutating a stored amount |
| `refresh_tokens` | Opaque refresh tokens | Only the SHA-256 hash is persisted, never the plaintext. Access tokens are stateless and not stored at all |

There is no `notifications_log` or `device_tokens` table. Renewal reminders are scheduled on-device
from the client's local mirror, so nothing server-side decides when to notify and there is no push
token to keep (see the `DropPushNotificationTables` migration).

### Indexes and extensions

- `pg_trgm` backs free-text provider matching.
- `user_subscriptions` is indexed on `user_id`, and on `next_billing_date` filtered to active rows.
- **No trigram index.** The catalog is ~90 rows, where a sequential scan is microseconds. Past a few
  thousand, add a GiST `gist_trgm_ops` index — GIN cannot accelerate an `ORDER BY` on similarity.

---

## 🔎 Provider Matching Flow

Free-text entry is standardized in the database, with no external service on the path:

```
[User Entry: "netflx"]
       │
       ▼
[.NET Backend API] ──(single SQL query, no network hop)──► [PostgreSQL + pg_trgm]
       │                                                            │
       ◄──────(top provider_name rows + similarity scores)──────────┘
       │
       ▼
[every row >= 0.50, best first, for the user to pick]
[tier of the best: >= 0.70 AutoFill | >= 0.50 SuggestConfirm | else Manual]
```

### How the score is computed

The whole match is one parameterised query against `subscription_catalog`, ordered by score, taking
the top few rows. It lives in `SubscriptionCatalogSearchRepository`.

**Both directions are scored and the higher one wins.** `word_similarity` is directional, which
matters more than it sounds:

- `"adobe"` inside `"Adobe Creative Cloud"` scores 1.0 one way round
- `"Netflix"` against `"Netflix Premium"` scores 1.0 the *other* way round

Score one direction only and half of the realistic inputs stop matching. The thresholds are measured
rather than guessed, and `SubscriptionCatalogTrigramMatchTests` pins them.

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

**The tier is wording, not authority.** Every candidate above the floor is returned and the client
shows the list; nothing is written into the form until the user taps one. `AutoFill` only says the
top row is a confident guess — and a confident guess about "youtube" is still three real products,
which no score can choose between. The floor still applies per row, so a good match never drags
sub-threshold noise onto the screen behind it.

What trigrams do not cover is rebrands and pure semantics — `G Suite` → Google Workspace,
`MS Office` → Microsoft 365. Those score below the floor and fall through to `Manual`.

---

## 🚀 Strategic Architecture Advantages
* **Single Core Repository:** Avoid writing discrete Swift/Kotlin layers. Features, visual design elements, and local configurations are completed entirely in C#.
* **Frictionless Onboarding:** Manual tracking drops drastically. Raw string entries resolve into standard categories, logos, and a catalog link without leaving the database.
* **Unified Financial Intelligence:** Database-centric architecture guarantees real-time notification synchronization, data security compliance, and platform independence.
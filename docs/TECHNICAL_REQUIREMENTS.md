# SubVora — Technical Requirements

## 1. Overview

SubVora is a cross-platform mobile subscription tracker with cancellation reminders and AI-assisted categorization. This document specifies the technical (engineering) requirements. For feature/UX requirements see [NON_TECHNICAL_REQUIREMENTS.md](./NON_TECHNICAL_REQUIREMENTS.md).

## 2. Architecture

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
      │ │  (no external AI      │ │         │ │      PostgreSQL       │ │
      │ │   provider - see §8)  │ │         │ │   (with pg_trgm)      │ │
      │ └───────────────────────┘ │         │ └───────────────────────┘ │
      └───────────────────────────┘         └───────────────────────────┘
```

## 3. Tech Stack

| Layer | Choice | Notes |
|---|---|---|
| Mobile client | .NET MAUI (C#) | Single codebase targeting Android + iOS |
| Local cache | SQLite (via `Microsoft.Data.Sqlite` / EF Core) | Offline-first, synced with backend |
| Backend API | ASP.NET Core Web API (.NET 8 LTS) | REST, JWT auth |
| ORM | Entity Framework Core + `Npgsql.EntityFrameworkCore.PostgreSQL` | |
| Database | PostgreSQL 16+ with `pg_trgm` extension | Relational data + fuzzy provider matching in one store |
| Provider matching | PostgreSQL `pg_trgm` trigram similarity | No AI provider, no API key, no network hop; see §8 |
| Renewal reminders | Local notifications scheduled on-device by the MAUI client | No push service, no vendor project, no API key; see §6 |
| Background jobs | `.NET BackgroundService` / Hosted Service (or Hangfire/Quartz.NET if scale requires) | Nightly scan for upcoming renewals |
| Currency conversion | External FX rate API (e.g. exchangerate.host, Open Exchange Rates) cached in DB | Refresh rates on a schedule, not per-request |
| Auth | JWT bearer tokens, refresh token rotation | ASP.NET Identity or custom user store |
| Hosting | Containerized (Docker) API + managed Postgres | Cloud-agnostic; Azure App Service / AWS / Railway all viable |

## 4. Backend API Requirements

- Stateless REST API, versioned (`/api/v1/...`).
- JWT bearer authentication on all endpoints except `/auth/*`.
- Standard CRUD endpoints for subscriptions, categories, currencies, alert preferences.
- `POST /api/v1/subscriptions/resolve` — accepts free-text subscription name, returns matched catalog entry (logo, category) via trigram similarity search.
- `GET /api/v1/dashboard/burn-rate` — returns aggregated weekly/monthly/yearly totals in user's home currency.
- Rate limiting on `/resolve` to bound CPU on an endpoint the client calls on every debounced keystroke pause.
- Input validation via FluentValidation or DataAnnotations; reject malformed currency codes, negative amounts, invalid dates.
- Centralized error handling middleware returning consistent problem-details responses.

## 5. Database Schema (PostgreSQL + pg_trgm)

See full DDL in [Design.md](./Design.md#-database-schema-blueprint). Key tables:

- `categories` — system categories seeded with `user_id IS NULL` (`Entertainment`, `Productivity`, `Fitness`, `Utilities`, `Finance`, `Food`, `Travel`, `Other`), plus per-user ones.
- `users` — account, `preferred_currency`.
- `subscription_catalog` — canonical provider list with `logo_url`, `standard_category`. Matched on `provider_name` via `pg_trgm`.
- `user_subscriptions` — per-user subscription record: `cost_amount`, `currency`, `cycle_cadence` (Weekly/Monthly/Yearly/OneTime), `purchase_date`, `next_billing_date`, `alert_days_advance`, `deduction_source`, `is_free_trial`, `is_active`.
- `fx_rates` (to be added) — `base_currency`, `target_currency`, `rate`, `fetched_at` — cached exchange rates for burn-rate conversion.

Indexes: `next_billing_date` (partial, `is_active = TRUE`), `user_id`. No trigram index — the catalog is ~54 rows, where a sequential scan is microseconds; add a GiST `gist_trgm_ops` index past a few thousand.

## 6. Feature-Level Technical Notes

1. **Logo Feature** — logo resolved server-side at catalog-match time (`logo_url` on `subscription_catalog`); client renders from CDN URL with local placeholder/fallback icon.
2. **Category** — derived from `subscription_catalog.standard_category` when matched; user can override per-subscription.
3. **Billing type** — `billing_cycle_type` enum (`Weekly`, `Monthly`, `Yearly`, `OneTime`) drives both burn-rate math and next-billing-date calculation.
4. **Purchase / expiry dates** — `purchase_date` + `cycle_cadence` used to compute `next_billing_date`; recalculated on each renewal via background job.
5. **Alert preferences** — `alert_days_advance` (int, user-configurable per subscription or global default). The mobile client schedules one local notification per active subscription at `next_billing_date - alert_days_advance`, re-derived whenever the subscription list loads. The OS delivers it with the app closed, so no server-side send path exists. iOS holds at most 64 pending notifications, so the nearest dates win. The server's only job here is rolling passed billing dates forward (`BillingDateAdvanceBackgroundService`), which the client cannot do.
6. **Deduction source** — free-text field (`deduction_source`) initially; optionally normalized into a `payment_sources` lookup table later.
7. **Burn Rate Calculator** — server-side aggregation endpoint normalizes every active subscription's cost into a common daily rate `(cost / cycle_days)`, sums, then projects `daily_rate * 7`, `* 30`, `* 365`. All amounts converted to home currency before summing (see §7).
8. **Multi-Currency Uniformity** — every subscription stores its own `currency`; conversion to `preferred_currency` happens at query/display time using cached FX rates, never mutates stored amounts.

## 7. Currency Conversion Requirements

- FX rates fetched on a scheduled job (e.g. daily) from a third-party provider and cached in `fx_rates`.
- Burn-rate and dashboard totals always computed server-side in the user's `preferred_currency` to keep client logic thin and consistent across devices.
- Store original `currency` + `cost_amount` immutably; conversion is a read-time projection, not a write-time mutation — avoids lossy re-conversion and lets rate history stay auditable.

## 8. Provider Matching Requirements

- Matching runs entirely inside PostgreSQL. There is no AI provider on any request path, no API key to configure, and no per-request third-party cost.
- Match flow: user free-text → one `pg_trgm` query against `subscription_catalog.provider_name` → best match with a similarity score; below-threshold matches fall back to manual entry.
- Scoring uses `greatest(word_similarity(input, name), word_similarity(name, input))`. Both directions are needed: the first catches input that is a fragment of the provider name (`adobe` → Adobe Creative Cloud), the second catches input that contains it (`Netflix Premium` → Netflix). Plain `similarity()` scores the former at 0.300, down among the wrong answers.
- Three-tier confidence, measured against the seeded 54-provider catalog rather than guessed: `AutoFill` ≥ 0.70, `SuggestConfirm` ≥ 0.50, otherwise `Manual`. Correct matches scored 0.545 and up; wrong answers topped out at 0.429. `SubscriptionCatalogTrigramMatchTests` pins both bands so a regression is visible.
- The provider list lives in `src/SubVora.Infrastructure/Catalog/subscription-catalog.json`, embedded in the assembly, and `SubscriptionCatalogSyncService` inserts anything missing on start (`ON CONFLICT (provider_name) DO NOTHING`). Adding a brand is one JSON entry — no migration, no hand-assigned id, and it is matchable immediately because the row is the index. Existing rows are never overwritten.
- Nothing the user types is written to `subscription_catalog` — it is global and unowned, so a `Manual` result means no catalog link, not a new row.
- **Known gap:** trigrams do not resolve rebrands or purely semantic input (`G Suite` → Google Workspace, `MS Office` → Microsoft 365, `the mouse streaming service` → Disney+). These score below the floor and land in `Manual`. Closing that gap needs an LLM fallback on the sub-threshold path; it is deliberately not in this design.

## 9. Non-Functional Requirements

- **Security:** JWT auth, password hashing (bcrypt/Argon2), HTTPS-only, secrets in a vault/managed config (not source control).
- **Performance:** Dashboard burn-rate query < 300ms p95 for typical user (<100 subscriptions); mobile UI must remain responsive offline via local SQLite cache.
- **Offline support:** Mobile app reads/writes to local SQLite when offline; syncs to backend when connectivity resumes.
- **Reliability:** Background renewal-scan job must be idempotent (safe to re-run without duplicate notifications) — enforced via `notifications_log`.
- **Scalability:** Stateless API instances behind a load balancer; Postgres connection pooling (Npgsql pooling or PgBouncer) as user base grows.
- **Observability:** Structured logging (Serilog), health-check endpoint, basic metrics on job success/failure.
- **Testability:** Unit tests for burn-rate math and currency conversion; integration tests for auth and CRUD endpoints.

## 10. Out of Scope (v1)

- Automated bank/email scraping for subscription discovery.
- In-app subscription cancellation (deep links to provider only, no cancellation-on-behalf-of-user).
- Web client (mobile-only for v1).

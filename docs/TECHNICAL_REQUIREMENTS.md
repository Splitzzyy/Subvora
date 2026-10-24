# SubVora — Technical Requirements

## 1. Overview

SubVora is a cross-platform mobile subscription tracker with cancellation reminders and catalog-based categorization (trigram matching in Postgres; there is no AI provider, see §8). This document specifies the technical (engineering) requirements. For feature/UX requirements see [NON_TECHNICAL_REQUIREMENTS.md](./NON_TECHNICAL_REQUIREMENTS.md).

## 2. Architecture

```
                     ┌───────────────────────────────────┐
                     │       Mobile Client Layer         │
                     │  ┌─────────────────────────────┐  │
                     │  │   React Native (Expo) App   │  │
                     │  │  (TypeScript, Android/ dir) │  │
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
                                       ▼
                     ┌───────────────────────────────────┐
                     │          Database Layer           │
                     │  ┌─────────────────────────────┐  │
                     │  │         PostgreSQL          │  │
                     │  │ (pg_trgm: provider matching │  │
                     │  │  in-database, see §8)       │  │
                     │  └─────────────────────────────┘  │
                     └───────────────────────────────────┘
```

There is no separate AI or matching service: provider matching is one SQL query in the database.

## 3. Tech Stack

| Layer | Choice | Notes |
|---|---|---|
| Mobile client | React Native (Expo, TypeScript) in `Android/` | Android-only; Material 3 Expressive UI. The earlier .NET MAUI client (`src/SubVora.Mobile`) is retired - kept for reference, not built |
| Local cache | `expo-sqlite` kv-store in the React Native client | Read-only mirror of the last successful reads; no offline writes (decided in #144) |
| Backend API | ASP.NET Core Web API (.NET 10, `net10.0`) | REST, JWT auth |
| ORM | Entity Framework Core + `Npgsql.EntityFrameworkCore.PostgreSQL` | |
| Database | PostgreSQL 16+ with `pg_trgm` extension | Relational data + fuzzy provider matching in one store |
| Provider matching | PostgreSQL `pg_trgm` trigram similarity | No AI provider, no API key, no network hop; see §8 |
| Renewal reminders | Local notifications scheduled on-device by the mobile client (`expo-notifications`) | No push service, no vendor project, no API key; see §6 |
| Background jobs | `.NET BackgroundService` / Hosted Service | FX rate refresh, provider catalog sync, expired-credential cleanup and email dispatch. No renewal scan: reminders are scheduled on-device (§6.5) |
| Currency conversion | External FX rate API (open.er-api.com; exchangerate.host now needs a key) cached in DB | Refresh rates on a schedule, not per-request |
| Auth | JWT bearer tokens, refresh token rotation | Custom user store (`AuthService` + `users`, BCrypt); no ASP.NET Identity |
| Hosting | Containerized (Docker) API + managed Postgres | Render (API) + Neon (Postgres) + Brevo (SMTP) today, all free tier (`docs/DEPLOYMENT.md`); any container host works |

## 4. Backend API Requirements

- REST API, versioned (`/api/v1/...`). Requests carry no server-side session (JWT bearer), but the process holds some in-memory state - see Scalability in §9.
- JWT bearer authentication on all endpoints except `/auth/*`.
- Standard CRUD endpoints for subscriptions, categories, currencies, alert preferences.
- `POST /api/v1/subscriptions/resolve` — accepts free-text subscription name, returns every catalog entry above the similarity floor (logo, category), best first, for the client to offer as a pick list.
- `GET /api/v1/dashboard/burn-rate` — returns aggregated weekly/monthly/yearly totals in user's home currency, plus monthly spend broken down by category and by the payment source it is charged to.
- Rate limiting on `/resolve` to bound CPU on an endpoint the client calls on every debounced keystroke pause, and per IP on the credential endpoints (`auth` 10/min, `refresh` 60/min); on top of those, a global limit of 120 requests/min per user (600 per IP when anonymous; `/health/live` exempt) and a 64 KB request-body cap on every route.
- Per-account login lockout (`LoginThrottle`, in-process): each address gets 10 attempts, reserved before the password is checked so a parallel burst cannot exceed it; after that it is refused until 15 minutes pass with no counted attempt, with the same 401 whether or not it has an account. A successful login clears the count. A successful password reset clears it too and returns a token pair, so recovery never runs through a login a stranger can lock. This bounds guessing against one account, not credential stuffing across many; for that:
- New-password rule at register, change-password and reset-password (`PasswordRules.NewPassword`): at least 8 characters with an uppercase letter, a lowercase letter, a digit and a punctuation or symbol character (whitespace does not count). That is the whole rule - there is no breached- or common-password check, by decision, so nothing derived from a password leaves the server. A refused password is a 400 naming the field. Login only requires a non-empty password, so accounts created under an older rule still sign in.
- A global failed-login signal: `LoginThrottle.RecordFailure` counts failures across all accounts per clock minute and logs one warning when 30 is reached, so a stuffing run shows in the logs. No address or user id in that line.
- Refresh tokens form families (`family_id`, `family_started_at`): a login, reset or password change starts one and every rotation inherits it. Reuse detection revokes only the presented token's family, not every device. A rotated token replayed after it expired still counts as reuse, which is what the 7-day post-expiry retention is for. No family refreshes past 90 days from its sign-in (`AuthService.AbsoluteSessionLifetime`), so even an undetected stolen chain ends.
- Per-account caps on create: 500 subscriptions, 100 own categories, 50 payment sources (`UserQuotas`). They keep one account from filling the shared database and bound every list, which is why no list endpoint paginates.
- Input validation via FluentValidation or DataAnnotations; reject malformed currency codes, negative amounts, invalid dates.
- Centralized error handling middleware returning consistent problem-details responses.

## 5. Database Schema (PostgreSQL + pg_trgm)

The schema itself is defined by the EF Core entity configurations and migrations under `src/SubVora.Infrastructure/`; [Design.md](./Design.md#-database-schema) describes what each table is for. Key tables:

- `categories` — system categories seeded with `user_id IS NULL` (`Entertainment`, `Productivity`, `Fitness`, `Utilities`, `Finance`, `Food`, `Travel`, `Other`), plus per-user ones.
- `users` — account, `preferred_currency`.
- `subscription_catalog` — canonical provider list with `logo_url` and `category_id` (a system category). Matched on `provider_name` via `pg_trgm`.
- `user_subscriptions` — per-user subscription record: `cost_amount`, `currency`, `cycle_cadence` (Weekly/Monthly/Quarterly/Yearly/OneTime), `purchase_date`, `next_billing_date`, `last_paid_date` (null until first marked paid), `alert_days_advance`, `payment_source_id` (nullable, see `payment_sources`), `is_free_trial`, `is_active`.
- `payment_sources` — a user's cards, accounts and wallets (`label`, `source_type`), referenced by `user_subscriptions.payment_source_id`. Replaced the original free-text `deduction_source` column.
- `fx_rates` — `base_currency`, `target_currency`, `rate`, `fetched_at` — cached exchange rates for burn-rate conversion.
- `refresh_tokens`, `password_reset_codes` — auth state; only hashes are stored, and `ExpiredCredentialCleanupBackgroundService` deletes rows past their retention window and every refresh token of a sign-in with no live token left.

Indexes: `next_billing_date` (partial, `is_active = TRUE`), `user_id`. No trigram index — the catalog is ~54 rows, where a sequential scan is microseconds; add a GiST `gist_trgm_ops` index past a few thousand.

## 6. Feature-Level Technical Notes

1. **Logo Feature** — logo resolved server-side at catalog-match time (`logo_url` on `subscription_catalog`); client renders from CDN URL with local placeholder/fallback icon.
2. **Category** — derived from `subscription_catalog.category_id` when matched; user can override per-subscription.
3. **Billing type** — `billing_cycle_type` enum (`Weekly`, `Monthly`, `Quarterly`, `Yearly`, `OneTime`) drives both burn-rate math and next-billing-date calculation. It is a native Postgres enum, so adding a member needs a migration (`ALTER TYPE ... ADD VALUE`) applied before the code that can emit it. Burn-rate math counts charges per year (Weekly 52, Monthly 12, Quarterly 4, Yearly 1) rather than dividing by days (a daily rate was removed: it priced a year at 12.17 months, see item 7); date advancement uses calendar months (`AddMonths(3)` for quarterly) so a charge keeps its day of the month.
4. **Purchase / expiry dates** — `purchase_date` + `cycle_cadence` give the client its default `next_billing_date` (one cycle after purchase). Afterwards the date moves only when the user marks the charge paid, which records `last_paid_date` and steps on one cycle from the date settled. No background job advances it: a date left in the past is what tells the client the charge is overdue, and a nightly roll-forward would erase that signal.
5. **Alert preferences** — `alert_days_advance` (int, user-configurable per subscription or global default). The mobile client schedules one local notification per active subscription at `next_billing_date - alert_days_advance`, re-derived whenever the subscription list loads. The OS delivers it with the app closed, so no server-side send path exists. iOS holds at most 64 pending notifications, so the nearest dates win. The server has no part in reminders, and nothing advances a billing date on a timer (a nightly `BillingDateAdvanceBackgroundService` was removed: it erased the overdue signal, see item 4).
6. **Payment source** — the `payment_sources` table, one row per card, account or wallet the user names, linked from `user_subscriptions.payment_source_id` (optional). It replaced the original free-text `deduction_source` column; the burn-rate response groups monthly spend by it.
7. **Burn Rate Calculator** — server-side aggregation endpoint computes each active subscription's *annual* cost as `cost * charges_per_year` (Weekly 52, Monthly 12, Quarterly 4, Yearly 1), sums those, then derives `Monthly = annual / 12` and `Weekly = annual / 52`. All amounts converted to home currency before summing (see §7). Cycles are counted, not measured in days: normalising to a daily rate over a 30-day month and a 365-day year priced a year at 12.17 months, so 1000/month reported 12166.67/year.
8. **Multi-Currency Uniformity** — every subscription stores its own `currency`; conversion to `preferred_currency` happens at query/display time using cached FX rates, never mutates stored amounts.

## 7. Currency Conversion Requirements

- FX rates fetched on a scheduled job (e.g. daily) from a third-party provider and cached in `fx_rates`.
- Burn-rate and dashboard totals always computed server-side in the user's `preferred_currency` to keep client logic thin and consistent across devices.
- Store original `currency` + `cost_amount` immutably; conversion is a read-time projection, not a write-time mutation — avoids lossy re-conversion and lets rate history stay auditable.

## 8. Provider Matching Requirements

- Matching runs entirely inside PostgreSQL. There is no AI provider on any request path, no API key to configure, and no per-request third-party cost.
- Match flow: user free-text → one `pg_trgm` query against `subscription_catalog.provider_name` → the top rows with their similarity scores; anything below the floor is dropped, and an empty list means manual entry.
- Scoring uses `greatest(word_similarity(input, name), word_similarity(name, input))`. Both directions are needed: the first catches input that is a fragment of the provider name (`adobe` → Adobe Creative Cloud), the second catches input that contains it (`Netflix Premium` → Netflix). Plain `similarity()` scores the former at 0.300, down among the wrong answers.
- Three-tier confidence, measured against the seeded 54-provider catalog rather than guessed: `AutoFill` ≥ 0.70, `SuggestConfirm` ≥ 0.50, otherwise `Manual`. Correct matches scored 0.545 and up; wrong answers topped out at 0.429. `SubscriptionCatalogTrigramMatchTests` pins both bands so a regression is visible.
- The tier describes the best candidate; it does not decide anything. Every row above the floor is returned (capped at `SubscriptionMatchService.MaxSuggestions`) and the client shows them as a list — `AutoFill` never writes into the form on its own, because one input can match several real products (`youtube` → YouTube Music, YouTube Premium) and only the user knows which they pay for.
- The provider list lives in `src/SubVora.Infrastructure/Catalog/subscription-catalog.json`, embedded in the assembly, and `SubscriptionCatalogSyncService` inserts anything missing on start (`ON CONFLICT (provider_name) DO NOTHING`). Adding a brand is one JSON entry — no migration, no hand-assigned id, and it is matchable immediately because the row is the index. Existing rows are never overwritten.
- Nothing the user types is written to `subscription_catalog` — it is global and unowned, so a `Manual` result means no catalog link, not a new row.
- **Known gap:** trigrams do not resolve rebrands or purely semantic input (`G Suite` → Google Workspace, `MS Office` → Microsoft 365, `the mouse streaming service` → Disney+). These score below the floor and land in `Manual`. Closing that gap needs an LLM fallback on the sub-threshold path; it is deliberately not in this design.

## 9. Non-Functional Requirements

- **Security:** JWT auth, password hashing (bcrypt/Argon2), HTTPS-only, secrets in a vault/managed config (not source control).
- **Performance:** Dashboard burn-rate query < 300ms p95 for typical user (<100 subscriptions); mobile UI must remain readable offline via the local read cache.
- **Offline support:** Read-only. The mobile app shows its cached copy of the last successful reads when offline; writes need a connection and fail visibly without one. There is no outbox, replay or conflict resolution (decided in #144; revisit that decision before adding any).
- **Reliability:** Background jobs (FX refresh, catalog sync, credential cleanup) must be safe to re-run. There is no server-side renewal scan and no `notifications_log` (dropped by `DropPushNotificationTables`); reminders are scheduled on-device. Writes a client may retry: subscription and payment-source creates accept a client-supplied id, so a retry returns the original row (older clients that omit the id still duplicate on retry); category creates are deduplicated by name (a retry gets 409); mark-paid names the billing date it settles.
- **Scalability:** Single API instance by design (Render free tier). These hold in-process state and must move to Postgres (or a shared store) before scaling out, or each silently becomes per-instance: the login lockout (`LoginThrottle`), the outbound email queue (`QueuedEmailSender`, which also loses queued mail on restart), the FX failed-fetch cooldown (`FxRateService`) and the rate-limiter partitions. Postgres connection pooling (Npgsql pooling or PgBouncer) as the user base grows.
- **Observability:** Structured logging (Serilog), health-check endpoint, basic metrics on job success/failure.
- **Testability:** Unit tests for burn-rate math and currency conversion; integration tests for auth and CRUD endpoints.

## 10. Out of Scope (v1)

- Automated bank/email scraping for subscription discovery.
- In-app subscription cancellation (deep links to provider only, no cancellation-on-behalf-of-user).
- Web client (mobile-only for v1).

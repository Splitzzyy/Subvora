# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

SubVora — cross-platform subscription tracker with cancellation reminders, burn-rate dashboard, and multi-currency support. Full requirements live in `docs/`:

- `docs/TECHNICAL_REQUIREMENTS.md` — architecture, stack, API/DB requirements
- `docs/NON_TECHNICAL_REQUIREMENTS.md` — feature/product requirements
- `docs/Design.md` — architecture diagram, what each table is for, matching flow
- `docs/ADDING_A_PROVIDER.md` — how to add, rename, or remove a subscription provider
- `docs/DEPLOYMENT.md` — hosting the API (Render + Neon + Brevo, all free tier) and shipping the signed Android APK
- `Android/README.md` / `Android/DESIGN.md` — running the React Native app on a phone against a local API, and its Material 3 Expressive design system

Read the relevant doc before implementing a feature — don't guess at requirements that are already written down.

`technical_requirements.md` at the repo root is a **local, gitignored** working document (alongside `prd.md`/`issues.md`). Several code comments cite it by name; if you don't have a copy, those references are dead ends rather than missing files. Don't commit it without asking — it is ignored deliberately.

## Current State

Backend and mobile client both exist and are under active development. Actual layout:

```
/src
  /SubVora.Domain          # Entities + enums, no dependencies
  /SubVora.Application     # DTOs, validators, interfaces, pure logic — no EF Core
  /SubVora.Infrastructure  # EF Core, repositories, hosted services, external clients, migrations
  /SubVora.Api             # ASP.NET Core Web API (controllers, Program.cs)
  /SubVora.Mobile          # RETIRED .NET MAUI client - code kept for reference; not built, not in SubVora.slnx, not in CI
/Android                   # The mobile client: React Native (Expo, TypeScript), Material 3 Expressive
/tests
  /SubVora.Api.Tests            # integration, Testcontainers + WebApplicationFactory
  /SubVora.Application.Tests    # pure unit, no database
  /SubVora.Infrastructure.Tests # integration, Testcontainers
  /SubVora.Mobile.Tests         # RETIRED with SubVora.Mobile - kept, not built
/docs
```

There is deliberately **no `SubVora.Shared`** — mobile DTOs mirror the API's JSON contract by convention, so a contract change means editing `Android/src/api/types.ts` too. The client's pure rules (billing dates, grouping, reminders, money formatting) live in `Android/src/lib/logic.ts`, tested with `npm test`.

Don't edit, build or test `src/SubVora.Mobile` / `tests/SubVora.Mobile.Tests`: the MAUI client is retired. They stay in the tree as reference only.

### Build, test, run

```
dotnet build src/SubVora.Api/SubVora.Api.csproj -c Release
dotnet run --project src/SubVora.Api

dotnet test tests/SubVora.Api.Tests/SubVora.Api.Tests.csproj -c Release
dotnet test tests/SubVora.Application.Tests/SubVora.Application.Tests.csproj -c Release
dotnet test tests/SubVora.Infrastructure.Tests/SubVora.Infrastructure.Tests.csproj -c Release

cd Android && npm ci && npm run typecheck && npm run lint && npm test   # mobile client
```

`SubVora.slnx` holds only the backend projects, so `dotnet build SubVora.slnx` needs no Android SDK or MAUI workload. `.github/workflows/ci.yml` runs the three backend test projects plus the `android` job (typecheck, lint, tests, Hermes bundle); `release-android.yml` builds the signed APK from `Android/` on a `v*` tag.

`SubVora.Api.Tests` and `SubVora.Infrastructure.Tests` spin up a real `pgvector/pgvector:pg16` container per test class via Testcontainers (stock Postgres 16 plus an extension the app no longer uses — kept so existing dev volumes keep working) — Docker must be running. `SubVora.Application.Tests` needs nothing.

### Adding a brand to the subscription catalog

Add one entry to `src/SubVora.Infrastructure/Catalog/subscription-catalog.json` — no migration, no code, no id:

```json
{ "providerName": "Disney+", "category": "Entertainment", "iconSlug": "disneyplus" }
```

`SubscriptionCatalogSyncService` inserts anything missing on the next start, keyed on the unique `provider_name`. `category` must name a system category (`Entertainment`, `Productivity`, `Fitness`, `Utilities`, `Finance`, `Food`, `Travel`, `Other`) — a test fails otherwise. `iconSlug` is a [Simple Icons](https://simpleicons.org) slug or `null`; v13 dropped several brands for trademark reasons, and a null slug just means no logo, which matching does not need. Existing rows are never overwritten.

The `SeedSubscriptionCatalog` migration is frozen history for databases that already ran it — don't add brands there. Full walkthrough, plus the rename/remove cases the sync deliberately does not handle: `docs/ADDING_A_PROVIDER.md`.

Migrations: `dotnet ef migrations add <Name> --project src/SubVora.Infrastructure --startup-project src/SubVora.Infrastructure` (the Infrastructure project is its own startup project via `AppDbContextFactory`; `SubVora.Api` does not reference `Microsoft.EntityFrameworkCore.Design`).

## Stack (see docs/TECHNICAL_REQUIREMENTS.md for full detail)

- **Mobile:** React Native (Expo SDK 57, Expo Router, TypeScript) in `Android/`, Android-only, `expo-sqlite` kv-store as the offline read cache.
- **Backend:** ASP.NET Core Web API, JWT auth, EF Core + Npgsql. `docs/TECHNICAL_REQUIREMENTS.md` §3 says ".NET 8 LTS"; every `.csproj` actually targets `net10.0` — follow the code.
- **Database:** PostgreSQL + `pg_trgm` — relational subscription data plus trigram similarity for provider matching
- **Provider matching:** in-database trigram similarity. There is no AI provider and no API key — see `docs/TECHNICAL_REQUIREMENTS.md` §8 for the scoring and the known gap (rebrands/semantic input fall through to manual entry)

## Architectural Rules to Preserve

- **Currency conversion is a read-time projection, not a write-time mutation.** Store each subscription's original `currency` + `cost_amount` unchanged; convert to the user's home currency only when computing dashboard/burn-rate totals. Never overwrite stored amounts with converted values.
- **Burn-rate math is server-side, and counts cycles rather than days.** Each active subscription's annual cost is `cost * charges_per_year` (Weekly 52, Monthly 12, Quarterly 4, Yearly 1); sum those, then derive `Monthly = annual / 12` and `Weekly = annual / 52`. Keep this logic in the API, not duplicated in the mobile client. **Do not reintroduce a daily rate** (`cost / cycle_days`): with a 30-day month and a 365-day year it makes a year 12.17 months long, so 1000/month reported 12166.67/year. A calendar month is a count, not 30 days.
- **Provider matching is one SQL query, not a service call.** Scoring is `greatest(word_similarity(input, name), word_similarity(name, input))` — both directions are load-bearing. Thresholds are measured, not guessed; `SubscriptionCatalogTrigramMatchTests` pins them.
- **Renewal reminders are scheduled on-device, not pushed.** `planReminders` (`Android/src/lib/logic.ts`) derives them from the subscription list; the OS delivers them with the app closed. There is no push service, no device-token table and no vendor project.
- **Nothing advances `next_billing_date` on a timer.** A date left in the past is how the app says a charge is outstanding, which is what `SubscriptionDto.IsOverdue` reads. It moves only when the user marks the charge paid (`POST /api/v1/subscriptions/{id}/mark-paid`), which records `last_paid_date` and steps on exactly one cycle from the date just settled — not from today, which would silently write off the periods in between. The nightly `BillingDateAdvanceBackgroundService` was removed for this reason: it erased the very signal overdue depends on.
- **The mobile cache is a read-only mirror — there are no offline writes.** It is refreshed from successful GETs only; there is no outbox, no replay on reconnect and no conflict resolution. A write made without a connection fails and is discarded, saying so (`writeErrorMessage` in `Android/src/api/client.ts`), with the write buttons disabled when the device has no network. That is the intended behaviour, not a gap waiting to be filled — offline *reads* cover the common case, while offline writes would need queueing, replay ordering, a pending state, and conflict handling against the `Version` token (a queued edit can hit the same stale-version 409, with nobody to ask while the app is closed). Decided in #144; don't add an outbox without revisiting it.
- **`billing_cycle_type`** is a fixed enum: `Weekly`, `Monthly`, `Yearly`, `OneTime`, `Quarterly`. Extend deliberately, not ad hoc — and **append, never insert**. It is a native Postgres enum (mapped in `AppDbContextOptionsFactory`), so a new member needs a migration and a matching entry in the client's `BillingCycle` union. `Quarterly` sits last despite reading better after `Monthly`; the client's `CYCLES_ORDERED` orders the picker instead.

## Conventions

- C# for the backend, TypeScript (React Native/Expo) for the mobile client in `Android/`. No Swift/Kotlin layers - native config goes through `Android/app.json` and config plugins (`Android/android/` is generated and gitignored).
- REST API versioned under `/api/v1/`.
- Don't add a web or desktop client — mobile-only is a deliberate v1 scope decision (see NON_TECHNICAL_REQUIREMENTS.md §6).
- Don't build bank/email auto-scraping or in-app cancellation-on-behalf-of-user — explicitly out of scope for v1.

## When Implementing

1. Check `docs/TECHNICAL_REQUIREMENTS.md` §6 for the specific feature's data model and behavior before writing code.
2. The schema is defined by the EF Core entity configurations in `src/SubVora.Infrastructure/Data/Configurations/` and the migrations beside them — there is no DDL copy to keep in step. `docs/Design.md` describes what each table is *for*; update it when a table's purpose or a design decision changes, not when a column does.
3. Local config lives in `src/SubVora.Api/appsettings.Development.json` — gitignored, dockerignored, used directly by `dotnet run`, and mounted read-only by `docker-compose.yml` as `appsettings.Docker.json` (the environment stays `Docker` because `Program.cs` skips `UseHttpsRedirection` only for it). Copy `appsettings.Development.example.json` to create it. Compose overrides only the database and SMTP hosts; no secret is passed via `environment:` or baked into an image layer.
4. Keep secrets (DB connection string, JWT signing key) out of source control — use user-secrets/environment config locally, a managed vault in deployed environments.

## Secret Scanning (hard stop)

This repo blocks commits/pushes that introduce secrets, via [`detect-secrets`](https://github.com/Yelp/detect-secrets) wired up as a git hook.

- **One-time setup per clone:** run `git config core.hooksPath .githooks` (this is a local git config, not versioned — every clone/worktree needs to run it once) and `pip install detect-secrets`.
- Hooks live in `.githooks/pre-commit` and `.githooks/pre-push`, checked against `.secrets.baseline` at the repo root.
- **This is a hard stop, not a suggestion.** If a hook blocks a commit/push, fix the actual issue (remove the secret, use User Secrets/env vars) or mark a genuine false positive via `detect-secrets audit .secrets.baseline` and re-commit the updated baseline. Do not bypass with `--no-verify` or `git commit -n`.
- If the baseline needs to be regenerated after legitimate changes: `python3 -m detect_secrets scan --baseline .secrets.baseline`.

# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

SubVora — cross-platform subscription tracker with cancellation reminders, burn-rate dashboard, and multi-currency support. Full requirements live in `docs/`:

- `docs/TECHNICAL_REQUIREMENTS.md` — architecture, stack, API/DB requirements
- `docs/NON_TECHNICAL_REQUIREMENTS.md` — feature/product requirements
- `docs/Design.md` — architecture diagram, DB schema (DDL), AI flow, sample code

Read the relevant doc before implementing a feature — don't guess at requirements that are already written down.

## Current State

Requirements and design are defined. No application code exists yet (no MAUI project, no ASP.NET Core project). When scaffolding begins, expect this layout:

```
/src
  /SubVora.Mobile      # .NET MAUI app (Android + iOS)
  /SubVora.Api         # ASP.NET Core Web API
  /SubVora.Shared      # Shared DTOs/models between client and API (if used)
/tests
  /SubVora.Api.Tests
  /SubVora.Mobile.Tests
/docs
```

Update this file once that structure exists for real, including actual build/test/run commands.

## Stack (see docs/TECHNICAL_REQUIREMENTS.md for full detail)

- **Mobile:** .NET MAUI, single C# codebase for Android + iOS, local SQLite cache for offline support
- **Backend:** ASP.NET Core Web API (.NET 8 LTS), JWT auth, EF Core + Npgsql
- **Database:** PostgreSQL + `pgvector` — relational subscription data alongside vector embeddings for semantic matching
- **AI:** OpenAI embeddings/LLM, called **server-side only** — API keys must never ship in the mobile client

## Architectural Rules to Preserve

- **Currency conversion is a read-time projection, not a write-time mutation.** Store each subscription's original `currency` + `cost_amount` unchanged; convert to the user's home currency only when computing dashboard/burn-rate totals. Never overwrite stored amounts with converted values.
- **Burn-rate math is server-side.** Normalize every active subscription to a daily rate (`cost / cycle_days`), sum, then project to weekly/monthly/yearly. Keep this logic in the API, not duplicated in the mobile client.
- **AI/embedding calls happen only in the backend.** The mobile client never calls OpenAI directly.
- **Background renewal-scan job must be idempotent.** Guard against duplicate push notifications (track sent alerts, e.g. via a `notifications_log` table) when adding or touching the reminder job.
- **`billing_cycle_type`** is a fixed enum: `Weekly`, `Monthly`, `Yearly`, `OneTime`. Extend deliberately, not ad hoc.

## Conventions

- C# throughout (mobile + backend) — no separate Swift/Kotlin/JS layers per the single-codebase design goal.
- REST API versioned under `/api/v1/`.
- Don't add a web or desktop client — mobile-only is a deliberate v1 scope decision (see NON_TECHNICAL_REQUIREMENTS.md §6).
- Don't build bank/email auto-scraping or in-app cancellation-on-behalf-of-user — explicitly out of scope for v1.

## When Implementing

1. Check `docs/TECHNICAL_REQUIREMENTS.md` §6 for the specific feature's data model and behavior before writing code.
2. Match the DDL in `docs/Design.md` unless there's a reason to deviate — if you do deviate, update `docs/Design.md` in the same change.
3. Keep secrets (OpenAI key, DB connection string, JWT signing key) out of source control — use user-secrets/environment config locally, a managed vault in deployed environments.

## Secret Scanning (hard stop)

This repo blocks commits/pushes that introduce secrets, via [`detect-secrets`](https://github.com/Yelp/detect-secrets) wired up as a git hook.

- **One-time setup per clone:** run `git config core.hooksPath .githooks` (this is a local git config, not versioned — every clone/worktree needs to run it once) and `pip install detect-secrets`.
- Hooks live in `.githooks/pre-commit` and `.githooks/pre-push`, checked against `.secrets.baseline` at the repo root.
- **This is a hard stop, not a suggestion.** If a hook blocks a commit/push, fix the actual issue (remove the secret, use User Secrets/env vars) or mark a genuine false positive via `detect-secrets audit .secrets.baseline` and re-commit the updated baseline. Do not bypass with `--no-verify` or `git commit -n`.
- If the baseline needs to be regenerated after legitimate changes: `python3 -m detect_secrets scan --baseline .secrets.baseline`.

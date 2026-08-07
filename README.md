# Subvora

**The smart way to manage subscriptions.**

SubVora is a cross-platform mobile app that tracks all your subscriptions, warns you before renewals hit, and shows your real spend — across currencies — in one dashboard.

## Features

- 🏷️ Auto-matched brand logos and categories
- 🔁 Weekly / Monthly / Yearly / One-time billing types
- 📅 Purchase & next-billing date tracking
- 🔔 Configurable renewal alerts (1 / 3 / 7 days before), delivered as on-device notifications — no push service, no account
- 💳 Track source of deduction (which card/account is billed)
- 📊 **Burn Rate dashboard** — see spend per week, month, and year at a glance
- 🌍 **Multi-currency** — track subscriptions in any currency, view totals in your home currency

## Tech Stack

- **Mobile:** .NET MAUI (single C# codebase, Android + iOS)
- **Backend:** ASP.NET Core Web API
- **Database:** PostgreSQL + `pg_trgm`
- **Provider matching:** trigram similarity in the database — no AI provider, no API key

See [docs/Design.md](./docs/Design.md) for the full architecture and database schema.

## Documentation

| Doc | Purpose |
|---|---|
| [docs/TECHNICAL_REQUIREMENTS.md](./docs/TECHNICAL_REQUIREMENTS.md) | Engineering/architecture requirements |
| [docs/NON_TECHNICAL_REQUIREMENTS.md](./docs/NON_TECHNICAL_REQUIREMENTS.md) | Feature/product requirements |
| [docs/Design.md](./docs/Design.md) | Architecture diagram, DB schema, matching flow |
| [docs/ADDING_A_PROVIDER.md](./docs/ADDING_A_PROVIDER.md) | How to add, rename, or remove a subscription provider |
| [docs/DEPLOYMENT.md](./docs/DEPLOYMENT.md) | Hosting the API and shipping the Android APK |
| [docs/GO_LIVE_CHECKLIST.md](./docs/GO_LIVE_CHECKLIST.md) | Step-by-step checklist for the first deploy |
| [CLAUDE.md](./CLAUDE.md) | Guidance for Claude Code working in this repo |

API docs (Swagger UI) are served at `/swagger` when the API runs in the `Development` environment.

## Status

Backend and mobile client are both implemented and under active development. Backend: full DB schema (users, categories, payment sources, subscription catalog with trigram matching, user subscriptions, FX rates, refresh tokens, notifications log, device tokens), auth (register/login/refresh/logout/password reset with JWT + rotating refresh tokens), subscription CRUD, trigram catalog matching, burn-rate dashboard, and the nightly billing-date advance job. Mobile: .NET MAUI client covering auth, subscription list/detail, dashboard, categories, payment sources, and settings, with an offline SQLite mirror and on-device renewal reminders.

## Getting Started

**One-time setup per clone** — the repo blocks commits that introduce secrets:

```
git config core.hooksPath .githooks
pip install detect-secrets
```

**Local development**

One gitignored config file serves both `dotnet run` and Docker, so the signing key is defined once
and a token minted by either is accepted by the other.

```
cp src/SubVora.Api/appsettings.Development.example.json src/SubVora.Api/appsettings.Development.json
# edit it: set Jwt:Secret to `openssl rand -base64 48`
```

Then either:

```
docker compose up -d --build          # whole stack, API on :8080
```

```
docker compose up -d db mailpit       # dependencies only
dotnet run --project src/SubVora.Api  # API on :5271
```

- Swagger at `/swagger`; Postgres published on `5433`; Mailpit inbox at <http://localhost:8025>
- Migrations and the catalog sync run at start; there is no separate step
- Compose overrides exactly two settings, neither secret: the database host (inside a container
  `localhost` is the container) and the SMTP host. Everything else, including the signing key,
  comes from the mounted file

Create `appsettings.Development.json` **before** the first `docker compose up`. If it is missing,
Docker creates a directory at the mount path and the API fails on an empty connection string.

**Tests**

Each project is tested individually rather than via the solution — on Linux `SubVora.Mobile` only
exposes its `net10.0-android` target, so `SubVora.Mobile.Tests` (Windows-only TFM) cannot resolve its
project reference there. CI splits them the same way.

```
dotnet test tests/SubVora.Api.Tests/SubVora.Api.Tests.csproj -c Release
dotnet test tests/SubVora.Application.Tests/SubVora.Application.Tests.csproj -c Release
dotnet test tests/SubVora.Infrastructure.Tests/SubVora.Infrastructure.Tests.csproj -c Release
dotnet test tests/SubVora.Mobile.Tests/SubVora.Mobile.Tests.csproj -c Release   # Windows only
```

`SubVora.Api.Tests` and `SubVora.Infrastructure.Tests` start a real `pgvector/pgvector:pg16` container (a stock Postgres 16 plus an extension the app no longer uses — kept so existing dev volumes keep working)
via Testcontainers, so Docker must be running.

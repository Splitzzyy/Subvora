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

1. Start the local database: `docker compose up -d db`
2. Provide local secrets. These are never committed — `appsettings.json` ships them blank, and the API
   fails fast at startup rather than running with an empty signing key:
   ```
   cd src/SubVora.Api
   dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5433;Database=subvora_dev;Username=subvora;Password=subvora_dev_password"
   dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
   ```
3. Apply migrations: `dotnet ef database update --project src/SubVora.Infrastructure --startup-project src/SubVora.Infrastructure`
4. Run the API: `dotnet run --project src/SubVora.Api`
5. Browse the API docs at `http://localhost:<port>/swagger`

**Running the whole stack in Docker**

No secrets are passed on the command line, stored in `docker-compose.yml`, or baked into the image.
Everything lives in one gitignored file that is mounted into the container at runtime.

```
cp src/SubVora.Api/appsettings.Docker.example.json src/SubVora.Api/appsettings.Docker.json
# edit it: set Jwt:Secret to `openssl rand -base64 48`
docker compose up -d --build
```

- API on <http://localhost:8080>, Swagger at `/swagger`, Postgres published on `5433`
- Mailpit catches password-reset codes and already-registered notices — inbox at <http://localhost:8025>
- Migrations and the catalog sync run at start; there is no separate step

Create `appsettings.Docker.json` **before** the first `up`. If it is missing, Docker creates a
directory at that mount path and the API fails on an empty connection string.

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

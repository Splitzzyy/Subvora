# Subvora

**The smart way to manage subscriptions.**

SubVora is a cross-platform mobile app that tracks all your subscriptions, warns you before renewals hit, and shows your real spend — across currencies — in one dashboard.

## Download

**[⬇ Get the Android APK — latest release](https://github.com/Splitzzyy/Subvora/releases/latest)**

Android 8.0 (API 26) or newer. Download the `.apk` on your phone and tap it.

Android blocks the first attempt and offers **Settings → Allow from this source** — that permission
is granted per app, to whatever opened the file (Chrome, Files, …). Tap Install again afterwards.
Play Protect will also warn that the app is unrecognised: that is what it says about any APK not
distributed through the Play Store, not a finding about this one.

No account is needed to download. Register inside the app.

> iOS is not distributed. The Apple Developer Program is $99/yr and free provisioning only
> sideloads to a device you own, seven days at a time. Nothing in the codebase blocks it later —
> see [docs/DEPLOYMENT.md](./docs/DEPLOYMENT.md).

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
| [docs/DEPLOYMENT.md](./docs/DEPLOYMENT.md) | Hosting the API, cutting a release, shipping the Android APK |
| [docs/debug/ANDROID_DEVICE.md](./docs/debug/ANDROID_DEVICE.md) | Running the app on a physical phone against a local API |
| [CLAUDE.md](./CLAUDE.md) | Guidance for Claude Code working in this repo |

API docs (Swagger UI) are served at `/swagger` when the API runs in the `Development` environment.

## Status

**Live.** The API runs on Render against Neon Postgres, and v1.0.0 of the Android app is published
under [Releases](https://github.com/Splitzzyy/Subvora/releases/latest). Both sides remain under
active development.

**Backend:** users, categories, payment sources, subscription catalog, user subscriptions, FX rates,
refresh tokens and password-reset codes; auth (register / login / refresh / logout / password reset /
change password) on JWT plus rotating refresh tokens; subscription CRUD with optimistic concurrency;
trigram catalog matching; and the burn-rate dashboard.

Nothing advances a billing date on a timer — a date left in the past is how the app says a charge is
outstanding, and it moves only when the user marks the charge paid.

**Mobile:** .NET MAUI client covering auth, subscription list and detail, dashboard, categories,
payment sources and settings, with a read-only offline SQLite mirror and on-device renewal
reminders. There is no push service: reminders are derived from the subscription list and scheduled
with the OS, which delivers them with the app closed.

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

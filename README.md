# Subvora

**The smart way to manage subscriptions.**

SubVora is a cross-platform mobile app that tracks all your subscriptions, warns you before renewals hit, and shows your real spend — across currencies — in one dashboard.

## Features

- 🏷️ Auto-matched brand logos and categories
- 🔁 Weekly / Monthly / Yearly / One-time billing types
- 📅 Purchase & next-billing date tracking
- 🔔 Configurable renewal alerts (1 / 3 / 7 days before)
- 💳 Track source of deduction (which card/account is billed)
- 📊 **Burn Rate dashboard** — see spend per week, month, and year at a glance
- 🌍 **Multi-currency** — track subscriptions in any currency, view totals in your home currency

## Tech Stack

- **Mobile:** .NET MAUI (single C# codebase, Android + iOS)
- **Backend:** ASP.NET Core Web API
- **Database:** PostgreSQL + `pgvector`
- **AI:** OpenAI embeddings/LLM for smart subscription matching and categorization

See [docs/Design.md](./docs/Design.md) for the full architecture and database schema.

## Documentation

| Doc | Purpose |
|---|---|
| [technical_requirements.md](./technical_requirements.md) | Locked schema, API contract, and implementation decisions (source of truth) |
| [docs/TECHNICAL_REQUIREMENTS.md](./docs/TECHNICAL_REQUIREMENTS.md) | Engineering/architecture requirements |
| [docs/NON_TECHNICAL_REQUIREMENTS.md](./docs/NON_TECHNICAL_REQUIREMENTS.md) | Feature/product requirements |
| [docs/Design.md](./docs/Design.md) | Architecture diagram, DB schema, AI flow |
| [CLAUDE.md](./CLAUDE.md) | Guidance for Claude Code working in this repo |

API docs (Swagger UI) are served at `/swagger` when the API runs in the `Development` environment.

## Status

Backend foundation in progress: solution scaffolding, full DB schema (users, categories, payment sources, subscription catalog with pgvector, user subscriptions, FX rates, refresh tokens, notifications log), and auth (register/login/refresh/logout with JWT + rotating refresh tokens) are implemented. Mobile client (.NET MAUI) not yet started.

## Getting Started

1. Start the local database: `docker compose up -d`
2. Provide local secrets (never committed — see [CLAUDE.md](./CLAUDE.md)):
   ```
   cd src/SubVora.Api
   dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=subvora_dev;Username=subvora;Password=subvora_dev_password"
   dotnet user-secrets set "Jwt:Secret" "<a long random string>"
   ```
3. Apply migrations: `dotnet ef database update --project src/SubVora.Infrastructure`
4. Run the API: `dotnet run --project src/SubVora.Api`
5. Browse the API docs at `http://localhost:<port>/swagger`

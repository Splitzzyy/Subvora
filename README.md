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
| [docs/TECHNICAL_REQUIREMENTS.md](./docs/TECHNICAL_REQUIREMENTS.md) | Engineering/architecture requirements |
| [docs/NON_TECHNICAL_REQUIREMENTS.md](./docs/NON_TECHNICAL_REQUIREMENTS.md) | Feature/product requirements |
| [docs/Design.md](./docs/Design.md) | Architecture diagram, DB schema, AI flow |
| [CLAUDE.md](./CLAUDE.md) | Guidance for Claude Code working in this repo |

## Status

Early-stage — requirements and design defined; implementation not yet started.

## Getting Started

_To be filled in once the MAUI and ASP.NET Core projects are scaffolded._

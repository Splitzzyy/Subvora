# Deployment

How to put SubVora in front of real users on free infrastructure. This is a one-time provisioning
runbook plus the per-release loop; the ordering in [First deploy](#first-deploy) is load-bearing.

Doing the first deploy right now? Work through [GO_LIVE_CHECKLIST.md](./GO_LIVE_CHECKLIST.md)
instead — same steps as tick-boxes, without the reasoning. Come back here when you want to know why
something is the way it is.

Scope: **Android only.** iOS has no free distribution path — the Apple Developer Program is $99/yr
and free provisioning only sideloads to a device you own, for seven days at a time. Nothing in the
repo is iOS-specific enough to block adding it later.

## What runs where

| Piece | Host | Free allowance |
|---|---|---|
| API (container) | Render, Docker web service | 750 instance-hours/month, 512 MB, 0.1 CPU, HTTPS on `*.onrender.com` |
| PostgreSQL 16 | Neon | 0.5 GB storage, scale-to-zero compute |
| Outbound SMTP | Brevo relay | 300 emails/day |
| Exchange rates | api.exchangerate.host | no key, no account — already hardcoded in `Program.cs` |
| Android APK | GitHub Releases | unlimited |
| Keep-warm ping | cron-job.org | 5-minute interval |

No container registry: Render builds the image from the repo. No push service, no object storage,
no cache — see the architectural rules in [CLAUDE.md](../CLAUDE.md).

**Costs nothing recurring.** Deliberately excluded: Apple Developer Program, Google Play Console
($25 one-time), custom domain.

## Prerequisites

- Accounts on [Render](https://render.com), [Neon](https://neon.tech), [Brevo](https://brevo.com).
  None require a payment method.
- `keytool` (ships with any JDK) to generate the signing keystore.
- Admin on the GitHub repo, to add secrets and a `production` environment.

## First deploy

### 1. Create the database

Create a Neon project on **Postgres 16**. Copy the **direct** connection host — the one *without*
`-pooler` in it. Two reasons: `AppDbContextOptionsFactory` maps two Postgres enums at connection
open, which is fussy through PgBouncer, and a single free API instance has no need for external
pooling anyway.

Neon is the pick because the migration history needs **both** extensions. `pg_trgm` is what provider
matching runs on today; `vector` was created by `AddSubscriptionCatalog` and dropped again by
`ReplaceCatalogEmbeddingWithTrigram`, so applying the full history from scratch still executes
`CREATE EXTENSION vector` even though the running app never touches it. A provider that blocks
`vector` fails the migration partway.

Assemble the connection string in Npgsql's format — Neon's dashboard hands you a `postgresql://`
URL, which Npgsql does not accept:

```
Host=ep-xxx-xxx.region.aws.neon.tech;Database=neondb;Username=...;Password=...;SSL Mode=VerifyFull
```

`VerifyFull` works as-is: Neon's certificate chains to a public CA, so no
`Trust Server Certificate=true` and no bundled root cert are needed.

### 2. Migrate, before the API ever boots

Add the connection string as a secret named `PROD_DB_CONNECTION_STRING` under a GitHub environment
called `production`, then run the **Database Migration (Production)** workflow
(`.github/workflows/db-migrate.yml`) from the Actions tab. It builds a self-contained EF migration
bundle and applies it.

Do this *first*, by hand, for the **first** deploy only — there is no previous release to sequence
against yet. From then on the same workflow runs automatically on every push to `main`; see
[Per-release](#per-release) below.

In the `Production` environment the API does not migrate on startup — that is deliberate, and
migrations stay an explicit deploy step. Two reasons worth keeping: it keeps DDL rights out of the
credential the API itself runs with, and it keeps a slow migration from colliding with Render's
health-check timeout mid-DDL, which would leave a partially applied schema.

`SubscriptionCatalogSyncService` runs at boot regardless, and against an unmigrated database it
logs the failure and swallows it, with no retry until the next restart. The visible symptom is an
empty provider catalog and no brand matching, which is confusing to diagnose after the fact.

### 3. Set up email

Sign up for Brevo, verify a sender address (a plain Gmail address is fine — no domain required),
and create an **SMTP key**. The credentials are the Brevo *login email* as username and the SMTP
key as password; the account password will not authenticate.

Email is only used for password-reset codes, so a misconfiguration here does not stop the API from
starting — `SmtpEmailSender` reads its config at send time and throws only then.

### 4. Deploy the API

Point Render at the repo and let it pick up [`render.yaml`](../render.yaml) as a Blueprint. It will
prompt for the five values marked `sync: false`:

| Variable | Value |
|---|---|
| `ConnectionStrings__Default` | the Npgsql string from step 1 |
| `Jwt__Secret` | `openssl rand -base64 48` — **must be ≥32 UTF-8 bytes** or startup throws |
| `Smtp__Username` | Brevo login email |
| `Smtp__Password` | Brevo SMTP key |
| `Smtp__FromAddress` | the verified sender address |

The first build is a cold Docker build and takes roughly ten minutes.

`render.yaml` sets `autoDeploy: false`, so Render will *not* redeploy on push by itself — the
migration workflow triggers it instead, after the schema is in place. Create a **Deploy Hook** for
the service (Render dashboard → the service → Settings → Deploy Hook), and add the URL as a secret
named `RENDER_DEPLOY_HOOK_URL` in the `production` GitHub environment. The URL embeds its own key,
which is why it is a secret rather than a plain setting.

If you deploy somewhere other than Render, the service name in `render.yaml` and the Release
`ApiBaseAddress` in `SubVora.Mobile.csproj` both have to change — and the second one means cutting a
new APK (see [Distribution](#distribution)).

### 5. Keep it warm

Add a cron-job.org job that GETs `https://subvora-api.onrender.com/` every 5 minutes. Free Render
services sleep after 15 minutes of no inbound traffic, and waking a .NET container takes ~40–60s —
long enough that the app looks broken.

Ping the **root path**, which 404s, not `/health`. The health check includes an Npgsql probe, so
pinging it every five minutes would hold Neon's compute awake around the clock and burn the free
compute-hour allowance. A 404 still counts as inbound traffic for Render's purposes.

At a 5-minute interval the service never sleeps, consuming ~730 of the 750 free instance-hours per
month. That budget covers exactly one always-on service.

### 6. Create the signing keystore

```
keytool -genkeypair -v -keystore subvora-release.keystore -alias subvora \
  -keyalg RSA -keysize 2048 -validity 10000
```

**Back this file up somewhere durable, outside the repo.** Android identifies an app by its signing
key: lose the keystore and you can never ship an upgrade to an already-installed app — users have
to uninstall and lose their local cache. `.gitignore` blocks `*.keystore`/`*.jks` because
`detect-secrets` does not inspect binaries and would not catch it.

Then add four repo secrets:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | `base64 -w0 subvora-release.keystore` |
| `ANDROID_KEYSTORE_PASSWORD` | store password from `keytool` |
| `ANDROID_KEY_ALIAS` | `subvora` |
| `ANDROID_KEY_PASSWORD` | key password from `keytool` |

## Per-release

Pushing to `main` is the whole API release process. `.github/workflows/db-migrate.yml` runs on the
push, applies any pending migrations, and only then calls Render's deploy hook.

The ordering is the point. A migration that fails takes the workflow down with it, the hook never
fires, and the previous release carries on against the schema it was built for — the cost of a bad
migration is a red check mark rather than an outage. Nothing deploys ahead of its schema, and there
is no step anyone has to remember.

Two things this does *not* protect against:

- **Code and schema still land seconds apart.** A migration that breaks the *currently running*
  release — dropping or renaming a column it still reads — causes errors in that window. Use
  expand-contract: add the new column, ship code that writes both, drop the old one a release
  later.
- **Nothing rolls the schema back.** Reverting a bad deploy reverts the code; the migration stays
  applied. That is usually what you want, and it is another reason expand-contract matters.

`MigrationDriftTests` fails CI if an entity configuration changed without a migration to match, so
the pending-migration set is always what the model actually needs.

To re-run a migration without a code change — or for the first deploy, before there is a release to
sequence against — run the workflow by hand from the Actions tab. `workflow_dispatch` skips the
deploy-hook step.

## Distribution

Tag a release and CI does the rest:

```
git tag v1.0.0 && git push origin v1.0.0
```

`.github/workflows/release-android.yml` builds a signed APK and attaches it to the GitHub Release.
Users download it and sideload; `adb install -r <apk>` for a device on your desk.

Two things the workflow controls deliberately:

- **It does not pass `ApiBaseAddress`.** The Release default in `SubVora.Mobile.csproj` is the single
  source of truth for which backend a shipped build talks to. That address is read from assembly
  metadata by `ApiConfig`, so it is fixed at build time — a shipped APK cannot be repointed, and
  moving hosts means a new release.
- **`ApplicationVersion` comes from the run number**, not the `.csproj`. Android refuses an in-place
  upgrade unless the version code strictly increases, and the checked-in value is static.

Release builds also have no cleartext-HTTP path: `usesCleartextTraffic` is set only by the
Debug-only manifest overlay. That is why the API must be reachable over HTTPS, which Render's
subdomain certificate provides for free.

## Verifying a deploy

```
curl -i https://subvora-api.onrender.com/health     # 200 Healthy -> DB reachable over TLS
curl -i http://subvora-api.onrender.com/health      # 307 to https, exactly one hop
curl -i https://subvora-api.onrender.com/swagger    # 404 -> dev surface is not public
```

The second one matters. Render terminates TLS and forwards plain HTTP; `UseForwardedHeaders` in
`Program.cs` is what stops `UseHttpsRedirection` from redirecting to a URL that arrives as `http`
again, forever. If you see a redirect loop, that middleware is missing or ordered after the
redirect.

Then, against the deployed instance: register, log in, add a subscription, and load
`/api/v1/dashboard/burn-rate`. A populated category breakdown proves the catalog sync inserted
providers and trigram matching is working — i.e. that step 2 ran before step 4. Request a password
reset and confirm delivery in Brevo's activity log.

On a device, with **no** `adb reverse` mapping active (unlike the local loop in
[debug/ANDROID_DEVICE.md](./debug/ANDROID_DEVICE.md)): log in, confirm the list populates, then kill
wifi and confirm cached rows still render with the cached-data indicator. Watch
`adb logcat -b crash` throughout.

## Known limits of this setup

These are accepted trade-offs of the free tier, not bugs to fix:

- **Queued emails are not durable.** `QueuedEmailSender` is an in-memory channel drained by
  `EmailDispatchBackgroundService`; a restart mid-queue drops the reset code silently and the user
  has to request another. Render was chosen partly because it does *not* throttle CPU between
  requests — on a scale-to-zero platform like Cloud Run the drain loop stalls and this breaks
  outright rather than occasionally.
- **The nightly FX refresh may not run.** `FxRateRefreshBackgroundService` fires at 01:00 UTC and on
  boot, so a restart pattern can skip it. Harmless: `FxRateService` fetches on cache miss on the
  request path, so rates stay correct either way.
- **512 MB / 0.1 CPU is tight.** Watch Render's logs for OOM restarts under load. The fix is a paid
  instance, not a code change.
- **The auth rate limit is per-IP and shared-NAT-blind.** Ten login attempts per 60 seconds per
  client address, keyed on the forwarded address. Users behind one NAT share a bucket.
- **`SCHEDULE_EXACT_ALARM` is declared without a fallback.** Irrelevant for sideloaded APKs; the day
  distribution moves to Play it becomes a declaration form, alongside a target-API floor and a
  privacy policy URL.

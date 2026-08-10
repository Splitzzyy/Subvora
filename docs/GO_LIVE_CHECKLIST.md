# Go-live checklist

Tick-box version of [DEPLOYMENT.md](./DEPLOYMENT.md). That doc explains *why* each choice was made;
this one is just the order to do things in. Roughly 90 minutes end to end, most of it waiting on
Render's first Docker build.

The code side is already done and merged — no source changes are needed to complete this list.

**Do the steps in order.** Step 4 must finish before step 6, or the API starts against an
unmigrated database, silently fails its catalog sync, and brand matching comes up empty with no
retry until you restart it.

---

## Accounts to create (~10 min)

None need a payment method.

- [ ] [Neon](https://neon.tech) — Postgres
- [ ] [Render](https://render.com) — API hosting; sign in with GitHub so it can see the repo
- [ ] [Brevo](https://brevo.com) — SMTP for password-reset codes
- [ ] [cron-job.org](https://cron-job.org) — keep-warm ping

## 1. Database (~5 min)

- [ ] Create a Neon project on **Postgres 16**
- [ ] In *Connection Details*, switch the snippet dropdown to **.NET** and **untick** "Pooled
      connection" — you want the direct host, no `-pooler` in it
- [ ] Save the connection string somewhere temporary. It must look like this — Npgsql does not
      accept the `postgresql://` URL form Neon shows by default:

```
Host=ep-xxx-xxx.region.aws.neon.tech;Database=neondb;Username=...;Password=...;SSL Mode=VerifyFull
```

## 2. Generate a JWT signing key (~1 min)

- [ ] Run it and keep the output for step 5:

```
openssl rand -base64 48
```

Anything shorter than 32 bytes makes the API throw on startup. The 48-byte output above is safe.

## 3. Email sender (~10 min)

- [ ] Brevo → *Senders* → add and verify a sender address. A personal Gmail address is fine; no
      domain needed. Verification is a click-through email.
- [ ] Brevo → *SMTP & API* → *SMTP* tab → create an **SMTP key**
- [ ] Record three values: your Brevo **login email** (this is the SMTP username), the **SMTP key**
      (this is the password — the account password will not authenticate), and the verified
      **sender address**

## 4. Migrate the database — before anything else runs (~5 min)

- [ ] GitHub repo → *Settings* → *Environments* → **New environment** named exactly `production`
- [ ] Inside that environment, add secret `PROD_DB_CONNECTION_STRING` = the string from step 1
- [ ] GitHub → *Actions* → **Database Migration (Production)** → *Run workflow*
- [ ] Confirm it goes green

Nothing to write — `.github/workflows/db-migrate.yml` already exists and needs no changes.

## 5. Deploy the API (~15 min, mostly build time)

- [ ] Render dashboard → *New* → **Blueprint** → pick this repo. It reads `render.yaml` on its own.
- [ ] Render prompts for five values. Fill them:

| Prompt | Value |
|---|---|
| `ConnectionStrings__Default` | connection string from step 1 |
| `Jwt__Secret` | output from step 2 |
| `Smtp__Username` | Brevo login email |
| `Smtp__Password` | Brevo SMTP key |
| `Smtp__FromAddress` | verified sender address |

- [ ] Apply, then wait out the first Docker build (~10 min; later pushes to `main` are faster and
      automatic)
- [ ] Note the assigned URL. **If it is not `https://subvora-api.onrender.com`** — because the name
      was taken — you must edit the Release `ApiBaseAddress` in
      `src/SubVora.Mobile/SubVora.Mobile.csproj` to match before step 8, or the app will call a
      host that does not exist.
- [ ] Render dashboard → the service → *Settings* → **Deploy Hook** → copy the URL, and add it as
      secret `RENDER_DEPLOY_HOOK_URL` in the same `production` environment as step 4:

```
gh secret set RENDER_DEPLOY_HOOK_URL --env production --repo <owner>/<repo>
```

**Do not skip this.** `render.yaml` sets `autoDeploy: false`, so that hook is the only thing that
deploys anything. Without it every push to `main` migrates the database and then fails on an empty
URL — a red check mark, and code that never ships while the schema moves ahead of it. The URL
embeds its own key, which is why it is a secret rather than a plain setting.

- [ ] Redeploy once, by hand, if the service first booted *before* step 4 finished — Render
      dashboard → **Manual Deploy** → *Deploy latest commit*. `SubscriptionCatalogSyncService` runs
      only at boot: against an unmigrated database it logs the failure, swallows it, and never
      retries. The symptom is an empty provider catalog behind a perfectly healthy `/health`, which
      step 6 is written to catch.

## 6. Check it works (~5 min)

- [ ] These three, substituting your URL:

```
curl -i https://subvora-api.onrender.com/health       # expect 200, body "Healthy" - includes the DB probe
curl -i https://subvora-api.onrender.com/health/live  # expect 200 - no DB, what Render polls
curl -i http://subvora-api.onrender.com/health        # expect one redirect hop to https
curl -i https://subvora-api.onrender.com/swagger      # expect 404 - dev surface must not be public
```

If `/health/live` returns 200 but `/health` does not, the app is running and the database is not
reachable — check the connection string before anything else.

The third checks **one hop and no loop**, not a particular code: on Render it is a `301` from the
Cloudflare edge, which never reaches the app (no `x-render-origin-server: Kestrel` header on the
response). `UseHttpsRedirection`'s `307` only appears off-Render. A redirect *loop* is the real
failure, and means `UseForwardedHeaders` is missing or mis-ordered.

- [ ] Register a user and log in via `/api/v1/auth/register` and `/api/v1/auth/login`
- [ ] POST `{ "input": "netflix" }` to `/api/v1/subscriptions/resolve` with that token. This is the
      fastest proof that step 4 ran before step 5:

```
tier         : AutoFill        <- pg_trgm is live and scoring
providerName : Netflix         <- the catalog sync inserted providers
categoryId   : <non-null>      <- system categories seeded, and the sync resolved one by name
```

`tier: Manual` means the catalog is empty — the sync ran against an unmigrated database. Redeploy
(step 5's last box) and try again.

- [ ] Add a subscription, then GET `/api/v1/dashboard/burn-rate` — a populated category breakdown
      confirms the same thing end to end
- [ ] Confirm SMTP. Registering a *second* time with the same address is enough: that path emails
      the existing owner ("Someone tried to create a SubVora account with your email") while
      returning the identical 202, so it doubles as an anti-enumeration check. Otherwise POST to
      `/api/v1/auth/forgot-password` and look in Brevo's *Statistics → Log*.

A redirect loop on the third curl means the deployed image predates the `UseForwardedHeaders`
change; redeploy from latest `main`.

## 7. Stop it falling asleep (~3 min)

- [ ] cron-job.org → create a job → URL `https://subvora-api.onrender.com/health/live`, every
      5 minutes. Leave failure notifications on — with a 200 expected they become a real uptime
      alert.

Not `/health`: it probes Postgres, so pinging it every five minutes keeps Neon's compute awake
around the clock and eats the free compute-hour allowance. `/health/live` is registered with
`Predicate = _ => false`, so no check runs at all — it proves the process is serving and touches
nothing.

Not the root path either, which this checklist used to recommend. Root 404s, and cron-job.org counts
any non-2xx as a *failed* execution: it alerts every five minutes and disables jobs that keep
failing. A keep-warm ping that switches itself off is worse than none, because the app silently
returns to cold starts.

A free Render service sleeps after 15 minutes idle and takes 40–60s to wake, which reads as a broken
app. At 5-minute pings it never sleeps, using ~730 of the 750 free instance-hours a month — enough
for exactly one service.

## 8. Android signing key (~10 min)

- [ ] Generate it (any JDK provides `keytool`; this machine has one at
      `%LOCALAPPDATA%\Android\jdk\bin`):

```
keytool -genkeypair -v -keystore subvora-release.keystore -alias subvora \
  -keyalg RSA -keysize 2048 -validity 10000
```

- [ ] **Copy the keystore file and both passwords into a password manager, outside this repo.**
      Android identifies an app by its signing key. Lose this file and you can never ship an update
      to anyone who already installed the app — they have to uninstall, losing their local cache.
      `.gitignore` already blocks `*.keystore`/`*.jks`; keep it that way.
- [ ] Base64 it:

```
base64 -w0 subvora-release.keystore > keystore.b64
```

- [ ] GitHub repo → *Settings* → *Secrets and variables* → *Actions* → add four repository secrets:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | contents of `keystore.b64` |
| `ANDROID_KEYSTORE_PASSWORD` | store password you just chose |
| `ANDROID_KEY_ALIAS` | `subvora` |
| `ANDROID_KEY_PASSWORD` | key password you just chose |

- [ ] Delete `keystore.b64` and the local `subvora-release.keystore` once both are safely in the
      password manager and the secrets are set

## 9. Ship the app (~10 min)

- [ ] Tag and push:

```
git tag v1.0.0
git push origin v1.0.0
```

- [ ] Watch *Actions* → **Release Android APK**. It builds a signed APK and attaches it to a new
      GitHub Release.
- [ ] Download the APK and install on a real phone: `adb install -r <apk>`

## 10. Confirm on the phone (~10 min)

Do this with **no `adb reverse` mapping active** — that tunnel is for the local dev loop in
[debug/ANDROID_DEVICE.md](./debug/ANDROID_DEVICE.md) and would mask a broken production URL.

- [ ] Register or log in — proves the app reached Render over HTTPS
- [ ] Subscription list and dashboard populate
- [ ] Turn off wifi and mobile data: cached rows still render, with the cached-data indicator
- [ ] Add a subscription renewing inside its alert window, revisit the list screen, and confirm a
      notification is scheduled. Reminders only reschedule when the **list screen** loads — a known
      ceiling, not a bug.
- [ ] Keep `adb logcat -b crash` running throughout

---

## After you are live

- Pushes to `main` auto-deploy the API. Nothing to run.
- **Schema changes need the migration workflow run by hand** before the deploy that depends on
  them. Startup migration is off in `Production` on purpose.
- New app builds only happen on a `v*` tag.
- Watch Render's logs for OOM restarts — 512 MB is tight for ASP.NET Core. The fix is a paid
  instance, not a code change.
- Free-tier trade-offs you have accepted are listed at the end of [DEPLOYMENT.md](./DEPLOYMENT.md);
  the one users can notice is that a queued password-reset email is lost if the service restarts
  mid-send. They just request another.

## What this checklist deliberately does not buy

| Want | Cost | Consequence of skipping |
|---|---|---|
| iOS app | $99/yr Apple Developer | Android only |
| Google Play listing | $25 one-time | Users sideload an APK; also needs a privacy policy URL and a `SCHEDULE_EXACT_ALARM` declaration form |
| Custom domain | ~$10/yr | URL stays `*.onrender.com`, and changing it later means a new APK — the address is baked in at build time |
| No cold starts / more RAM | ~$7/mo Render | Keep-warm ping covers the cold start; RAM stays tight |

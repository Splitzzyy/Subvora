# Adding a Provider to the Subscription Catalog

The catalog is the list of known brands behind smart-match: type "netflx" when adding a
subscription and you get Netflix, its logo, and its category filled in for you.

Adding a brand is **one entry in one JSON file**. No migration, no C#, no id to invent.

---

## TL;DR

1. Add an entry to `src/SubVora.Infrastructure/Catalog/subscription-catalog.json`
2. Restart the API
3. Done — it's matchable immediately

---

## Step 1 — Find the icon slug

Logos come from [Simple Icons](https://simpleicons.org) (CC0) via the jsDelivr CDN. The slug is
usually the brand name lowercased with spaces and punctuation removed: `Adobe Creative Cloud` →
`adobecreativecloud`.

**Verify it before you commit it** — a wrong slug means a silently broken image in every user's
list:

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  https://cdn.jsdelivr.net/npm/simple-icons@13/icons/<slug>.svg
```

`200` means it exists. `404` means it doesn't — see Step 2.

## Step 2 — If there's no icon, use `null`

Simple Icons v13 removed a number of brands for trademark reasons. Disney+, Hulu, Xbox,
Microsoft and Google Workspace are all among them.

That is not a reason to leave the brand out. Matching works on the name; the logo is a bonus, and
the mobile subscription list already renders its placeholder when `catalogLogoUrl` is null.

```json
{ "providerName": "Disney+", "category": "Entertainment", "iconSlug": null }
```

## Step 3 — Add the entry

Open `src/SubVora.Infrastructure/Catalog/subscription-catalog.json` and append:

```json
{ "providerName": "Disney+", "category": "Entertainment", "iconSlug": "disneyplus" }
```

| Field | Rules |
|---|---|
| `providerName` | The canonical brand name, as a user would recognise it. Must be unique in the file — a duplicate is silently swallowed by `ON CONFLICT DO NOTHING`, so a test fails on it instead. |
| `category` | One of exactly: `Entertainment`, `Productivity`, `Fitness`, `Utilities`, `Finance`, `Food`, `Travel`, `Other`. A test fails on anything else. |
| `iconSlug` | A verified Simple Icons slug, or `null`. |

There is no `id` field. `provider_name` carries a unique index, so it is the key.

**Need a category that doesn't exist?** Add it to the system categories with a migration first —
`SeedFoodAndTravelCategories` is the pattern. Don't reach for `Other`: the dashboard's per-category
breakdown is the reason categories exist, and a bucket labelled "Other" tells the user nothing.

**Naming matters more than you'd think.** Trigram matching scores the name against what the user
types, so use the name people actually say. `HBO Max`, not `Warner Bros. Discovery HBO Max`. If a
brand is commonly known by two names, that is a genuine limitation — see
[Known limits](#known-limits).

## Step 4 — Apply it

**Locally:** restart the API. `SubscriptionCatalogSyncService` runs at startup, finds the new name
missing, inserts it, and logs:

```
Added 1 provider(s) to the subscription catalog.
```

**Deployed:** it happens on the next deploy, at process start. There is no separate migration step
and no downtime — the sync is a single insert against an already-running schema.

## Step 5 — Verify

```bash
curl -X POST http://localhost:5271/api/v1/subscriptions/resolve \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"input":"disney"}'
```

```json
{ "tier": "AutoFill", "providerName": "Disney+", "logoUrl": null, "categoryId": "..." }
```

`tier` tells you how confident the match was:

| Tier | Score | What the app does |
|---|---|---|
| `AutoFill` | ≥ 0.70 | Offers it, user confirms |
| `SuggestConfirm` | ≥ 0.50 | Offers it, user confirms |
| `Manual` | < 0.50 | Nothing — user types it themselves |

**No tier writes into the form on its own.** Despite its name, `AutoFill` is a confidence band, not a
licence to act: both matching tiers raise the same "Looks like X" chip and wait. Tapping **Use** is
what applies the match, and it applies all of it at once — name, category, logo and the catalog link
that goes on the save payload, including over a category the user had already picked. Ignore the
chip and the subscription saves exactly as typed, with no catalog link.

The tier still reaches the client on `SuggestedTier`, so the chip can be worded by confidence; what
it must not do is skip the confirmation. Anything the app fills in silently is something the user has
to notice and undo mid-edit, and a 0.99 match is still a guess about a field being typed into.

Run the tests too — they check the category name and the absence of duplicates:

```
dotnet test tests/SubVora.Infrastructure.Tests/SubVora.Infrastructure.Tests.csproj -c Release
```

---

## Troubleshooting

**The brand still isn't matching.**
Check the API logs at startup for the "Added N provider(s)" line. If the sync failed it logs an
error and moves on — the app stays up with a stale catalog by design. Then confirm the row landed:

```sql
SELECT provider_name, logo_url, category_id FROM subscription_catalog WHERE provider_name = 'Disney+';
```

**It matches, but the wrong brand wins.**
Two catalog names are competing for the same input. Check what actually scores highest:

```sql
SELECT provider_name,
       greatest(word_similarity('disney', provider_name),
                word_similarity(provider_name, 'disney')) AS score
FROM subscription_catalog ORDER BY score DESC LIMIT 5;
```

Ties resolve alphabetically, so `youtube` picking YouTube Music over YouTube Premium is expected,
not a bug.

**The logo doesn't render.**
Open the CDN URL in a browser. If it 404s, the slug is wrong — set it to `null` and move on rather
than leaving a broken image.

**Nothing happened after a deploy.**
The sync only inserts what's *missing*. If a row with that exact `provider_name` already exists —
including one a user created before the runtime write path was removed — it is left alone.

---

## Known limits

These are deliberate, not oversights.

**The sync only inserts. It never updates or deletes.**
So:

- **Renaming** a brand in the JSON creates a *second* row and leaves the old one behind.
- **Deleting** an entry from the JSON does nothing to the database.

Both are one-line SQL statements, and both should be done as a migration if the change needs to
reach every environment:

```sql
UPDATE subscription_catalog SET provider_name = 'Max' WHERE provider_name = 'HBO Max';
DELETE FROM subscription_catalog WHERE provider_name = 'Defunct Service';
```

Deleting is safe: `user_subscriptions.catalog_id` is `ON DELETE SET NULL`, so a user keeps their
subscription and its name, and only loses the logo.

**One name per brand — there are no aliases.**
`G Suite` will not find Google Workspace, and `MS Office` will not find Microsoft 365. Trigram
similarity scores characters, and those share almost none. Adding both spellings as separate rows
"works" but pollutes the catalog with two entries for one service. Proper alias support would need
an `aliases` column, or an LLM fallback on the sub-threshold path — see
`TECHNICAL_REQUIREMENTS.md` §8.

---

## What not to do

**Don't edit `SeedSubscriptionCatalog`.** That migration is frozen history for databases that
already ran it. Editing it changes nothing for them and desynchronises it from the JSON. The two
overlap by design — every name it inserted is already present, so the sync skips them.

**Don't hand-assign ids.** The `5eedca70-…` prefix in the old seed exists only because migrations
needed a stable id for their `Down`. New rows get `gen_random_uuid()`.

**Don't add a brand from user input at runtime.** `subscription_catalog` is global and unowned, so
anything written there is visible to every other user's fuzzy match. That path was removed
deliberately — a `Manual` result means "no catalog link", not "create one".

---

## How it works

```
subscription-catalog.json  (embedded resource)
        │
        ▼  at process start
SubscriptionCatalogSyncService
        │  SELECT existing provider_names   ← usually the only query
        │  INSERT ... ON CONFLICT (provider_name) DO NOTHING   ← only what's missing
        ▼
subscription_catalog  ──(pg_trgm word_similarity)──►  POST /subscriptions/resolve
```

No advisory lock: `ON CONFLICT DO NOTHING` makes two instances starting together harmless. No retry
loop: a failure costs a log line and the next start tries again. In the steady state it is one
`SELECT` that finds nothing to do.

A brand is matchable the instant the row lands, because with trigram scoring the row *is* the
index. That was not true of the previous embedding-based design, where a new row stayed invisible
until a network-backed backfill had run over it.

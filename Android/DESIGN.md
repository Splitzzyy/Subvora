# SubVora Material Expressive UI

React Native (Expo SDK 57, Expo Router) client for the same `/api/v1` backend the retired MAUI app used. Material 3 Expressive is applied by hand with React Native primitives: tonal surfaces, an expressive shape scale, springy press feedback and system appearance. No UI kit dependency.

## Brand

- **Renewal Ring** icon: master artwork `assets/brand/icon-master.svg`. `npm run icons` (`scripts/generate-icons.mjs`, `@resvg/resvg-js` devDependency) renders the legacy icon, the adaptive background/foreground (scaled 0.88 for the 66dp safe zone)/monochrome layers, the Android 12+ splash icons (one for midnight, one for light backgrounds), the notification small icon (ring and orb only, inside the 20dp live area) and `assets/brand/store/play-store-512.png`. All are wired through `app.json`; the native folders stay generated. Android only: no iOS or web assets.
- Splash: follows the system appearance. It is midnight `#0B0A1F` with the mark in dark mode, and the light surface with the violet-on-light mark in light mode, so a light phone never flips from dark to light. It hides once stored tokens are read, or after 3 s, when `SubVoraLoader` takes over. `LaunchHandoff` (`useSplashLook`) then redraws the same mark, sweeps the arc closed and fades into the first route.
- `plugins/with-brand-accent.js` sets the native theme's `colorAccent` and `colorPrimary` from the generated palette (light and night), so the one remaining system dialog, the date picker, uses the brand violet instead of AppCompat teal.

## Tokens (`src/theme/index.tsx`)

- **Colour:** `npm run palette` (`scripts/generate-palette.mjs`, build-time `@material/material-color-utilities`, Expressive variant, 2025 spec) writes the committed `src/theme/palette.generated.ts` from the key colours violet `#7B5CFF`, cyan `#38C6FF`, amber-orange `#FF8A4C` (renewal warnings only) and midnight `#0B0A1F`. That file holds every M3 role in light and dark, plus semantic roles harmonised to primary: `renewalWarning` (overdue), `renewalSoon` (also the warning banner via the `warningContainer` alias), `success`, and `spendUp`/`spendDown` (aliases of the first and last). It also holds the brand colours and the fixed category/payment identities. Each identity has `color`, `soft` and `ink`: a tone of the same hue that reads at 4.5:1 on its wash, used for tile glyphs. Red stays reserved for errors. `tests/contrast.test.ts` checks every text/background pair in both schemes against WCAG AA.
- **Wallpaper colours** (Android 12+ `system_accent*` for the accent roles) are **off by default**, so users see the brand scheme first. The preference is stored as `wallpaper`.
  - **Contrast:** wallpaper pairs are not covered by the contrast test. The OS guarantees its own `system_accent*` pairs, and that holds only while every pairing stays inside the dynamic set: accent role on accent role. Never put a dynamic accent on a fixed surface role.
  - **Plain colours only where unavoidable:** `theme.hex` gives the explicit palette as plain strings, only for what cannot take a native colour. That means the system UI background and SVG gradient stops.
  - **Wallpaper-aware drawing:** the switch thumb, pull-to-refresh and the wavy progress stroke take `colors.*` directly. With wallpaper colours on, `RingMark` draws a solid wallpaper-primary arc instead of its gradient.
- **Type:** Plus Jakarta Sans (OFL, `assets/fonts`), embedded by the `expo-font` plugin. Each style names its weight's family and never sets `fontWeight`. The scale is the full M3 display/headline/title/body/label × L/M/S, plus a `…Emphasized` variant of each. Money uses `amount*` styles with `tabular-nums`; the font ships `tnum`.
- **Shape:** `extraSmall 8, small 12, medium 16, large 24, extraLarge 32, full`, plus `listInner 6` and `listGap 2` for the segmented list. The old `xl`/`xxl` names were migrated, not aliased: cards use `large`; sheets, dialogs and the hero use `extraLarge`.
- **Spacing** `space.*` on a 4dp grid. **Motion:** the M3 Expressive spring tokens (`spatialFast/Default/Slow`, `effectsFast/Default/Slow`), converted for `Animated.spring`. Reanimated is not used. Reduced motion (`src/lib/motion.ts`) turns every spring and slide into a fade.

## Shared UI

- `Tap`: ripple plus a spring. Buttons and the FAB morph from pill to rounded square on press. Anything else scales on the native driver, and list rows don't scale at all.
- `Button`: `loading` keeps the label and shows an inline indicator; there is a `textDanger` variant for dialogs. Disabled follows M3: content at 38% on a quiet container (outlined keeps a faint outline), so it cannot pass for a live tonal button.
- `IconButton`, extended `Fab` (tells the snackbar while it is on screen).
- `ButtonGroup`: a change morphs: inner corners spring to full while fills and labels cross-fade. `onPrimaryContainer` inverts the selected segment, which a `primary` fill would lose on a primary card, and draws the others as a wash over the card (ink at 12% in light, black at 20% in dark; lightening the dark card fails 4.5:1). Labels wrap rather than truncate.
- `ListItem` + `positionOf`: `actions` exposes a row's inner buttons to TalkBack, since a pressable row is a single focus stop.
- `TextField`: errors are announced and included in the field's spoken label.
- Also: `Card`, `Banner` (always icon + text), `Pill` (optional icon), `Chip`, `ProgressBar` (no value: wavy indeterminate variant), `Tile` (monogram; provider logos are not loaded), `EmptyState` (ring motif). Every control reaches a 48dp touch target.
- `src/ui/brand.tsx`: `RingMark`, `SubVoraMark` (mark only, `monochrome` option), `SubVoraLoader` (full-screen/first-load waits), `ExpressiveLoadingIndicator` (local stand-in for Compose's LoadingIndicator: a rounded square morphing to a circle while it turns) and `LaunchHandoff`.
- `src/ui/states.tsx`: `AsyncView` (loading / content / empty / error / offline), with `useDelayedLoading` (150 ms delay, 300 ms minimum; rules in `src/lib/loading.ts`). It also holds the layout-shaped skeletons (opacity shimmer), `ErrorState` (Retry, with "More info" showing the status in plain language) and `OfflineState`.
- `src/ui/snackbar.tsx`: one app-wide queue for write results, shown one at a time in order, so a success never replaces an unread failure.
  - **Placement:** it sits directly above the keyboard if open, else the navigation bar, plus a FAB only while one is on screen (`src/ui/obstructions.ts`).
  - **Announcing:** it is announced once, through a live region.
  - **Failures** stay until dismissed.
  - **Undo** appears for renames and the home currency, where the same call reverses the change. An undo that cannot run yet says so.
- `ConfirmDialog` / `useConfirm()` (in `sheets.tsx`): the M3 basic dialog used for every confirmation: deletes, sign-out and discarding unsaved edits. The native `Alert` is not used, because the OEM themes it. Dialogs grow in from 90% on the spatial spring, dismiss on a scrim tap, and keep their content while fading out.
- `src/ui/layout.tsx`: `Screen` has an optional large collapsing top app bar that runs under the status bar, and an optional fixed `header`. The add/edit form puts *Save* there, beside *Close*, as an M3 full-screen dialog does. `AuthScaffold` is the signed-out layout. The tab bar is a custom M3 navigation bar that becomes a navigation rail at ≥600dp. Its indicator grows out from the icon and fades (native driver), and destinations fade into each other rather than slide.
- `src/ui/sheets.tsx`: `Sheet` (the scrim fades while only the sheet rises; drag the handle or header down to close), `ActionSheet` (keeps its last title and actions while it slides away), `PickerSheet` (opens scrolled to the current choice), `PromptDialog`, `ConfirmDialog`, `DateField`.
- Approximations: stack transitions use native-stack `fade_from_bottom`/`slide_from_right` in place of shared-axis (a fade with reduced motion). The pull-to-refresh indicator is the native Android one, tinted from tokens. The dashboard ring shows the biggest category's share of monthly spend; the orb is decorative, because the dashboard has no next-billing data without an extra load.
- `src/lib/haptics.ts`: semantic Android haptics, best effort.
- Two Android quirks the components work around:
  - **Lost corner radius.** A view whose background switches from none to a colour loses its corner radius, so it draws square. The nav indicator keeps its colour and fades in, and unselected chips sit on the card colour instead of `transparent`. Follow the same pattern for any new toggle.
  - **Large text.** Display and headline styles scale with the system font size only up to 1.3x, glyphs and line height together. Nav labels shrink to fit, and section headers wrap.

## Preserved behaviour (from the MAUI client)

- **Auth:** JWT with a single-flight refresh on 401. The server rotates refresh tokens and treats a replay as theft. An unreachable refresh does not end the session; only a refusal does. Login's own 401 means "Invalid email or password". Register is followed by a login, because register answers 202 either way. Change-password stores the new token pair it returns. Sign-out revokes on a best-effort basis, then always clears tokens, the mirror and reminders.
- **Offline:** the dashboard and the subscription list fall back to the last successful GET, held in an `expo-sqlite/kv-store` mirror with its save time. The banner says "Offline — showing data from …" only when nothing answered. A server error with the phone online reads "Couldn't refresh" and offers Retry. **Offline writes are not supported** (#144). Write buttons are disabled with no network, and a failed write says that it was not saved.
- **Loading:** each screen loads on first focus, then again only after a write anywhere bumps `dataVersion`. Tab switches never refetch.
- **Subscriptions:** grouped by category, with groups and rows ordered by the soonest billing date. Rows show an overdue chip and *Mark as paid*. The server moves the date one cycle on from the date that was paid. Writes are gated by a busy flag, so a double tap cannot advance the date twice.
- **Add/edit:** provider matching is debounced (500 ms) and drops stale answers. A suggestion is only offered and is never applied until tapped. The next billing date is derived from the purchase date and the cycle, and a date picked by hand stands. A 409 reloads the record and says so. New subscriptions start in the user's home currency. Save explains missing fields instead of greying out. Leaving with unsaved changes asks first.
- **Reminders:** scheduled on the device with `expo-notifications` (09:00, `alertDaysAdvance` before the date, nearest 64, full replace on each sync). Permission is asked right after sign-in.
- **Money:** each amount is shown with the symbol for *its own* currency code, never the device locale's.

## Verification

```sh
npm run typecheck
npm run lint
npm test                      # pure rules, loading timing, WCAG contrast
npx expo install --check
npx expo export --platform android --output-dir <tmp>
```

The bundle export checks module resolution and Hermes compilation. It is not a device test. Device acceptance is still required: sign in, register, forgot/reset password, add/edit/delete a subscription, check provider suggestions, mark paid, run category and payment CRUD, check the dashboard breakdowns, switch the theme and wallpaper colours, pull to refresh, go offline (cached banner, disabled writes), check reminder delivery and sign out. Also check sheets with Android back, keyboard, large text, TalkBack and reduced motion.

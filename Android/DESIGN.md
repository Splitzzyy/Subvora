# SubVora Material Expressive UI

React Native (Expo SDK 57, Expo Router) client for the same `/api/v1` backend the retired MAUI app used. Material 3 Expressive is applied by hand with React Native primitives: tonal surfaces, an expressive shape scale, springy press feedback and system appearance. No UI kit dependency.

## Shared UI

- `src/theme/index.tsx`: M3 tonal roles for light and dark, seeded from the MAUI brand violet (`#512BD4`). It also holds the type scale (emphasised display and headline weights), the shape scale (`xl` 28 and `xxl` 36 for cards and sheets) and the category and payment-source identities (fixed hue and icon per seeded category, neutral for anything else, red reserved for errors).
- `ThemeProvider`: System/Light/Dark preference plus **wallpaper colours**. On Android 12+ the accent roles (`primary`, `primaryContainer`, `secondaryContainer`, `tertiaryContainer` and their `on*` pairs) map to `@android:color/system_accent*` resources. Surfaces, status colours and category hues keep the explicit palette. Native colour objects never go into string concatenation. Preferences live in the device's kv-store and are read synchronously, so the first frame already has the right appearance.
- `Tap`: native ripple plus a small spring. With a radius pair it **morphs shape on press** (pill → rounded square), as Expressive buttons do. Honours reduced motion.
- `Button` (filled / tonal / outlined / text / danger, `lg` for primary actions), `IconButton`, extended `Fab`.
- `ButtonGroup`: connected button group. Segments share small inner corners, and the selected one rounds fully and fills, so the selection shows through shape as well as colour. Used for the dashboard period, theme and payment-source type.
- `ListItem` + `positionOf`: segmented list. Each run of rows gets 24dp outer corners and 6dp inner corners, with 2dp gaps between rows.
- `Card` tones, `Banner` (error / warning / info / success, always with an icon and text, never colour alone), `Pill`, `Chip`, `ProgressBar` (rounded with a gap before the track), `Tile` (squircle letter or icon), `EmptyState`.
- `src/ui/sheets.tsx`: `Sheet` (modal bottom sheet, Android back dismisses it, and the backdrop is a sibling so scrolling inside never closes it), `ActionSheet`, searchable `PickerSheet`, `PromptDialog` (Android has no `Alert.prompt`) and `DateField` (native Material date dialog). Confirmations use `Alert.alert`, which renders as a Material dialog on Android.
- `src/lib/haptics.ts`: semantic Android haptics (`Segment_Tick`, `Confirm`, `Reject`). Best effort, so a failure can never block an action.

## Preserved behaviour (from the MAUI client)

- **Auth:** JWT with a single-flight refresh on 401. The server rotates refresh tokens and treats a replay as theft. An unreachable refresh does not end the session; only a refusal does. Login's own 401 means "Invalid email or password". Register is followed by a login, because register answers 202 either way. Change-password stores the new token pair it returns. Sign-out revokes on a best-effort basis, then always clears tokens, the mirror and reminders.
- **Offline:** the dashboard and the subscription list fall back to the last successful GET, held in an `expo-sqlite/kv-store` mirror. **Offline writes are not supported** (#144). Write buttons are disabled with no network, and a failed write says that it was not saved.
- **Loading:** each screen loads on first focus, then again only after a write anywhere bumps `dataVersion`. Tab switches never refetch.
- **Subscriptions:** grouped by category, with groups and rows ordered by the soonest billing date. Rows show an overdue chip and *Mark as paid*. The server moves the date one cycle on from the date that was paid. Writes are gated by a busy flag, so a double tap cannot advance the date twice.
- **Add/edit:** provider matching is debounced (500 ms) and drops stale answers. A suggestion is only offered and is never applied until tapped. The next billing date is derived from the purchase date and the cycle, and a date picked by hand stands. A 409 reloads the record and says so.
- **Reminders:** scheduled on the device with `expo-notifications` (09:00, `alertDaysAdvance` before the date, nearest 64, full replace on each sync). Permission is asked right after sign-in.
- **Money:** each amount is shown with the symbol for *its own* currency code, never the device locale's.

## Verification

```sh
npm run typecheck
npm run lint
npm test                      # pure rules: dates, money, grouping, reminders
npx expo install --check
npx expo export --platform android --output-dir <tmp>
```

The bundle export checks module resolution and Hermes compilation. It is not a device test. Device acceptance is still required: sign in, register, forgot/reset password, add/edit/delete a subscription, check provider suggestions, mark paid, run category and payment CRUD, check the dashboard breakdowns, switch the theme and wallpaper colours, pull to refresh, go offline (cached banner, disabled writes), check reminder delivery and sign out. Also check sheets with Android back, keyboard, large text, TalkBack and reduced motion.

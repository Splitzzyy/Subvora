# SubVora Android (React Native / Expo)

A Material 3 Expressive Android client for the SubVora API. It uses the same `/api/v1` endpoints, auth flow and rules as the MAUI app in `src/SubVora.Mobile`. Design notes are in [DESIGN.md](DESIGN.md).

```
src/app/          Expo Router screens: (auth) login/register/forgot-password, (tabs) home/subs/categories/payment/settings, subscription/[id]
src/api/          fetch client (tokens, single-flight refresh, error wording) + typed endpoints
src/lib/          logic.ts (pure rules, unit-tested), session, loaders, offline mirror, reminders, haptics
src/theme/        M3 Expressive tokens + ThemeProvider (system/light/dark, wallpaper colours)
src/ui/           shared components, sheets, layout
tests/            node --test against src/lib/logic.ts
```

## Run

```sh
cd Android
npm install
npx expo run:android          # dev build on a device or emulator (needs a JDK + Android SDK)
```

The API defaults to the deployed `https://subvora-api.onrender.com`. To use a local API (see `docs/debug/ANDROID_DEVICE.md`):

```sh
adb reverse tcp:8080 tcp:8080
EXPO_PUBLIC_API_URL=http://localhost:8080 npx expo run:android
```

Debug builds allow cleartext HTTP. Release builds expect HTTPS.

## Release

Push a tag `rn-vX.Y.Z` (for example `git tag rn-v1.0.0 && git push origin rn-v1.0.0`). `.github/workflows/release-android-rn.yml` then:

1. Generates the native project.
2. Builds a release APK with the JS bundle embedded, for arm64 and armv7.
3. Re-signs it with the production keystore.
4. Attaches it to the GitHub Release.

It reads `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` from the `production` environment. These are the same secrets `release-android.yml` uses.

## Check

```sh
npm run typecheck && npm run lint && npm test
```

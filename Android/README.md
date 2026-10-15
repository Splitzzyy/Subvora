# SubVora Android (React Native / Expo)

A Material 3 Expressive Android client for the SubVora API. This is the only mobile client: all mobile work happens here. It replaces the retired MAUI app (`src/SubVora.Mobile`, reference only) under the same application id, `com.subvora.mobile`, and signing key, so MAUI installs upgrade to it in place. See *Upgrading from the MAUI app* in `docs/DEPLOYMENT.md`. Design notes are in [DESIGN.md](DESIGN.md).

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

The API defaults to the deployed `https://subvora-api.onrender.com`.

### Against a local API on a physical phone

```sh
docker compose up -d --build                     # repo root: API :8080, Postgres, Mailpit :8025
adb reverse tcp:8080 tcp:8080                    # phone's localhost:8080 -> your PC's API
adb reverse tcp:8081 tcp:8081                    # phone's localhost:8081 -> Metro
EXPO_PUBLIC_API_URL=http://localhost:8080 npx expo run:android
```

- Debug builds allow cleartext HTTP; release builds expect HTTPS.
- A phone with a release build (MAUI or React Native) installed refuses a debug build over it (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`, different signing key). Uninstall it first. The debug build uses the same `com.subvora.mobile` id.
- `adb reverse` mappings drop whenever the cable or USB connection resets. The symptom is "You appear to be offline." Re-run both commands.
- Reset codes and other mail land in Mailpit at <http://localhost:8025>, not a real inbox.
- `EXPO_PUBLIC_API_URL` is inlined when Metro bundles. After changing it, restart Metro with `--clear`.
- Xiaomi/MIUI phones refuse `adb install` (`INSTALL_FAILED_USER_RESTRICTED`) until *Developer options → Install via USB* is on, which needs a Mi account. Another option is to `adb push` the APK to `/sdcard/Download` and install it from the file manager.

## Release

Push a tag `vX.Y.Z` from `main` (the first React Native release is `v2.0.0`: `git tag v2.0.0 && git push origin v2.0.0`). `.github/workflows/release-android.yml` then:

1. Generates the native project.
2. Builds a release APK with the JS bundle embedded, for arm64 and armv7.
3. Re-signs it with the production keystore.
4. Attaches it to the GitHub Release.

It reads `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` from the `production` environment.

## Check

```sh
npm run typecheck && npm run lint && npm test
```

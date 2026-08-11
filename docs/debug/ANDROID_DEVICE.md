# Running and debugging SubVora.Mobile on a physical Android phone

How to get the MAUI app onto a real handset, pointed at a locally-running API, and how to read what
it tells you when it breaks. Written against a Xiaomi Redmi (MIUI 14 / Android 13) — the MIUI
sections are the only vendor-specific parts, everything else applies to any Android device.

The emulator is not covered here: its `10.0.2.2` host alias is already the Debug default in
`SubVora.Mobile.csproj`, so `dotnet build -t:Run` works with no extra steps. A physical phone needs
all of the below because `10.0.2.2` means nothing to it.

---

## Everyday situations — what happened, what to do

The rest of this document is the detail. This section is the day-to-day loop. Sections 1 and 2 are
one-time setup; you should not need them again once the phone is working.

### I changed mobile code (XAML, a view-model, anything in `SubVora.Mobile`)

Run this. It rebuilds, reinstalls over the app that is already there, and relaunches it. Usually
10-20 seconds. You stay logged in and the local cache is kept.

```powershell
dotnet build src/SubVora.Mobile/SubVora.Mobile.csproj -f net10.0-android -t:Run `
  -p:ApiBaseAddress=http://localhost:8080/ `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk"
```

### I changed API code (`SubVora.Api`, `Application`, `Infrastructure`, `Domain`)

```powershell
docker compose up -d --build api
```

**`--build` is the important part.** Without it Docker reuses the old image, your change is not
running, and you will spend a while debugging code that was never deployed. Migrations re-run by
themselves when the container starts.

### I changed both

API first, then mobile. Nothing else.

### I unplugged the USB cable and plugged it back in

```powershell
adb reverse tcp:8080 tcp:8080
```

That is all. The app is still installed and does not need rebuilding. This one mapping is the only
thing lost, and losing it is quiet: the phone still resolves `localhost:8080` — to itself — so calls
fail with a connection error rather than anything that mentions the cable.

### I restarted the phone

Same as above: re-run `adb reverse`. If you had set up wireless ADB, re-run `adb connect` too.

### I restarted my machine

```powershell
docker compose up -d
adb reverse tcp:8080 tcp:8080
```

### The app opens but nothing loads

Work outwards from the phone:

```powershell
adb shell "curl -s http://localhost:8080/health"   # phone -> your machine. Expect: Healthy
curl http://localhost:8080/health                  # your machine -> API. Expect: Healthy
docker compose ps                                  # api and db both Up
```

The first command failing while the second works means the tunnel is down — re-run `adb reverse`.

### The app crashes

```powershell
adb logcat -c        # clear old noise first, then reproduce the crash
adb logcat -b crash -v brief -d | Select-String -Pattern "FATAL|Exception|Process:" | Select-Object -First 20
```

That prints the real exception message. See §7 for more and §8 for ones already met.

### I am signed out, or my login stopped working

Register again. This is normal after the database has been cleared — an access token stays
signature-valid until it expires, so the app can look logged in while every call fails against a user
that no longer exists.

### Something looks stale even though the build succeeded

```powershell
dotnet clean src/SubVora.Mobile/SubVora.Mobile.csproj -f net10.0-android
```

Then build again. Worth trying after changing the `.csproj`, adding a package, or adding an image or
icon.

### Before you deploy a mobile change

```powershell
dotnet test tests/SubVora.Mobile.Tests/SubVora.Mobile.Tests.csproj
```

Fast, and it catches view-model mistakes. It cannot see XAML, Shell navigation, or DI lifetimes —
both crashes in §8 passed a green test run. Open the app on the phone before calling a UI change done.

---

## 1. One-time machine setup

`dotnet workload list` should show `maui-android`. If it doesn't:

```powershell
dotnet workload install maui-android
```

The Android SDK and a JDK are separate from the workload. You do **not** need Visual Studio or
Android Studio — the Android SDK targets can fetch both:

```powershell
dotnet build src/SubVora.Mobile/SubVora.Mobile.csproj -f net10.0-android `
  -t:InstallAndroidDependencies `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk" `
  -p:AcceptAndroidSDKLicenses=True
```

Add `%LOCALAPPDATA%\Android\Sdk\platform-tools` to `PATH` so `adb` is available. Every build command
below passes `AndroidSdkDirectory` / `JavaSdkDirectory` explicitly, so they work even if you skip the
`PATH` step.

---

## 2. One-time phone setup

Settings → About phone → tap **Build number** seven times → back → **Developer options**.

| Setting | Why |
|---|---|
| USB debugging | Lets `adb` see the device at all |
| **Install via USB** | MIUI blocks ADB installs without it |
| **USB debugging (Security settings)** | MIUI needs *this one too* — the previous toggle alone is not enough |
| Turn off MIUI optimization | Frequently required before ADB installs behave; reboots the phone |
| USB mode = File transfer (MTP) | MIUI refuses installs in charging-only mode |

MIUI demands a signed-in Mi account plus a live data connection before it will let you enable the two
install toggles. Verify the phone is visible:

```powershell
adb devices     # must print your serial followed by "device", not "unauthorized"
```

`unauthorized` means the RSA fingerprint prompt on the phone was missed or dismissed — unplug,
replug, and accept it.

---

## 3. Start the backend

```powershell
docker compose up -d
```

This serves the API over **plain HTTP on `localhost:8080`**. Use Docker rather than `dotnet run`:
`dotnet run` serves HTTPS on port 7198 with a self-signed development certificate, which a phone will
refuse and which is a great deal of work to make it trust.

The first `up` needs `src/SubVora.Api/appsettings.Development.json` to exist. Copy the example if you
have not already — Docker mounts it read-only as `appsettings.Docker.json`, and without the file it
will helpfully create a *directory* there and the API will fail to start:

```powershell
Copy-Item src/SubVora.Api/appsettings.Development.example.json src/SubVora.Api/appsettings.Development.json
```

Migrations run automatically on startup for the `Development` and `Docker` environments
(`Program.cs`), so there is no `dotnet ef database update` step for local work. Only deployed
databases need the explicit `db-migrate` workflow.

Changed API code? The image is not rebuilt by a plain `up`:

```powershell
docker compose up -d --build api
```

Check it is alive from the host:

```powershell
curl http://localhost:8080/health      # -> Healthy
```

---

## 4. Give the phone a route to your machine

`10.0.2.2` is an **emulator-only** alias. Two options for real hardware; the first is better.

### adb reverse (recommended)

```powershell
adb reverse tcp:8080 tcp:8080
```

The phone's own `localhost:8080` now tunnels over USB to your machine. No firewall rules, no IP to
look up, and it keeps working when you move between networks. **Re-run it after every unplug** — the
mapping does not survive a disconnect.

Confirm from the device itself, which tests the exact path the app will use:

```powershell
adb shell "curl -s http://localhost:8080/health"     # -> Healthy
```

### LAN (fallback)

Find your IPv4 with `ipconfig`, use `http://192.168.x.x:8080/` as the base address below, open port
8080 in Windows Firewall, and keep the phone on the same Wi-Fi.

---

## 5. Cleartext HTTP

Android 9+ refuses cleartext HTTP by default, which blocks every call to a locally-run API. Since the
alternative is making a phone trust a self-signed dev certificate, Debug builds carry an exemption:

- `Platforms/Android/AndroidManifest.Debug.xml` — sets `android:usesCleartextTraffic="true"`
- `SubVora.Mobile.csproj` — merges it via `AndroidManifestOverlay`, `Configuration == Debug` only

Release builds keep the platform default and still refuse cleartext. If you ever see
`CLEARTEXT communication to … not permitted`, you are running a Release build or the overlay has been
removed.

---

## 6. Build, install, launch

```powershell
dotnet build src/SubVora.Mobile/SubVora.Mobile.csproj -f net10.0-android -t:Run `
  -p:ApiBaseAddress=http://localhost:8080/ `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk"
```

`-t:Run` builds, installs, and launches on the connected device.

`ApiBaseAddress` is an MSBuild property baked into assembly metadata at build time and read by
`Api/ApiConfig.cs`. Override it per build — never edit the default in the `.csproj` to point at your
machine, because that default is what everyone else's build uses too.

### Sideloading, when MIUI will not allow ADB installs

New Mi accounts sometimes sit in a waiting period before the install toggles unlock. The normal
package-installer path is not restricted:

```powershell
dotnet build src/SubVora.Mobile/SubVora.Mobile.csproj -f net10.0-android -c Debug `
  -p:ApiBaseAddress=http://localhost:8080/ `
  -p:EmbedAssembliesIntoApk=true `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk"

adb push src\SubVora.Mobile\bin\Debug\net10.0-android\com.subvora.mobile-Signed.apk /sdcard/Download/
```

Then open Files → Download → tap the APK. `EmbedAssembliesIntoApk=true` is not optional here: without
it a Debug APK expects the fast-deploy channel and crashes on launch when started standalone.

---

## 7. Debugging

### Reading crashes

MAUI surfaces managed exceptions through the Java bridge, so the useful line is buried. The dedicated
crash buffer is the fastest way in:

```powershell
adb logcat -b crash -v brief -d | Select-String -Pattern "FATAL|Exception|Process:" | Select-Object -First 20
```

That prints the exception type and message. For the stack:

```powershell
adb logcat -b crash -v brief -d | Select-Object -Last 40
```

Clear the buffer first so you are reading *this* run and not an earlier one:

```powershell
adb logcat -c
```

Live app output:

```powershell
adb logcat -s DOTNET:* mono-stdout:* MonoDroid:*
```

### Two crashes that look alike and are not

Both kill the app on launch during local debugging, and the fix is completely different.

**1. Fast deployment — a tooling problem, not a bug.**

```
Abort message: 'No assemblies found in
'/data/user/0/com.subvora.mobile/files/.__override__/arm64-v8a' or '<unavailable>'.
Assuming this is part of Fast Deployment. Exiting...'
```

The APK was installed without the assemblies inside it and the fast-deploy channel never pushed
them. Rebuild with `-p:EmbedAssembliesIntoApk=true` (see the sideloading section above), or deploy
through `dotnet build -t:Run` so the same build does both halves. Nothing in the app is wrong.

**2. An unreachable API — historically a real crash.**

```
FATAL EXCEPTION: main
android.runtime.JavaProxyThrowable: [Refit.ApiRequestException]: Connection failure
  at SubVora.Mobile.ViewModels.DashboardViewModel+<LoadAsync>d__44.MoveNext
  at CommunityToolkit.Mvvm.Input.AsyncRelayCommand+<AwaitAndThrowIfFailed>
```

This is what a Debug build on a physical phone does when `adb reverse` is not set up: the default
`10.0.2.2` is an emulator alias, so nothing answers.

The app should now show the offline state instead. If you see this trace again, the exception type
has escaped `ApiErrorMapper.IsApiFailure` — Refit wraps connection failures in `ApiRequestException`
rather than letting `HttpRequestException` through, and anything the filter does not match escapes
every view model's catch block and gets rethrown on the UI thread by `AsyncRelayCommand`. Widen the
filter; do not add a catch to the individual screen.

Ignore `monodroid-assembly: open_from_bundles: failed to load bundled assembly …` — that is normal
fast-deploy noise on Debug builds, not an error.

### Screenshots

```powershell
adb exec-out screencap -p > screen.png
```

A fully black image usually means the screen is off, not that the app crashed. Check with:

```powershell
adb shell dumpsys power | Select-String "mWakefulness="
adb shell pidof com.subvora.mobile        # a pid means the app is alive
```

MIUI blocks `adb shell input keyevent`, so you cannot wake or drive the phone from the host — unlock
it by hand. Expect `SecurityException: Injecting input events requires … INJECT_EVENTS`.

### Inspecting the database

```powershell
docker compose exec db psql -U subvora -d subvora_dev -c "SELECT email, preferred_currency FROM users;"
```

Reset codes and outbound mail land in Mailpit at <http://localhost:8025> — registration and
forgot-password deliberately reveal nothing over the API, so this is how you read them locally.

---

## 8. Problems already hit, and what they meant

| Symptom | Cause |
|---|---|
| `INSTALL_FAILED_USER_RESTRICTED: Install canceled by user` | MIUI install toggles off — §2. Nothing to do with your code |
| `Global routes currently cannot be the only page on the stack` | A page reached by `//Route` must be declared in `AppShell.xaml`, not registered with `Routing.RegisterRoute` |
| `The 'InnerHandler' property must be null` when switching tabs | A `DelegatingHandler` registered as a singleton and attached to more than one Refit client. Handlers must be transient; shared state belongs in `SessionRefresher` |
| App shows a blank coloured top bar | Something set `Shell.TitleView` — Shell hides the page title whenever one is present |
| A second tab strip above the page | Bare `ShellContent` children of a `TabBar`. Wrap each in a `<Tab>` |
| Currency still shows USD after a change | The API image is stale. `docker compose up -d --build api` |
| Login works, every other call 401s | The account was deleted from under a still-valid JWT. Access tokens are stateless and stay signature-valid until they expire |

---

## 9. Things worth knowing

- **The `Add` toolbar button on Subscriptions** is how you create one; there is no floating button.
- **Swipe a row left** to delete on the subscriptions and payment-sources lists.
- **Payment sources are optional.** An empty picker on the add-subscription form means you have not
  created any on the Payment tab — not that loading failed.
- **`SubVora.Mobile.Tests` runs on Windows only** and covers view-models, not XAML or Shell
  navigation. Every crash in the table above got through a green test run, so exercise the app on the
  device before calling a UI change done.
- **Building `SubVora.slnx` as a whole** needs the Android SDK. Build `SubVora.Api.csproj` directly
  when you are not working on mobile.

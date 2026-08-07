# Running and debugging SubVora.Mobile on a physical Android phone

How to get the MAUI app onto a real handset, pointed at a locally-running API, and how to read what
it tells you when it breaks. Written against a Xiaomi Redmi (MIUI 14 / Android 13) — the MIUI
sections are the only vendor-specific parts, everything else applies to any Android device.

The emulator is not covered here: its `10.0.2.2` host alias is already the Debug default in
`SubVora.Mobile.csproj`, so `dotnet build -t:Run` works with no extra steps. A physical phone needs
all of the below because `10.0.2.2` means nothing to it.

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

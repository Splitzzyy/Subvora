# Android App Deploy — Hinglish Guide

SubVora ka Android app (React Native / Expo, `Android/` folder) kaise banta hai, kya-kya chahiye, aur
release kaise nikalte hain — GitHub Releases (APK) aur Google Play (AAB) dono ke liye.
Ye doc [DEPLOYMENT.md](./DEPLOYMENT.md) ka Hinglish version hai, sirf **Android wale hisse** par focus
karke. Poora infra runbook (Neon, Render, Brevo) original doc mein hai — koi farak dikhe to wahi sahi
maana jaaye.

Scope: **sirf Android.** iOS ka koi free distribution path nahi hai — Apple Developer Program
$99/saal. Code mein aisa kuch nahi hai jo baad mein iOS add karne se roke.

Purana .NET MAUI client (`src/SubVora.Mobile`) retired hai — na build hota hai, na release.

---

## 1. Ek nazar mein — poora flow

```
version bump (Android/app.json)  →  PR merge  →  main se tag v2.x.y  →  push tag
                                                       ↓
                                  GitHub Actions (release-android.yml)
                                    ├─ Node 24 + Java 17 setup, npm ci
                                    ├─ keystore (base64 secret) → file
                                    ├─ signing secrets verify  ← yahan fail hua to 5 sec mein pata
                                    ├─ expo prebuild (android/ generate) + versionCode/versionName set
                                    ├─ gradlew assembleRelease bundleRelease  (APK + AAB)
                                    ├─ APK: apksigner se sign
                                    ├─ AAB: debug signature hatao, jarsigner se sign, 1 signer check
                                    └─ GitHub Release banao + APK aur AAB attach
                                                       ↓
                       APK → users sideload          AAB → Play Console mein upload (haath se)
```

Aap sirf **tag push** karte ho. Baaki sab workflow karta hai. Play upload abhi manual hai.

---

## 2. Kya-kya chahiye

### Accounts / access

| Cheez | Kyun chahiye |
|---|---|
| GitHub repo par **admin** | secrets add karne aur `production` environment banane ke liye |
| `keytool` (koi bhi JDK ke saath aata hai) | signing keystore generate karne ke liye |
| Deployed API (Render + Neon + Brevo) | app deployed API se baat karta hai — API live honi chahiye |
| Google Play Console ($25 one-time) | sirf Play Store par daalne ke liye |

### Local machine (agar khud build karna ho)

```
cd Android && npm ci && npm run typecheck && npm run lint && npm test
npx expo prebuild --platform android && cd android && ./gradlew assembleRelease
```

Node, JDK 17 aur Android SDK chahiye. `Android/android/` generated hai aur gitignored — haath se edit
mat karo, native config `Android/app.json` aur config plugins se jaata hai.

### GitHub secrets — 4 signing secrets

Ye `production` environment mein hone chahiye (repo-level par nahi, warna workflow ko dikhenge hi
nahi kyunki job `environment: production` use karti hai):

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | `base64 -w0 subvora-release.keystore` ka output |
| `ANDROID_KEYSTORE_PASSWORD` | `keytool` waala store password |
| `ANDROID_KEY_ALIAS` | `subvora` |
| `ANDROID_KEY_PASSWORD` | key password — **store password ke barabar hona chahiye** (neeche dekho) |

---

## 3. One-time setup: signing keystore

Ye sirf **ek baar** karna hai. Ho chuka hai — wahi keystore MAUI releases ne bhi use kiya tha.

```
keytool -genkeypair -v -keystore subvora-release.keystore -alias subvora \
  -keyalg RSA -keysize 2048 -validity 10000
```

### ⚠️ Keystore ka backup — ye sabse important cheez hai

**Is file ko repo ke bahar, kisi durable jagah backup karo.** Android app ki pehchaan uski signing
key se hoti hai. Keystore kho gaya to:

- already-installed app ko **kabhi upgrade nahi bhej paoge**
- user ko app uninstall karna padega
- naya keystore = Android ke liye bilkul alag app

`.gitignore` `*.keystore` aur `*.jks` block karta hai, kyunki `detect-secrets` binary files ke andar
nahi dekhta.

### Key password vs store password

`keytool` JDK 9 se default mein **PKCS12** keystore banata hai. PKCS12 mein alag key password hota
hi nahi. Matlab `ANDROID_KEY_PASSWORD` aur `ANDROID_KEYSTORE_PASSWORD` **same** honi chahiye.

### Secrets set karte waqt ek trap

`gh secret set` mein pipe karke value dene par trailing newline chipak jaata hai. Interactive prompt
trim kar deta hai, pipe nahi. Isliye **interactive prompt use karo.** Workflow iske liye alag se check
karta hai aur value kabhi print nahi karta.

---

## 4. Release kaise cut karte hain

1. `Android/app.json` mein `version` badhao (jaise `2.1.2`), `chore(android): version 2.1.2` PR,
   merge.
2. **Hamesha `main` se tag karo, pull karne ke baad:**

```
git checkout main
git pull
git tag v2.1.2
git push origin v2.1.2
```

`release-android.yml` `v*` tag par chalta hai aur GitHub Release par dono files attach karta hai:

| File | Kiske liye |
|---|---|
| `SubVora-v2.1.2.apk` | sideload / `adb install -r <apk>` |
| `SubVora-v2.1.2.aab` | Google Play Console upload |

### Version kahan se aata hai

- **versionName** = tag (`v2.1.2` → `2.1.2`) — Play aur Android settings yahi dikhate hain. App ki
  Settings screen `Android/app.json` ka `version` dikhati hai, isliye tag se pehle wahi bump karo.
- **versionCode** = workflow ka run number. Android upgrade tabhi leta hai jab ye strictly badhe, aur
  Play bhi har upload par bada versionCode maangta hai — run number apne aap badhta hai.
- MAUI ka aakhri versionCode 7 tha; workflow 7 ya kam par fail ho jaata hai. **Workflow file ka naam
  mat badlo** — run number 1 se restart ho jaayega.

### `main` se hi tag karo — warna dobara shuru karna padega

Tag-triggered run **tagged commit waali workflow file** use karta hai, `main` waali nahi. Feature
branch par tag kiya to us branch ki copy bake ho jaayegi. Nikalne ka ek hi raasta — tag delete karke
sahi commit par dobara banao:

```
gh release delete v2.1.2 --yes --cleanup-tag   # release bana hi nahi to skip karo
git push origin :refs/tags/v2.1.2              # tag release ke baad bhi bacha ho to
git tag -d v2.1.2
git checkout main && git pull
git tag v2.1.2 && git push origin v2.1.2
```

Jab tak kisi ne download nahi kiya, ye sasta hai. Download ho gaya to fix agle version mein bhejo.

---

## 5. Google Play

AAB Play Console mein **haath se** upload hota hai (Internal testing → Closed testing → Production).

### ⚠️ Do settings jo kabhi wapas nahi badalti

- **Package name `com.subvora.mobile`** — `Android/app.json` ka `android.package`. Badla to alag app.
- **App signing key: apni upload karo.** Pehli release par *Use a different key → Upload private key
  from Java keystore* chuno aur `production` keystore PEPK tool se export karke do. Google ki
  generated key li to Play build aur GitHub APK ke signatures alag honge — sideload waale users Play
  version par bina uninstall kiye switch nahi kar paayenge.

### Listing ke URLs

`site/` folder GitHub Pages par `.github/workflows/pages.yml` se publish hota hai:

| Play field | URL |
|---|---|
| Privacy policy | https://splitzzyy.github.io/Subvora/privacy.html |
| Delete account (Data safety) | https://splitzzyy.github.io/Subvora/delete-account.html |

Bina app ke deletion request email se aati hai — haath se karo
(`DELETE FROM users WHERE email = '...'`, cascade baaki sab hata deta hai) aur 7 din mein reply do.

### Review account

Play reviewer khud account nahi bana sakte. "Sign in details" mein ek demo account do (kuch
subscriptions daal ke). Is account ka password mat badlo aur delete mat karo — har update review
isi se hota hai.

### Naya personal developer account

Production se pehle **closed test, kam se kam 12 testers, lagaatar 14 din** — Google ki requirement.

---

## 6. App ki Android settings

| Cheez | Value | Note |
|---|---|---|
| Application ID | `com.subvora.mobile` | badla to har existing install atak jaayega |
| Min Android | API **24** | Expo SDK 57 default |
| Target SDK | **36** | Play ki current requirement poori |
| ABIs | arm64-v8a, armeabi-v7a | sirf phones; emulator ABIs APK double kar dete |

### Permissions

Notifications app ka core feature hai — renewal se pehle user ko alert. Inki permissions rehti hain:

| Permission | Kyun |
|---|---|
| `INTERNET`, `ACCESS_NETWORK_STATE` | API calls; offline detect karke write buttons disable |
| `POST_NOTIFICATIONS` | Android 13+ ka runtime prompt, renewal reminders ke liye |
| `RECEIVE_BOOT_COMPLETED` | reboot alarms clear kar deta hai; iske bina restart ke baad reminders band |
| `WAKE_LOCK`, `VIBRATE` | phone soya ho tab bhi notification deliver ho |

`Android/app.json` ke `android.blockedPermissions` se **hataayi gayi** (app inhe use nahi karta,
aur Play reviewer aisi permissions par sawaal karte hain):

| Permission | Kahan se aati thi |
|---|---|
| `SYSTEM_ALERT_WINDOW` | Expo template default |
| `READ_/WRITE_EXTERNAL_STORAGE` | Expo template default |
| `com.google.android.c2dm.permission.RECEIVE` | FCM push — `expo-notifications` kheenchta hai, par app mein push hai hi nahi |

Reminders **on-device schedule** hote hain (`Android/src/lib/reminders.ts`, `planReminders`) — koi
push service nahi, koi device-token table nahi. OS app band hone par bhi deliver karta hai.

**Exact alarm permission nahi hai, aur add mat karna.** Uske bina `expo-notifications` inexact
alarm (`setAndAllowWhileIdle`) use karta hai — Doze mein reminder thoda late aa sakta hai, jo kuch
din pehle wale reminder ke liye theek hai. Play `SCHEDULE_EXACT_ALARM`/`USE_EXACT_ALARM` sirf
alarm-clock/calendar apps ko deta hai.

Expo upgrade ke baad list check karo: Play Console → App bundle explorer, ya
`aapt2 dump permissions <apk>`.

---

## 7. Build fail ho to

`Verify signing secrets` step Gradle build se **pehle** chalta hai aur galat secret ka naam batata hai.

| Message | Kya galat hai |
|---|---|
| `Alias <***> does not exist` | `ANDROID_KEY_ALIAS` keystore ke alias se match nahi karta |
| `keystore password was incorrect` | `ANDROID_KEYSTORE_PASSWORD` galat |
| `contains whitespace` | secret pipe karke set kiya tha — interactive prompt se dobara set karo |
| `is empty` | secret set hi nahi hua, ya `production` environment ke bajaye repo par set hua |
| `AAB has 2 signers` | AAB se debug signature nahi hata — Play aisa bundle reject karta hai |

Gradle fail ho to job log mein failing task ka naam hota hai. Local par chalao:
`cd Android && npx expo prebuild --platform android && cd android && ./gradlew assembleRelease`.

---

## 8. Release ke baad verify

```
curl -i https://subvora-api.onrender.com/health/live
```

Phir device par:

1. APK install karo: `adb install -r <apk>` (ya Play internal testing se)
2. Login karo, subscription list load ho
3. Notification permission allow karo — reminders schedule hone chahiye
4. Wifi band karo → cached data dikhna chahiye, write buttons disabled
5. Poore time `adb logcat -b crash` chalate raho

Cache **read-only mirror** hai — offline writes hote hi nahi, ye by-design hai.

---

## 9. Accepted limits

- **Play upload manual hai** — workflow AAB banata hai, Play Console mein daalna haath se.
- **Bina app ke account deletion manual hai** — email aata hai, aap DB se delete karte ho.
- **Reminders inexact hain** — upar dekho.
- **iOS nahi** — upar scope wala note dekho.

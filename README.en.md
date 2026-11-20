# ♥ Galaxy Watch LAN Real-Time Heart Rate Overlay (GalaxyWatchHR)

**[繁體中文](README.md) | [English](README.en.md)**

A **local-network-only, zero-cloud, ad-free, ultra-low-latency** real-time heart rate display for **Samsung Galaxy Watch (Wear OS 3 / 4 / 5 / 6, verified on Galaxy Watch 9)** and Windows 10/11. Stream your live heart rate over Wi-Fi LAN directly to a **transparent, draggable, BPM-synchronized animated desktop overlay**.

---

## Key Features

- **Direct LAN Streaming (UDP Port `19888`)**: No cloud servers, no accounts, sub-5ms latency.
- **Single Self-Contained `.exe` — Zero Terminal Needed**:
  - Download just one `GalaxyWatchHR.exe` and double-click to run on any Windows 10/11 PC (no .NET install required).
  - **Built-in GUI Watch Setup & Firewall Wizard**: Embeds the ~29 KB Wear OS companion APK directly inside the `.exe`. Automatically detects local `adb` or offers a one-click download of official Google Platform-Tools (~6 MB), configures Windows Firewall, sideloads the watch app wirelessly, and grants background permissions automatically!
- **Transparent Animated Overlay**:
  - True per-pixel transparent background with high-contrast shadows legible on dark games and light web pages.
  - Natural double-beat (*lub-dub*) heart & expanding pulse ring **synchronized to your live BPM**.
  - Drag anywhere (remembers position), `Ctrl + Mouse Wheel` to resize, Lock Position, **Click-Through Mode** (never blocks game clicks), and **Start with Windows** toggle.
- **Reliable Watch Background Streaming**:
  - Full Wear OS 4 / 5 / 6 background sensor support (`BODY_SENSORS_BACKGROUND`, `READ_HEART_RATE`, `READ_HEALTH_DATA_IN_BACKGROUND`, `FOREGROUND_SERVICE_HEALTH`) — keeps streaming when returning to the watch face or when the watch screen turns off.
  - Built-in **12-second optical motion glitch hold**: smoothly preserves your last valid BPM during brief wrist-movement sensor dropouts instead of flickering to `--`.
- **Bilingual UI (English & Traditional Chinese)**: Auto-detects system language on both PC and watch, with one-click manual language switching anytime.

---

## Quick Start (3 Steps, No Terminal Required)

### Step 1: Download & Launch on Windows
1. Download **`GalaxyWatchHR.exe`** from the **Releases** page (single standalone file, no extraction or runtime installation required).
2. Place `GalaxyWatchHR.exe` anywhere you like (e.g., Desktop) and **double-click to launch**.
3. The transparent heart rate overlay appears near the top-left corner (showing your PC's LAN IP while waiting), and a red heart icon appears in the Windows system tray.

### Step 2: Run the Built-in Setup & Watch Installer Wizard
**Right-click** the overlay (or the red heart icon in the system tray) → select **"Watch Setup & Firewall..."**:

1. **Allow Windows Firewall (One Click)**:
   - Click **"Allow Firewall"** in Section 1 and approve the Windows Administrator (UAC) prompt. This allows local-subnet UDP `19888` on both Private and Public Wi-Fi profiles.
2. **Enable Wireless Debugging on your Galaxy Watch**:
   - Ensure your watch and PC are on the **same Wi-Fi router/network**.
   - On your watch: **Settings → About watch → Software info → tap "Software version" 7 times** to unlock Developer Options.
   - Go back to Settings → **Developer options** → turn on **ADB debugging** and **Wireless debugging**.
3. **Enter Watch Addresses & Install**:
   - **First time on this PC**: Keep *"First-time setup (pairing code required)"* checked. On the watch, tap **"Pair new device"** and enter the shown **Pairing `IP:Port`** and **6-digit code** into the wizard.
   - Return to the watch's **Wireless debugging** main screen, read the top **"IP address & Port"** (note: this port differs from the pairing screen), and enter it into **"Wireless debugging main IP:Port"**.
   - Click **"Install & Grant Background Permissions"**:
     - If `adb` is not installed on your PC, the wizard asks for confirmation and automatically downloads official Google Android Platform-Tools (~6 MB).
     - It then pairs, connects, installs `GalaxyWatchHR.apk`, grants all foreground/background heart rate permissions, adds the app to the watch battery whitelist, and launches **LAN Heart Rate** on your watch automatically!

### Step 3: Start Streaming
1. Tap **"Start"** on the **LAN Heart Rate** watch app.
   - The watch automatically discovers your PC on the LAN and starts streaming live BPM.
   - If your router blocks LAN broadcast packets, enter your PC's LAN IP (shown in the wizard, e.g., `192.168.1.100`) into the watch's **PC IP** box and tap Start.
2. Once streaming, press **Home** to return to your normal watch face or let the screen sleep — your heart rate continues streaming in the background!
3. **Tip**: Turn off **Wireless debugging** on your watch after installation to save battery.

---

## Overlay Controls

| Action | Description |
| :--- | :--- |
| **Left-click & drag** | Move the overlay anywhere on screen (position is auto-saved) |
| **`Ctrl` + Mouse Wheel** | Resize the overlay smoothly |
| **Right-click** | Open menu (Setup Wizard, Copy PC IP, Demo Mode, Scale, Opacity, Language, Lock Position, Click-Through, Start with Windows) |
| **Right-click Tray Heart Icon** | Restore from Click-Through Mode, toggle settings, or exit |

---

## Troubleshooting (FAQ)

1. **Watch stays on "Searching LAN PC..." or PC shows no heart rate?**
   - Make sure you clicked **"Allow Firewall"** in the Setup Wizard (green status: *Allowed UDP 19888*).
   - Confirm both devices are on the **same Wi-Fi subnet** (e.g., `192.168.x.x`).
   - Some routers filter broadcast packets — type your PC's LAN IP directly into the watch app's **PC IP** field.
2. **Overlay doesn't show over my game?**
   - Native Windows topmost overlays cannot appear above *Exclusive Fullscreen* games. Switch your game display mode to **Borderless Windowed**, and enable **Click-Through Mode** so mouse clicks pass cleanly to the game.
3. **Heart rate occasionally freezes briefly or resets to `--`?**
   - Optical wrist sensors momentarily drop signal during rapid arm movement; the app already includes a **12-second glitch hold**. Wearing the strap snugly (about one finger-width above the wrist bone) significantly improves optical stability.
4. **Moved `GalaxyWatchHR.exe` to another folder and "Start with Windows" stopped working?**
   - Uncheck and re-check **"Start with Windows"** from the right-click menu to update the saved executable path.

---

## Project Structure & Building from Source

```text
GalaxyWatchHR/
├── pc-overlay/            # Windows .NET 7 WPF overlay & built-in setup wizard
├── watch-app/             # Native Wear OS minimal app & offline build script (build_apk.ps1)
├── tools/
│   └── test_udp_sender.py # Local UDP simulator for testing
├── README.md              # Traditional Chinese documentation
├── README.en.md           # English documentation
└── LICENSE                # MIT License
```

### Build Commands
- **Watch APK** (requires Java JDK 17/20 + Android SDK `android-34` & `build-tools 33.0.2`, no Gradle required):
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\watch-app\build_apk.ps1
  ```
  Output: `dist\GalaxyWatchHR.apk`
- **Windows Self-Contained Single-File `.exe`** (requires .NET 7 SDK; automatically embeds `dist\GalaxyWatchHR.apk`):
  ```powershell
  dotnet publish .\pc-overlay\WatchHROverlay.csproj -c Release -r win-x64 --self-contained true -o .\release
  ```
  Output: `release\GalaxyWatchHR.exe`

---

## License

Licensed under the [MIT License](LICENSE).

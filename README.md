# Multi-Monitor Sleep Controller

A Windows desktop application for controlling individual monitors without breaking Windows display topology. Supports DDC/CI hardware power commands, fullscreen blackout overlays, software dimming, OS-level output control, and system-tray brightness management — all designed to keep your windows exactly where they are.

## Why this exists

Windows rearranges desktop windows whenever a monitor is disabled through its display topology APIs. This app avoids that entirely by using non-topology control paths:

| Mode | What it does | Requires DDC/CI |
|---|---|---|
| **Auto** | DDC/CI first, blackout overlay fallback | No |
| **DDC/CI** | Hardware power on/off via `SetVCPFeature` (VCP `0xD6`) | Yes |
| **Blackout Overlay** | Fullscreen black window covering the display | No |
| **Software Dimming** | Semi-transparent overlay proportional to brightness | No |
| **Output Disable** *(experimental)* | OS-level signal cut via `ChangeDisplaySettingsEx` | No |

Because none of these touch the Windows display topology, your monitor arrangement and window positions stay intact.

## Features

### Monitor Control
- Enumerate connected displays, including logical-only displays without DDC handles.
- Per-monitor on/off toggle with configurable control mode.
- Batch actions: **All On**, **All Off**, **Apply Grid States**, **Clear Overlays**.
- Editable monitor friendly names (persisted across sessions).

### Brightness
- **Global brightness slider** with per-monitor DDC/CI brightness control.
- **Per-monitor brightness offset** — fine-tune each display relative to the global slider.
- **DDC brightness type** selectable per monitor (`Luminance` VCP `0x10` or `Backlight` VCP `0x13`).
- **Software dimming mode** for displays without DDC/CI brightness support.
- **System tray brightness icon** — left-click for a flyout brightness slider, right-click for options.
- **Virtual driver IPC** — listens on `VirtualMonitorBrightnessPipe` for external brightness commands.
- Periodic background sync reads current hardware brightness to keep the slider up to date.

### Profiles & Hotkeys
- Save named monitor profiles capturing the current grid state.
- Assign global hotkeys per profile (`Alt + NumPad0..9` and other modifier combos).
- Trigger profiles from anywhere via registered global hotkeys.

### Safety & Recovery
- **Emergency wake** — when all monitors are turned off, press any key to wake everything (uses a low-level keyboard hook).
- 1-second activation delay after arming to prevent accidental instant re-wake.

### UI
- Dark mode and light mode with persistent preference.
- Immersive dark title bar (DWM `DWMWA_USE_IMMERSIVE_DARK_MODE`).
- Minimise-to-tray on window close; restore via tray icon context menu.

### Settings
- All settings persisted as JSON at:
  ```
  %AppData%\MultiMonitorSleepController\settings.json
  ```
- Saved data: monitor modes, brightness offsets, DDC types, custom names, profiles, hotkeys, dark mode preference.

## Requirements

- **OS:** Windows 10 version 2004+ or Windows 11.
- **SDK:** .NET 9.0 SDK or later (for building from source).
- **DDC/CI** is optional. Monitors that don't expose DDC/CI can use Blackout Overlay, Software Dimming, or Output Disable mode. For hardware power control and brightness, DDC/CI must be enabled in the monitor's OSD menu.

## Build

### Quick build

```powershell
dotnet build .\MultiMonitorSleepController\MultiMonitorSleepController.csproj
```

### Using the build script

```powershell
# Release build
.\build.ps1

# Debug build
.\build.ps1 -Configuration Debug

# Clean then publish a self-contained single-file exe
.\build.ps1 -Clean -Publish

# Publish for a specific runtime
.\build.ps1 -Publish -Runtime win-arm64
```

The published executable is written to `publish/` at the repository root.

### Run from source

```powershell
dotnet run --project .\MultiMonitorSleepController\MultiMonitorSleepController.csproj
```

## Usage

1. **Start the app.** Monitors are enumerated automatically on launch.
2. **Choose a mode** per monitor row:
   - `Auto` — tries DDC/CI, falls back to blackout overlay.
   - `DdcCi` — forces DDC/CI only.
   - `BlackoutOverlay` — fullscreen black window, no DDC/CI.
   - `SoftwareDimming` — semi-transparent overlay controlled by brightness slider.
   - `OutputDisable` — OS-level output disable/enable *(experimental, may reflow windows)*.
3. **Toggle Signal On** per monitor and click **Apply Grid States**.
4. **Adjust brightness** using the global slider or the system tray flyout.
   - Set per-monitor **Brightness Offset** for relative tuning.
   - Select **DDC Type** (`Luminance` or `Backlight`) if your monitor uses a non-standard VCP code.
5. **Create a profile:** enter a name, choose a hotkey, click **Save Profile From Grid**.
6. **Apply profiles** from the list or by pressing the assigned global hotkey.
7. **Dark mode** toggle is at the bottom status bar.
8. **Minimise to tray** by closing the window. Right-click the tray icon to open or exit.
9. If all monitors are off and you can't see anything, **press any key** to wake all monitors.

## Project structure

```
multimonitor-controller/
├── build.ps1                              # Build & publish script
├── multimonitor-sleepcontroller.sln       # Visual Studio solution
├── MultiMonitorSleepController/
│   ├── MultiMonitorSleepController.csproj # .NET 9.0 WinForms project
│   ├── Program.cs                         # Entry point
│   ├── MainForm.cs                        # Main UI, profiles, hotkeys, brightness
│   ├── MonitorControllerService.cs        # DDC/CI enumeration & power/brightness
│   ├── DisplayOutputControllerService.cs  # OS-level output disable/enable
│   ├── OverlayControllerService.cs        # Blackout & dimming overlay management
│   ├── BlackoutOverlayForm.cs             # Fullscreen overlay window
│   ├── BrightnessTrayIcon.cs              # System tray NotifyIcon
│   ├── BrightnessFlyoutForm.cs            # Tray brightness flyout popup
│   ├── GlobalKeyboardHook.cs              # Low-level keyboard hook (emergency wake)
│   ├── NativeMethods.cs                   # P/Invoke declarations
│   ├── AppSettings.cs                     # Settings & profile models
│   ├── SettingsStore.cs                   # JSON settings persistence
│   ├── MonitorControlMode.cs              # Control mode enum
│   ├── DdcBrightnessType.cs               # Brightness VCP code enum
│   └── HotkeyModels.cs                   # Hotkey binding models
└── README.md
```

## Notes and limitations

- If a monitor row shows DDC/CI unchecked, use `Auto`, `BlackoutOverlay`, `SoftwareDimming`, or `OutputDisable` mode.
- Some monitors or GPU chains may not wake reliably from deep sleep with VCP commands.
- If DDC/CI causes side effects on your setup, set that monitor's mode to `BlackoutOverlay` or `SoftwareDimming`.
- `OutputDisable` mode may cause temporary window repositioning because Windows treats that output as detached.
- If a hotkey fails to register, another application or profile is likely already using it.
- Global hotkeys and emergency any-key wake require the app to be running.
- The virtual driver IPC pipe (`VirtualMonitorBrightnessPipe`) is optional and only used when an external brightness driver is installed.

## License

This project does not currently specify a license.

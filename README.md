<p align="center">
  <img src="docs/logo.svg" width="112" alt="SideDim logo: three monitors, the middle one lit">
</p>

<h1 align="center">SideDim</h1>

<p align="center"><b>Dims your other monitors while you game.</b></p>

<p align="center">
  <a href="https://github.com/Quintinator/SideDim/releases/latest"><img src="https://img.shields.io/github/v/release/Quintinator/SideDim?label=download" alt="Latest release"></a>
  <a href="https://github.com/Quintinator/SideDim/actions/workflows/ci.yml"><img src="https://github.com/Quintinator/SideDim/actions/workflows/ci.yml/badge.svg" alt="CI status"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT%20%2B%20Commons%20Clause-green" alt="License: MIT + Commons Clause"></a>
</p>

SideDim is a small Windows tray app. When your game (or any window you choose) has focus, every other monitor goes dark. Alt-tab out and they come back at the brightness they had before.

Only one screen? SideDim works there too: it darkens everything around the focused window, so only your game or video stays lit.

<p align="center">
  <img src="docs/screenshot.png" width="430" alt="The SideDim settings window">
</p>

## Features

- **Pick your games.** Browse to an `.exe` (the dialog opens in your Steam library) or pick from apps that are running. Games are matched by exe name, so moving a game to another drive doesn't break anything.
- **Any fullscreen app.** Optionally dim for anything that covers a whole monitor, such as videos and games you haven't added.
- **Always mode.** Dim around whatever window has focus, game or not.
- **Three ways to dim:**
  | Method | How | Best for |
  |---|---|---|
  | Overlay | A black, click-through layer over each other screen | Works on every monitor, smooth fades |
  | Monitor backlight | Turns the monitor's real brightness down over DDC/CI | Less light in the room |
  | Both | Backlight down plus the overlay on top | As dark as it gets without turning screens off |
- **Spotlight.** Also darkens the focused window's own screen, leaving only the window lit. The cut-out follows the window when you move it.
- **Works on a single monitor.** With one screen there's nothing beside it to dim, so SideDim turns the spotlight on by itself. The backlight options need a second monitor, so they're greyed out until one is connected; your choice is kept and comes back when you plug one in.
- **Two sliders.** One for the backlight level, one for overlay darkness. A screen you already turned down further is never brightened.
- **Test button.** Uses the settings window as the "game", so you can tune everything live without starting one.
- **Dim now hotkey.** `Ctrl+Alt+F9` by default, for windowed games and videos. Any combination with Ctrl or Alt works; bare keys and Alt+F4 are refused so SideDim never steals keys from games.
- **Your brightness comes back.** SideDim saves each monitor's brightness to disk before it changes anything. After a crash or power cut, the next start restores it. An unplugged monitor is restored when it comes back.
- **Never steals focus.** Overlays can't be clicked or focused, so exclusive fullscreen games don't minimize.
- **Start with Windows.** A toggle in the settings window and in the tray menu.
- One small exe. No installer, no admin rights, no network access.

## Install

1. Download the latest exe from [Releases](https://github.com/Quintinator/SideDim/releases/latest):
   - `SideDim-x.y.z-win-x64.exe` runs anywhere (about 70 MB, includes .NET).
   - `SideDim-x.y.z-win-x64-needs-dotnet8.exe` is under 1 MB but needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Run it. The settings window opens the first time; after that SideDim lives in the tray.

Windows 10 or 11. The exe isn't code signed yet, so SmartScreen may warn the first time: click **More info**, then **Run anyway**.

## Controls

| Action | What it does |
|---|---|
| Left-click the tray icon | Opens settings |
| Right-click the tray icon | Settings, automatic dimming on/off, Dim now, Dim for *last app*, Start with Windows, Exit |
| `Ctrl+Alt+F9` | Dim now, press again to stop (changeable in settings) |
| Start the exe again | Opens the settings of the copy that's already running |

## Settings

Everything in the settings window applies immediately. Settings are stored in `%APPDATA%\SideDim\settings.json`. You can edit that file by hand: values out of range are repaired, and a file that can't be read is kept as `settings.json.broken-<date>` instead of being overwritten.

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Automatic dimming on or off |
| `Trigger` | `SelectedApps` | `SelectedApps`: only for apps in the list. `AnyWindow`: for whatever window has focus |
| `Apps` | `[]` | Exe paths or process names to dim for |
| `AlsoAnyFullscreen` | `true` | With `SelectedApps`, also dim for any app that covers a whole monitor |
| `Mode` | `Overlay` | `Overlay`, `Hardware` (backlight) or `Both`. With one monitor only the overlay is used |
| `BacklightLevel` | `10` | Monitor brightness while dimmed, 0 to 100 |
| `OverlayStrength` | `70` | Overlay darkness in percent, 0 to 95 |
| `Spotlight` | `false` | Also darken around the focused window on its own screen. Always on when only one monitor is connected |
| `NeverDimFor` | `[]` | Extra apps that never trigger dimming (file only). Explorer, the Start menu, search and the snipping tools are always excluded |
| `DimDelayMs` | `800` | How long a window must have focus before dimming, so quick alt-tabs don't flicker |
| `RestoreDelayMs` | `200` | How long before restoring after focus leaves (file only) |
| `FadeMs` | `300` | Overlay fade time, 0 to 2000 (file only) |
| `ToggleHotkey` | `Ctrl+Alt+F9` | The Dim now hotkey. A function key is the default because Ctrl+Alt+letter is AltGr+letter on many keyboard layouts |

## Command line

| Flag | What it does |
|---|---|
| `--probe` | Lists which monitors answer DDC/CI and their current brightness. Printed to the console and written to the log, handy for bug reports. |

## Files

In `%APPDATA%\SideDim\` unless noted (there's a link at the bottom of the settings window):

| File | Contents |
|---|---|
| `settings.json` | Your settings |
| `sidedim.log` | What SideDim did and any errors. The previous log is kept as `sidedim.log.old`. |
| `hardware-state.json` | Brightness to restore. Only exists while a screen is dimmed, and lives in `%LOCALAPPDATA%\SideDim\` because it belongs to this PC's monitors. |

## Troubleshooting

- **Backlight mode does nothing on a screen.** Turn on DDC/CI in that monitor's on-screen menu, then run `SideDim.exe --probe`. Some monitors, docks and most laptop panels don't support DDC/CI; use Overlay for those.
- **The hotkey shows "In use by another app".** Another program registered that combination first. Pick a different one.
- **A screen stayed dark.** Start SideDim again: it restores whatever is in `%LOCALAPPDATA%\SideDim\hardware-state.json`. You can also set the brightness in the monitor's own menu.
- **Anti-cheat.** SideDim never touches game processes. It only asks Windows which window has focus and which exe owns it.
- **Something else?** [Open an issue](https://github.com/Quintinator/SideDim/issues) and include the output of `SideDim.exe --probe` and your `sidedim.log`.

## How it works

- Every 100 ms it checks the foreground window, its monitor and its exe. A target must hold for `DimDelayMs` before anything changes.
- Overlays are borderless, topmost, layered windows with `WS_EX_TRANSPARENT` and `WS_EX_NOACTIVATE`, so clicks pass through and focus never moves.
- Backlight changes use VESA MCCS (VCP code `0x10`) through `dxva2.dll`, on a background thread because each call takes tens of milliseconds.

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
./build.ps1               # restore, format check, build, tests, publish (exactly what CI runs)
./build.ps1 -SkipPublish  # quick check before committing
dotnet test               # tests only
```

Exes land in `artifacts/publish/`. `tools/make-icon.ps1` regenerates the icon from code.

Contributions are welcome. Please run `./build.ps1` before opening a pull request.

## License

SideDim is free, and it stays free. It's released under the [MIT license with the Commons Clause](LICENSE):

- **You can** use it anywhere (at home, at work, on stream), read the code, fork it, change it and share your version for free.
- **You can't** sell SideDim, or sell anything whose value comes mainly from SideDim.

Strictly speaking this makes SideDim "source-available" rather than OSI open source; the only thing it takes away is the right to sell it.

---

<sub>Made with the help of AI.</sub>

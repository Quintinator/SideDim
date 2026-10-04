# Changelog

All notable changes to SideDim. Each `## [version]` section becomes the notes of that GitHub release.

## [0.1.0] - 2026-10-04

First public release.

### Features

- Dims every screen except the one with the app you're focused on, for gaming and for work. Choose an overlay, the monitor's real backlight (DDC/CI), or both.
- Dim for the apps you pick (browse to an exe or pick a running app, Microsoft Store and Game Pass apps included), for any fullscreen app, or for whatever window has focus.
- Spotlight: also darkens the focused window's own screen, leaving only the window lit. Turns on by itself when only one monitor is connected.
- Per-screen settings: give a screen its own darkness or set it to never dim. "Identify screens" shows each screen's number and name.
- Simple and Advanced tabs, a Test button to tune everything live, a "Dim now" hotkey (`Ctrl+Alt+F9`) and Start with Windows.
- Your brightness comes back: it is saved before anything changes and restored on exit, after a crash or power cut, and when an unplugged monitor returns.
- Never takes focus away from a game; no installer, no admin rights, no network access.

### Downloads

- `SideDim-0.1.0-win-x64.exe` runs on any Windows 10 or 11 PC (about 70 MB, includes .NET).
- `SideDim-0.1.0-win-x64-needs-dotnet8.exe` is under 1 MB but needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- `SHA256SUMS.txt` lets you check your download: `Get-FileHash SideDim-0.1.0-win-x64.exe` should match the line for that file.

The exe isn't code signed yet, so Windows SmartScreen may warn the first time: click **More info**, then **Run anyway**.

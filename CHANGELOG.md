# Changelog

All notable changes to SideDim. Each `## [version]` section becomes the notes of that GitHub release.

## [0.2.1] - 2026-10-04

### Security

- The Windows libraries SideDim itself calls (for monitor brightness and window sizes) are now loaded only from the Windows system folder. Before, a harmful file with the same name next to SideDim.exe, for example in Downloads, could be loaded instead.
- The small `needs-dotnet10` exe no longer looks for .NET's startup file (`hostfxr.dll`) next to itself. It only uses the .NET that is installed.
- The large exe has .NET built in, and while .NET starts it can still load a harmful file named like a Windows file from its own folder, before SideDim can stop that. So it now checks its folder when it starts. If it runs from Downloads, the Desktop, Documents or your Temp folder, or from a folder with other `.dll` files, it asks to move itself to `%LOCALAPPDATA%\Programs\SideDim`. **Move it** copies it there, adds a Start menu shortcut, restarts it from there and deletes the downloaded copy. If Start with Windows was on, it now starts the moved copy. Tick **Don't ask again** to stop the question.

### Fixed

- After a crash, brightness is now restored before the crash is written to the log, so a problem with the log can no longer keep screens dimmed until SideDim starts again.
- Monitor names could go missing when the display layout changed at the moment SideDim read them; it now retries.
- An app in your list whose icon Windows can't read no longer stops the settings window from opening. It gets a generic icon instead.

### Downloads

- `SideDim-0.2.1-win-x64.exe` runs on any Windows 10 or 11 PC (includes .NET).
- `SideDim-0.2.1-win-x64-needs-dotnet10.exe` is much smaller but needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- `SHA256SUMS.txt` lets you check your download: `Get-FileHash SideDim-0.2.1-win-x64.exe` should match the line for that file.

Settings carry over: just replace the exe.

## [0.2.0] - 2026-10-04

### Changed

- Runs on .NET 10, which Microsoft supports until November 2028. .NET 8, used by 0.1.0, stops getting security updates on 10 November 2026.
- The small download is now `SideDim-0.2.0-win-x64-needs-dotnet10.exe` and needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). The larger `SideDim-0.2.0-win-x64.exe` still runs on any Windows 10 or 11 PC without installing anything.

### Downloads

- `SideDim-0.2.0-win-x64.exe` runs on any Windows 10 or 11 PC (includes .NET).
- `SideDim-0.2.0-win-x64-needs-dotnet10.exe` is much smaller but needs the .NET 10 Desktop Runtime.
- `SHA256SUMS.txt` lets you check your download: `Get-FileHash SideDim-0.2.0-win-x64.exe` should match the line for that file.

Settings carry over from 0.1.0: just replace the exe.

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

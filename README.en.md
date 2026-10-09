# Wintools — Windows tweaks with history and rollback

Wintools is a portable Windows app that applies selected system settings (privacy and telemetry, File Explorer and taskbar, services, scheduled tasks, power, network), cleans up disk space, checks and repairs Windows, and restores previous settings through the same window. Before every change it saves the original state, so most changes can be undone from **History and rollback**.

Русская версия: [README.md](README.md).

## Download

Download **WintoolsPortable.exe** from [GitHub Releases](https://github.com/Lucky2356/wintools/releases/latest) and put it in its own writable folder. Nothing is installed: the interface and the engine are one EXE, and the `WintoolsData` folder next to it keeps settings, history and rollback data.

- Windows 10 1809+ or Windows 11, **x64** or **ARM64**, with the built-in .NET Framework 4.7.2 or later (native ARM64 needs Windows 11 24H2 or later with 4.8.1; older systems run it under x64 emulation).
- Check the download in PowerShell: `Get-FileHash .\WintoolsPortable.exe` must match `WintoolsPortable.exe.sha256` from the release.
- The program is not code-signed, so SmartScreen may warn about an unknown publisher: **More info → Run anyway**.
- The interface is in English or Russian. **Settings → Appearance → Interface language** switches it (applied after a restart); "Like Windows" picks Russian on Russian, Ukrainian, Belarusian and Kazakh Windows and English otherwise.

## Made for different people

- **First start:** a three-step introduction — language, theme and text size; how Wintools protects your PC; choosing a mode and a restore point. It can be shown again from Settings.
- **Simple and Full modes:** the Simple mode (default for new users) hides services, processes, startup, the settings check, site blocking and high-risk actions from the menu, Home and search. The Full mode shows everything.
- **Five ready collections:** fewer ads, a handier File Explorer, gaming, an older PC and a laptop. A collection only selects actions; Windows changes after you review the plan.
- **Clear action cards:** risk shown with an icon and words, what exactly changes, whether rollback is full, partial or unavailable, and when a sign-in is needed.
- **Help when something fails:** "What to do?" next to an error, with tips and a diagnostics package for a GitHub report.
- **Accessibility:** names for Narrator on buttons, tiles, lists and list rows; the status line is announced; text colours meet WCAG AA (4.5:1) and risk never relies on colour alone; text size up to 200 %; animation follows the Windows setting.
- **Keyboard:** Ctrl+K search, Ctrl+1…Ctrl+9 and Ctrl+0 main pages, F5 refresh, F1 shortcut help, Esc closes dialogs.
- **Older computers:** the light interface turns off shadows and animation and refreshes load graphs half as often.

## For administrators

Save a plan as a profile on one computer (**Change plan → Save profile**) and apply it on another without the window:

```
WintoolsPortable.exe --apply profile.json [--report result.txt] [--dry-run]
```

Exit code 0 means every action succeeded, 1 an action failed (later actions were not started), 2 the request was invalid. The report goes to the given file or to `WintoolsData\reports`. Run it from an elevated prompt to avoid a UAC prompt per action. **Settings check** saves an HTML report without user or computer names.

## Safety

- Every action explains its result, consequences and rollback limits; high-risk actions are hidden by default.
- Preview runs the real engine without changes; nothing changes without confirmation, and administrator rights are requested only for system changes.
- Disk cleanup, Edge removal and removal of provisioned AppX copies have rollback limits described in the app.

See [SECURITY.md](SECURITY.md) to report a vulnerability and [CONTRIBUTING.md](CONTRIBUTING.md) to help.

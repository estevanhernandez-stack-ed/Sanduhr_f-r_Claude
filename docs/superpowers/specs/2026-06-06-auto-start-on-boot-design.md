# Auto-start on Boot — Design Spec

- **Date:** 2026-06-06
- **Status:** Design approved (off-by-default opt-in confirmed) — build
- **Repo / branch:** `Sanduhr_f-r_Claude` (Windows PySide6) · `feat/auto-start` off `origin/main`
- **Part of:** the v2.4 Windows / MS-Store bundle (with the tier badge PR #25 + the sessionKey finder)

## 1. Goal

An opt-in "Start Sanduhr when Windows starts" control in Settings, **off by default**, working across both distribution channels (Inno `.exe` / GitHub and MSIX / Store).

## 2. Two mechanisms, by install type

- **Unpackaged (Inno `.exe`, the live channel):** a per-user registry Run entry at
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Sanduhr` = the quoted running
  executable path. This is the **same key + value name** the Inno installer's optional
  `autostart` task writes (`windows/installer/Sanduhr.iss` `[Registry]`), so an
  install-time opt-in and a later in-app toggle stay consistent. **Fully toggleable at
  runtime.**
- **Packaged (MSIX / Store, in review):** declared as a `windows.startupTask` manifest
  extension with `Enabled="false"`. Flipping it programmatically needs the WinRT
  `StartupTask` API, which we don't bundle for v2.4 (best-effort posture) — so the in-app
  control **deep-links to Windows Settings → Startup apps** (`ms-settings:startupapps`)
  instead. The manifest extension is what makes Sanduhr appear there.

## 3. New module — `startup.py`

Testable core kept free of UI:

- `is_packaged(executable=None) -> bool` — heuristic: executable path under `…\WindowsApps\`.
- `run_command(executable=None) -> str` — the quoted relaunch path for the Run value.
- `is_enabled_unpackaged(run_key=None, value_name=None) -> bool` / `set_enabled_unpackaged(enabled, executable=None, run_key=None, value_name=None)` — winreg I/O; key/value-name params (defaulting to the module globals) make the round-trip testable against a throwaway HKCU test key.
- `is_enabled() -> bool` — public read (packaged → `False`, can't read StartupTask without WinRT).
- `open_startup_settings()` — opens `ms-settings:startupapps`.
- `set_enabled(enabled, executable=None) -> StartupOutcome(applied, opened_settings)` — unpackaged writes the Run key; packaged opens Windows Settings.

## 4. Settings UI (`settings_dialog.py`, Cards tab)

A "**Startup**" subsection after the pacing toggles:
- **Unpackaged:** `QCheckBox` "Start Sanduhr when Windows starts", initial state = `startup.is_enabled()`, `toggled` → `_on_autostart_toggled` which calls `startup.set_enabled(checked)` (+ confirm chime, soft-fail on `OSError`).
- **Packaged:** a note + a "Open Windows Startup settings…" button → `startup.open_startup_settings()`.

## 5. Manifest (`windows/msix/Package.appxmanifest.template`)

Add the `desktop` namespace (+ to `IgnorableNamespaces`) and an `<Extensions>` block in `<Application>`:

```xml
<desktop:Extension Category="windows.startupTask" Executable="Sanduhr.exe" EntryPoint="Windows.FullTrustApplication">
  <desktop:StartupTask TaskId="SanduhrAutoStart" Enabled="false" DisplayName="Sanduhr für Claude" />
</desktop:Extension>
```

`Enabled="false"` = off by default.

## 6. Tests — `windows/tests/test_startup.py`

- `is_packaged` (WindowsApps path → True; AppData/Program-Files path → False; empty → False).
- `run_command` quoting.
- `_VALUE_NAME == "Sanduhr"` (must match the installer's `[Registry]` ValueName — guards drift).
- Enable→disable→re-read round-trip against a throwaway HKCU test key (real winreg, cleaned in teardown).
- `set_enabled` packaged branch opens settings (monkeypatched); unpackaged branch writes the key.

## 7. Out of scope / follow-ups

- **Programmatic packaged toggle** (WinRT `StartupTask.RequestEnableAsync` via `winsdk`) — deferred; v2.4 deep-links to Windows Settings.
- A dedicated **General** settings tab (Startup currently rides the Cards tab) — possible later regroup.

## 8. Notes

- Off by default everywhere.
- One PR (`feat/auto-start`), independent of the tier badge (different files; CHANGELOG `[Unreleased]` is the only merge touchpoint).
- GitNexus MCP not connected this session — manual blast-radius check on `_build_cards_tab` before editing.

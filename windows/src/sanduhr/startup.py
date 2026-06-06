"""Run-on-login (auto-start) management.

Off by default everywhere; the user opts in from Settings → Cards → Startup.

Two mechanisms, by install type:

- **Unpackaged** (Inno `.exe` / GitHub build): a per-user registry Run entry at
  ``HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\Sanduhr`` pointing at
  the running executable. This is the SAME value the Inno installer's optional
  "autostart" task writes (``windows/installer/Sanduhr.iss``), so an install-time
  opt-in and a later in-app toggle stay consistent. Fully toggleable at runtime.

- **Packaged** (MSIX / Store): declared as a ``windows.startupTask`` manifest
  extension (Enabled="false"). Flipping it programmatically needs the WinRT
  ``StartupTask`` API, which we don't bundle yet, so :func:`set_enabled`
  deep-links to Windows Settings → Startup apps instead.
"""

import sys
from typing import NamedTuple, Optional

_RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
_VALUE_NAME = "Sanduhr"  # MUST match windows/installer/Sanduhr.iss [Registry] ValueName
_SETTINGS_URI = "ms-settings:startupapps"


class StartupOutcome(NamedTuple):
    """Result of a :func:`set_enabled` call."""

    applied: bool          # True if the on-login state was actually written
    opened_settings: bool  # True if we punted to Windows Settings (packaged)


def is_packaged(executable: Optional[str] = None) -> bool:
    """True when running as an installed MSIX package.

    Heuristic: an MSIX-installed executable lives under ``…\\WindowsApps\\``.
    """
    exe = executable if executable is not None else (sys.executable or "")
    return "windowsapps" in exe.replace("/", "\\").lower()


def run_command(executable: Optional[str] = None) -> str:
    """The Run-key value: the quoted path used to relaunch Sanduhr on login."""
    exe = executable if executable is not None else (sys.executable or "")
    return f'"{exe}"'


# -- unpackaged (registry Run key) -------------------------------------------

def is_enabled_unpackaged(
    run_key: Optional[str] = None, value_name: Optional[str] = None
) -> bool:
    """True if the HKCU Run entry exists. Body reads the module globals so
    tests can redirect them to a throwaway key."""
    import winreg

    run_key = run_key if run_key is not None else _RUN_KEY
    value_name = value_name if value_name is not None else _VALUE_NAME
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, run_key) as key:
            winreg.QueryValueEx(key, value_name)
        return True
    except FileNotFoundError:
        return False
    except OSError:
        return False


def set_enabled_unpackaged(
    enabled: bool,
    executable: Optional[str] = None,
    run_key: Optional[str] = None,
    value_name: Optional[str] = None,
) -> None:
    """Write (enabled) or remove (disabled) the HKCU Run entry."""
    import winreg

    run_key = run_key if run_key is not None else _RUN_KEY
    value_name = value_name if value_name is not None else _VALUE_NAME
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, run_key) as key:
        if enabled:
            winreg.SetValueEx(
                key, value_name, 0, winreg.REG_SZ, run_command(executable)
            )
        else:
            try:
                winreg.DeleteValue(key, value_name)
            except FileNotFoundError:
                pass


# -- public API the UI calls -------------------------------------------------

def is_enabled() -> bool:
    """Best-effort current state. For packaged installs we can't read the
    StartupTask state without WinRT, so we report the manifest default
    (False) and let the user manage it in Windows Settings."""
    if is_packaged():
        return False
    return is_enabled_unpackaged()


def open_startup_settings() -> None:
    """Open Windows Settings → Startup apps (the packaged fallback)."""
    import webbrowser

    webbrowser.open(_SETTINGS_URI)


def set_enabled(enabled: bool, executable: Optional[str] = None) -> StartupOutcome:
    """Apply the on-login preference.

    Unpackaged: writes/removes the Run key. Packaged: opens Windows
    Settings → Startup apps (can't toggle programmatically without WinRT).
    """
    if is_packaged():
        open_startup_settings()
        return StartupOutcome(applied=False, opened_settings=True)
    set_enabled_unpackaged(enabled, executable=executable)
    return StartupOutcome(applied=True, opened_settings=False)

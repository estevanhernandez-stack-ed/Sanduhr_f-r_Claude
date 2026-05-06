"""Filesystem location helpers for Sanduhr data files.

Single source of truth for where the app reads and writes on Windows.
"""

import os
import sys
from pathlib import Path


def bundled_icon_path() -> str:
    """Resolve the path to the shipped Sanduhr.ico — works both when
    running from source (`windows/icon/Sanduhr.ico`) and from a
    PyInstaller bundle (`_MEIPASS/icon/Sanduhr.ico`). Caller is
    responsible for `os.path.exists` check before passing to QIcon."""
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        return os.path.join(meipass, "icon", "Sanduhr.ico")
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(here, "..", "..", "icon", "Sanduhr.ico")


def app_data_dir() -> Path:
    """Return %APPDATA%\\Sanduhr, creating it on first access."""
    base = Path(os.environ["APPDATA"]) / "Sanduhr"
    base.mkdir(parents=True, exist_ok=True)
    return base


def history_file() -> Path:
    """Legacy single-account history path. Read during migration only —
    new writes go to history_file_for(account)."""
    return app_data_dir() / "history.json"


def history_file_for(account: str) -> Path:
    """Per-account history file: history.{account}.json."""
    return app_data_dir() / f"history.{account}.json"


def settings_file() -> Path:
    return app_data_dir() / "settings.json"


def log_file() -> Path:
    return app_data_dir() / "sanduhr.log"


def last_error_file() -> Path:
    return app_data_dir() / "last_error.json"


def legacy_config_file() -> Path:
    """v1 plaintext config location — read-only, used for migration."""
    return Path.home() / ".claude-usage-widget" / "config.json"


def legacy_history_file() -> Path:
    """v1 history file — read during migration."""
    return Path.home() / ".claude-usage-widget" / "history.json"

"""Filesystem location helpers for Sanduhr data files.

Single source of truth for where the app reads and writes on Windows.
"""

import os
from pathlib import Path


def app_data_dir() -> Path:
    """Return %APPDATA%\\Sanduhr, creating it on first access."""
    base = Path(os.environ["APPDATA"]) / "Sanduhr"
    base.mkdir(parents=True, exist_ok=True)
    return base


def history_file() -> Path:
    return app_data_dir() / "history.json"


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

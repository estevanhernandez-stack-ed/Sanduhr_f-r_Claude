"""Credential storage (Windows Credential Manager) + v1 migration.

Uses the `keyring` library to persist the session key and cf_clearance
cookie against the Windows Credential Manager under service
`com.626labs.sanduhr` -- matching the Mac Keychain service name exactly.
"""

import json
import logging
from typing import Optional

import keyring

from sanduhr import paths

SERVICE = "com.626labs.sanduhr"
_ACCOUNT_SESSION = "sessionKey"
_ACCOUNT_CF = "cf_clearance"

_log = logging.getLogger(__name__)


def load() -> dict:
    """Return stored credentials as {session_key, cf_clearance}."""
    return {
        "session_key": keyring.get_password(SERVICE, _ACCOUNT_SESSION),
        "cf_clearance": keyring.get_password(SERVICE, _ACCOUNT_CF),
    }


def save(session_key: Optional[str] = None, cf_clearance: Optional[str] = None) -> None:
    """Write credentials. Passing None for a field leaves it untouched."""
    if session_key is not None:
        keyring.set_password(SERVICE, _ACCOUNT_SESSION, session_key)
    if cf_clearance is not None:
        keyring.set_password(SERVICE, _ACCOUNT_CF, cf_clearance)


def clear() -> None:
    """Remove all stored credentials. Used on uninstall."""
    for account in (_ACCOUNT_SESSION, _ACCOUNT_CF):
        try:
            keyring.delete_password(SERVICE, account)
        except keyring.errors.PasswordDeleteError:
            pass


def migrate_from_v1() -> dict:
    """Migrate v1's plaintext config + history into v2 storage.

    Returns {"migrated": bool, "session_key": bool, "theme": bool, "history": bool}.
    """
    legacy = paths.legacy_config_file()
    if not legacy.exists():
        return {"migrated": False, "session_key": False, "theme": False, "history": False}

    result = {"migrated": True, "session_key": False, "theme": False, "history": False}

    try:
        cfg = json.loads(legacy.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as e:
        _log.warning("v1 config unreadable, skipping migration: %s", e)
        return {"migrated": False, "session_key": False, "theme": False, "history": False}

    if cfg.get("session_key"):
        save(session_key=cfg["session_key"])
        result["session_key"] = True

    if cfg.get("theme"):
        settings_path = paths.settings_file()
        settings = {}
        if settings_path.exists():
            try:
                settings = json.loads(settings_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                settings = {}
        settings["theme"] = cfg["theme"]
        settings_path.write_text(json.dumps(settings), encoding="utf-8")
        result["theme"] = True

    legacy_hist = paths.legacy_history_file()
    if legacy_hist.exists():
        try:
            paths.history_file().write_text(
                legacy_hist.read_text(encoding="utf-8"), encoding="utf-8"
            )
            result["history"] = True
        except OSError as e:
            _log.warning("Failed to copy v1 history: %s", e)

    try:
        legacy.unlink()
    except OSError as e:
        _log.warning("Could not delete v1 config: %s", e)

    _log.info("v1 -> v2 migration complete: %s", result)
    return result

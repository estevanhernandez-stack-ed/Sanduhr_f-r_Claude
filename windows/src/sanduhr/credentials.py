"""Credential storage — delegates to the multi-account registry in accounts.py.

Public API (load / save / clear / migrate_from_v1) is preserved so existing
callers in widget.py and settings_dialog.py continue to work unchanged.
Operates on the ACTIVE account by default. On first save with no active
account, auto-creates a 'Personal' account (first-time sign-in flow).
"""

import json
import logging
from typing import Optional

from sanduhr import accounts, paths

_log = logging.getLogger(__name__)

# Preserved constant — some callers may import it directly.
SERVICE = accounts.SERVICE
_DEFAULT_ACCOUNT_NAME = "Personal"


def load() -> dict:
    """Return active account's credentials as {session_key, cf_clearance}.

    Returns {None, None} if no active account is configured.
    """
    active = accounts.get_active()
    if active is None:
        return {"session_key": None, "cf_clearance": None}
    return accounts.load_credentials(active)


def save(session_key: Optional[str] = None, cf_clearance: Optional[str] = None) -> None:
    """Save credentials into the active account.

    If no active account exists AND a session_key is provided, auto-create
    a 'Personal' account with these credentials. This preserves the
    pre-multi-account first-time sign-in UX (settings dialog → paste key →
    save → account exists).

    No-op if there's no active account AND no session_key.
    """
    active = accounts.get_active()
    if active is None:
        if not session_key:
            return
        accounts.add_account(
            _DEFAULT_ACCOUNT_NAME,
            session_key=session_key,
            cf_clearance=cf_clearance,
        )
        return
    accounts.save_credentials(
        active, session_key=session_key, cf_clearance=cf_clearance
    )


def clear() -> None:
    """Remove the active account from the registry.

    Used by the sign-out flow (blank-sessionKey save). Removes the active
    account's keyring entries; if other accounts exist, advances active
    pointer to the first remaining one. No-op if no active account.

    Note: this does NOT wipe the per-account history file. Callers handle
    that explicitly (see widget._on_credentials_cleared).
    """
    active = accounts.get_active()
    if active is None:
        return
    accounts.remove_account(active)


def migrate_from_v1() -> dict:
    """Migrate v1's plaintext config + history into v2 storage.

    After v1 migration, also runs accounts.migrate_legacy() to promote any
    pre-v2.2.0 single-slot keyring credentials to a 'Personal' account.

    Returns a status dict:
        {
            "migrated":       bool — whether any v1 migration happened
            "session_key":    bool — whether the session key was copied to keyring
            "theme":          bool — whether the theme preference was copied
            "history":        bool — whether history.json was copied
            "legacy_cleaned": bool — whether the plaintext v1 config was deleted.
                                     FALSE means the v1 plaintext credential file
                                     is still on disk — the caller (and the user)
                                     should be told so they can clean it up.
        }
    """
    base = {
        "migrated": False,
        "session_key": False,
        "theme": False,
        "history": False,
        "legacy_cleaned": False,
    }

    legacy = paths.legacy_config_file()
    if not legacy.exists():
        # No v1 config to migrate. Still attempt v2.0.x → v2.2.0 promotion.
        accounts.migrate_legacy()
        return base

    try:
        cfg = json.loads(legacy.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as e:
        _log.warning("v1 config unreadable, skipping migration: %s", e)
        accounts.migrate_legacy()
        return base

    result = dict(base, migrated=True)

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
        result["legacy_cleaned"] = True
    except OSError as e:
        _log.warning(
            "Could not delete v1 config at %s; plaintext credentials remain on disk. "
            "User should manually delete after confirming v2 works: %s",
            legacy, e,
        )

    # In case any v2.0.x keyring slots somehow co-exist (edge case), promote them.
    accounts.migrate_legacy()

    _log.info("v1 -> v2 migration complete: %s", result)
    return result

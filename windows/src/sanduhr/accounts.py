"""Multi-account registry layered on the OS keyring.

Stores per-account credentials under named slots:
    sessionKey:{label}, cf_clearance:{label}

And the registry under fixed slots:
    accounts:list   — JSON array of label strings
    accounts:active — label of the currently active account

Migration: legacy single-slot installs (sessionKey, cf_clearance without label)
auto-promote to a 'Personal' account on first call to migrate_legacy().
"""

import json
import re
from typing import Optional

import keyring
import keyring.errors

SERVICE = "com.626labs.sanduhr"
_LIST_SLOT = "accounts:list"
_ACTIVE_SLOT = "accounts:active"
# These two are KEYRING SLOT NAMES (storage identifiers), not credential
# values. They must match exactly what v2.0.x / v2.1.x wrote so that
# migrate_legacy() can read pre-v2.2.0 credentials from those slots. Do
# not rename or treat as secrets.
_LEGACY_SESSION_SLOT = "sessionKey"  # pragma: allowlist secret
_LEGACY_CF_SLOT = "cf_clearance"  # pragma: allowlist secret
_LABEL_RE = re.compile(r"^[A-Za-z0-9 _-]{1,32}$")


def _validate_label(label: str) -> None:
    if not _LABEL_RE.match(label):
        raise ValueError(
            f"Invalid account label {label!r}. Must be 1-32 chars, "
            f"letters/digits/space/underscore/hyphen only."
        )


def _read_list() -> list[str]:
    raw = keyring.get_password(SERVICE, _LIST_SLOT)
    return json.loads(raw) if raw else []


def _write_list(labels: list[str]) -> None:
    keyring.set_password(SERVICE, _LIST_SLOT, json.dumps(labels))


def _delete_safely(slot: str) -> None:
    try:
        keyring.delete_password(SERVICE, slot)
    except keyring.errors.PasswordDeleteError:
        pass


def list_accounts() -> list[str]:
    return _read_list()


def get_active() -> Optional[str]:
    return keyring.get_password(SERVICE, _ACTIVE_SLOT)


def set_active(label: str) -> None:
    if label not in _read_list():
        raise ValueError(f"Account {label!r} not in registry")
    keyring.set_password(SERVICE, _ACTIVE_SLOT, label)


def add_account(
    label: str,
    session_key: str,
    cf_clearance: Optional[str] = None,
) -> None:
    _validate_label(label)
    labels = _read_list()
    if label in labels:
        raise ValueError(f"Account {label!r} already exists")
    keyring.set_password(SERVICE, f"sessionKey:{label}", session_key)
    if cf_clearance:
        keyring.set_password(SERVICE, f"cf_clearance:{label}", cf_clearance)
    labels.append(label)
    _write_list(labels)
    if get_active() is None:
        set_active(label)


def remove_account(label: str) -> None:
    labels = _read_list()
    if label not in labels:
        return
    _delete_safely(f"sessionKey:{label}")
    _delete_safely(f"cf_clearance:{label}")
    labels.remove(label)
    _write_list(labels)
    if get_active() == label:
        if labels:
            set_active(labels[0])
        else:
            _delete_safely(_ACTIVE_SLOT)


def rename_account(old: str, new: str) -> None:
    _validate_label(new)
    labels = _read_list()
    if old not in labels:
        raise ValueError(f"Account {old!r} not in registry")
    if new in labels:
        raise ValueError(f"Account {new!r} already exists")
    creds = load_credentials(old)
    if creds["session_key"]:
        keyring.set_password(SERVICE, f"sessionKey:{new}", creds["session_key"])
    if creds["cf_clearance"]:
        keyring.set_password(SERVICE, f"cf_clearance:{new}", creds["cf_clearance"])
    _delete_safely(f"sessionKey:{old}")
    _delete_safely(f"cf_clearance:{old}")
    labels[labels.index(old)] = new
    _write_list(labels)
    if get_active() == old:
        set_active(new)


def load_credentials(label: str) -> dict:
    return {
        "session_key": keyring.get_password(SERVICE, f"sessionKey:{label}"),
        "cf_clearance": keyring.get_password(SERVICE, f"cf_clearance:{label}"),
    }


def save_credentials(
    label: str,
    session_key: Optional[str] = None,
    cf_clearance: Optional[str] = None,
) -> None:
    if label not in _read_list():
        raise ValueError(f"Account {label!r} not in registry")
    if session_key is not None:
        keyring.set_password(SERVICE, f"sessionKey:{label}", session_key)
    if cf_clearance is not None:
        keyring.set_password(SERVICE, f"cf_clearance:{label}", cf_clearance)


def migrate_legacy(default_name: str = "Personal") -> bool:
    """Promote legacy single-slot creds to a named account.

    Returns True if migration ran, False if it was a no-op (registry already
    populated, or no legacy creds found).
    """
    if _read_list():
        return False
    legacy_session = keyring.get_password(SERVICE, _LEGACY_SESSION_SLOT)
    if not legacy_session:
        return False
    legacy_cf = keyring.get_password(SERVICE, _LEGACY_CF_SLOT)
    add_account(default_name, session_key=legacy_session, cf_clearance=legacy_cf)
    _delete_safely(_LEGACY_SESSION_SLOT)
    _delete_safely(_LEGACY_CF_SLOT)
    return True

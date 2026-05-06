# Multi-Account Support Implementation Plan (v2.2.0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one Sanduhr install track usage across multiple Claude accounts (e.g., personal + work). The widget shows ONE active account at a time (small surface, can't render both well). The History tab supports both per-account views and an aggregated view that overlays all accounts on a single chart per tier, color-coded.

**Architecture:** All credentials remain in OS keyring; we move from single-slot to named-slot storage with an "active account" pointer. Per-account history fork: each account gets its own `history.{label}.json` file, keeping per-account wipe trivial and sidestepping the "what does aggregated utilization mean" question for storage. Aggregation happens at chart render-time by overlaying multiple lines per tier. Existing single-account installs migrate silently on first v2.2.0 launch — legacy `sessionKey` slot becomes the "Personal" account; legacy `history.json` becomes `history.Personal.json`. No data loss, no user prompt.

**Bonus (small additive):** Track `extra_usage` (API credit spend) as a tier while we're in the fetcher. It's actively populated, has a different shape (`used_credits / monthly_limit`, currency), and doesn't fit the percentage-utilization assumption — render as a percentage where percent = `utilization` field already in the response.

**Routines tier:** Not in this plan. The `/usage` endpoint doesn't return a Routines key. The "0/15" the user saw on claude.ai is skill-quota tracking (e.g., `/insights`), surfaced from a different endpoint and a different shape. Tracking skill quotas is a separate future PR.

**Tech Stack:** Python 3.11+, PySide6 (Qt 6), keyring for credential storage, pytest + pytest-qt for tests. Version bump to v2.2.0.

---

## File Structure

**Files created:**

- `windows/src/sanduhr/accounts.py` — account registry: `list_accounts()`, `add_account(name, session_key, cf_clearance)`, `remove_account(name)`, `rename_account(old, new)`, `get_active()`, `set_active(name)`, `migrate_legacy()`. Wraps the keyring named-slot pattern.
- `windows/src/sanduhr/accounts_dialog.py` — Qt widgets for the new "Accounts" tab. List view + add/rename/remove/set-active buttons.
- `windows/tests/test_accounts.py` — registry CRUD + legacy migration tests.
- `windows/tests/test_history_per_account.py` — per-account history routing + legacy migration tests.
- `windows/tests/test_aggregated_chart.py` — aggregated chart rendering tests.

**Files modified:**

- `windows/src/sanduhr/credentials.py` — keep `SERVICE` constant; deprecate `_ACCOUNT_SESSION` / `_ACCOUNT_CF` direct access; add account-aware `load(account: str)`, `save(account: str, ...)`, `clear(account: str)`. Keep legacy `load()` / `save()` / `clear()` as thin wrappers around the active account for back-compat during transition.
- `windows/src/sanduhr/history.py` — accept optional `account` param on `load_history`, `save_history`, `append`, `load`, `load_window`, `clear_all`. When omitted, default to active account. Add `aggregate_window(tier_key, days)` returning `{account: [points]}` for chart overlay.
- `windows/src/sanduhr/paths.py` — add `history_file_for(account: str)` that returns `history.{account}.json`. Keep `history_file()` returning the legacy path for migration purposes only.
- `windows/src/sanduhr/fetcher.py` — fetch active account; append history under that account label. Add `extra_usage` to `_HISTORY_TIERS`.
- `windows/src/sanduhr/history_chart.py` — accept an `account: str | None` param. None = aggregate view (overlay all accounts as colored lines per tier). Add color-mapping per account.
- `windows/src/sanduhr/settings_dialog.py` — add "Accounts" tab. Wire History tab account-selector dropdown ("All accounts" + each named account).
- `windows/src/sanduhr/widget.py` — small active-account label below the bars (e.g., "Personal"). Click cycles to next account or opens Accounts tab. `_on_credentials_cleared` becomes account-scoped (clears just the active account).
- `windows/src/sanduhr/__init__.py` — `__version__ = "2.2.0"`.
- `windows/pyproject.toml` — version 2.1.0 → 2.2.0.
- `windows/src/sanduhr/csv_export.py` — add an `account` column to CSV output. Existing tests adjusted.
- `windows/tests/test_csv_export.py` — assertions updated for new column.
- `windows/tests/test_history.py` — assertions updated for active-account default behavior.
- `windows/tests/test_clear_credentials.py` — clears just the active account, not all accounts.
- `docs/PRIVACY.md` — note that multiple account credentials may be stored if the user adds more than one.
- `SECURITY.md` — clarify that the keyring-stored credentials may include multiple session keys, one per named account.
- `CHANGELOG.md` — new v2.2.0 entry.
- `README.md` — Features bullet ("Multi-account support — track multiple Claude accounts in one install"); roadmap update.

**Files not touched:**

- `mac/` — Mac parity tracked separately; this plan is Windows-only per established pattern.
- Existing sparkline / ghost / horizon / breath / focus / game code — unchanged.
- `windows/src/sanduhr/api.py`, `pacing.py` — no changes.
- `windows/scripts/sniff_usage.py` — kept as-is (a permanent dev tool committed in this PR).

---

## Task 1: Account registry (keyring named-slot storage)

**Files:**
- Create: `windows/src/sanduhr/accounts.py`
- Create: `windows/tests/test_accounts.py`
- Modify: `windows/src/sanduhr/credentials.py`

**Architecture decisions:**

- **Slot naming:** `sessionKey:{label}` and `cf_clearance:{label}` for per-account creds. Two registry slots: `accounts:list` (JSON array of label strings) and `accounts:active` (label string).
- **Label rules:** non-empty, max 32 chars, restricted to `[A-Za-z0-9 _-]` (avoids keyring weirdness). Display name is the label; no separate display field.
- **Migration:** on first launch where `accounts:list` is missing AND legacy `sessionKey` slot exists → migrate to `[Personal]` with that name as active. Silent. If neither exists, no-op.

- [ ] **Step 1: Read existing `credentials.py` to confirm current keyring shape**

Run: `cat windows/src/sanduhr/credentials.py`
Expected: confirms `SERVICE = "com.626labs.sanduhr"`, `_ACCOUNT_SESSION = "sessionKey"`, `_ACCOUNT_CF = "cf_clearance"`, plus `load()` / `save()` / `clear()` functions.

- [ ] **Step 2: Write failing tests for the account registry**

Create `windows/tests/test_accounts.py`:

```python
"""Tests for the multi-account registry layered on top of keyring."""

import pytest

# Use a fake in-memory keyring backend so tests don't touch the real keychain.
@pytest.fixture(autouse=True)
def _fake_keyring(monkeypatch):
    import keyring
    store = {}

    class FakeBackend:
        priority = 100
        def get_password(self, service, username):
            return store.get((service, username))
        def set_password(self, service, username, password):
            store[(service, username)] = password
        def delete_password(self, service, username):
            if (service, username) not in store:
                raise keyring.errors.PasswordDeleteError("not found")
            del store[(service, username)]

    monkeypatch.setattr(keyring, "get_keyring", lambda: FakeBackend())
    monkeypatch.setattr(keyring, "get_password", lambda s, u: store.get((s, u)))
    monkeypatch.setattr(keyring, "set_password", lambda s, u, p: store.update({(s, u): p}))
    def _del(s, u):
        if (s, u) not in store:
            raise keyring.errors.PasswordDeleteError("not found")
        del store[(s, u)]
    monkeypatch.setattr(keyring, "delete_password", _del)
    yield store


def test_empty_registry_returns_no_accounts():
    from sanduhr import accounts
    assert accounts.list_accounts() == []
    assert accounts.get_active() is None


def test_add_account_persists_and_sets_active_when_first():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1", cf_clearance=None)
    assert accounts.list_accounts() == ["Personal"]
    assert accounts.get_active() == "Personal"


def test_add_second_account_does_not_change_active():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    assert accounts.list_accounts() == ["Personal", "Work"]
    assert accounts.get_active() == "Personal"


def test_set_active_changes_pointer():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    accounts.set_active("Work")
    assert accounts.get_active() == "Work"


def test_remove_account_clears_creds_and_advances_active_if_needed():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    accounts.remove_account("Personal")
    assert accounts.list_accounts() == ["Work"]
    assert accounts.get_active() == "Work"
    # Credentials for the removed account are gone
    assert accounts.load_credentials("Personal") == {"session_key": None, "cf_clearance": None}


def test_remove_last_account_leaves_no_active():
    from sanduhr import accounts
    accounts.add_account("Solo", session_key="placeholder-1")
    accounts.remove_account("Solo")
    assert accounts.list_accounts() == []
    assert accounts.get_active() is None


def test_rename_account_updates_list_active_and_keys():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1", cf_clearance="cf-1")
    accounts.rename_account("Personal", "Home")
    assert accounts.list_accounts() == ["Home"]
    assert accounts.get_active() == "Home"
    assert accounts.load_credentials("Home") == {"session_key": "placeholder-1", "cf_clearance": "cf-1"}


def test_invalid_label_rejected():
    from sanduhr import accounts
    with pytest.raises(ValueError):
        accounts.add_account("", session_key="sk")
    with pytest.raises(ValueError):
        accounts.add_account("a" * 33, session_key="sk")
    with pytest.raises(ValueError):
        accounts.add_account("bad/name", session_key="sk")


def test_duplicate_label_rejected():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    with pytest.raises(ValueError):
        accounts.add_account("Personal", session_key="placeholder-2")


def test_migrate_legacy_promotes_existing_creds_to_personal():
    """Legacy sessionKey slot becomes the 'Personal' account; legacy slots cleaned up."""
    import keyring
    from sanduhr import accounts
    keyring.set_password("com.626labs.sanduhr", "sessionKey", "placeholder-legacy")
    keyring.set_password("com.626labs.sanduhr", "cf_clearance", "cf-legacy")
    migrated = accounts.migrate_legacy()
    assert migrated is True
    assert accounts.list_accounts() == ["Personal"]
    assert accounts.get_active() == "Personal"
    assert accounts.load_credentials("Personal") == {"session_key": "placeholder-legacy", "cf_clearance": "cf-legacy"}
    assert keyring.get_password("com.626labs.sanduhr", "sessionKey") is None
    assert keyring.get_password("com.626labs.sanduhr", "cf_clearance") is None


def test_migrate_legacy_noop_when_already_migrated():
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    assert accounts.migrate_legacy() is False


def test_migrate_legacy_noop_when_no_creds():
    from sanduhr import accounts
    assert accounts.migrate_legacy() is False
```

- [ ] **Step 3: Run tests — verify they fail**

Run: `cd windows && python -m pytest tests/test_accounts.py -v`
Expected: FAIL — `accounts` module does not exist.

- [ ] **Step 4: Implement `accounts.py`**

Create `windows/src/sanduhr/accounts.py`:

```python
"""Multi-account registry layered on the OS keyring.

Stores: sessionKey:{label}, cf_clearance:{label} for per-account credentials.
       accounts:list (JSON array), accounts:active (label string) for registry.

Migration: legacy single-slot installs (sessionKey, cf_clearance without label)
auto-promote to a 'Personal' account on first call to migrate_legacy().
"""

import json
import re
from typing import Optional

import keyring

SERVICE = "com.626labs.sanduhr"
_LIST_SLOT = "accounts:list"
_ACTIVE_SLOT = "accounts:active"
_LEGACY_SESSION_SLOT = "sessionKey"
_LEGACY_CF_SLOT = "cf_clearance"
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
    for slot in (f"sessionKey:{label}", f"cf_clearance:{label}"):
        try:
            keyring.delete_password(SERVICE, slot)
        except keyring.errors.PasswordDeleteError:
            pass
    labels.remove(label)
    _write_list(labels)
    if get_active() == label:
        new_active = labels[0] if labels else None
        if new_active:
            set_active(new_active)
        else:
            try:
                keyring.delete_password(SERVICE, _ACTIVE_SLOT)
            except keyring.errors.PasswordDeleteError:
                pass


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
    for slot in (f"sessionKey:{old}", f"cf_clearance:{old}"):
        try:
            keyring.delete_password(SERVICE, slot)
        except keyring.errors.PasswordDeleteError:
            pass
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
    """Promote legacy single-slot creds to a named account. Returns True if migrated."""
    if _read_list():
        return False
    legacy_session = keyring.get_password(SERVICE, _LEGACY_SESSION_SLOT)
    if not legacy_session:
        return False
    legacy_cf = keyring.get_password(SERVICE, _LEGACY_CF_SLOT)
    add_account(default_name, session_key=legacy_session, cf_clearance=legacy_cf)
    for slot in (_LEGACY_SESSION_SLOT, _LEGACY_CF_SLOT):
        try:
            keyring.delete_password(SERVICE, slot)
        except keyring.errors.PasswordDeleteError:
            pass
    return True
```

- [ ] **Step 5: Run tests — verify they pass**

Run: `cd windows && python -m pytest tests/test_accounts.py -v`
Expected: PASS — all 11 tests green.

- [ ] **Step 6: Update `credentials.py` to delegate to `accounts.py`**

Replace the body of `credentials.py` with thin wrappers that target the active account:

```python
"""Credential storage — delegates to the multi-account registry in accounts.py.

Public API is preserved so existing callers (api.py, fetcher.py, settings_dialog.py)
continue to work without changes. Operates on the ACTIVE account by default.
"""

from typing import Optional

from sanduhr import accounts

# Preserved for back-compat — callers may still import this constant.
SERVICE = accounts.SERVICE


def load() -> dict:
    """Return active account's credentials. Empty dict if no active account."""
    active = accounts.get_active()
    if active is None:
        return {"session_key": None, "cf_clearance": None}
    return accounts.load_credentials(active)


def save(session_key: Optional[str] = None, cf_clearance: Optional[str] = None) -> None:
    """Save into the active account. No-op if no active account."""
    active = accounts.get_active()
    if active is None:
        return
    accounts.save_credentials(active, session_key=session_key, cf_clearance=cf_clearance)


def clear() -> None:
    """Remove the active account's credentials by removing the account itself.

    For 'wipe everything' (uninstall flow), iterate accounts.list_accounts()
    and call accounts.remove_account() for each.
    """
    active = accounts.get_active()
    if active is None:
        return
    accounts.remove_account(active)


def migrate_from_v1() -> dict:
    """v1 → v2 migration entry point (preserved). After v1 migration, v2.2 takes over."""
    # Existing v1 → v2 migration logic stays here. After it runs (or no-ops),
    # the v2.2 multi-account migration runs:
    accounts.migrate_legacy()
    return {"session_key": False, "cf_clearance": False}
```

> **Note:** the existing `migrate_from_v1()` body has more logic (reading legacy plaintext config, etc.). Preserve that logic; just append `accounts.migrate_legacy()` at the end. Re-read the existing function and edit minimally.

- [ ] **Step 7: Re-run all credential / account tests**

Run: `cd windows && python -m pytest tests/test_accounts.py tests/test_clear_credentials.py -v`
Expected: PASS — both new and existing tests green.

---

## Task 2: Per-account history schema + migration

**Files:**
- Modify: `windows/src/sanduhr/history.py`
- Modify: `windows/src/sanduhr/paths.py`
- Create: `windows/tests/test_history_per_account.py`
- Modify: `windows/tests/test_history.py` (adjust assertions)

**Architecture decisions:**

- **One file per account:** `history.{label}.json`. Trivial per-account wipe (delete the file). Aggregation happens at chart-render time.
- **Active-account default:** all existing `history.append/load/load_window/clear_all` functions accept an optional `account` kwarg; when omitted, default to `accounts.get_active()`. Existing callers continue to work.
- **Migration:** on first call to `load_history()` (or any history function), if `history.json` (legacy) exists and `history.{active}.json` doesn't yet, rename. One-time, idempotent.
- **Aggregate query:** new `aggregate_window(tier_key, days)` returns `{account_label: [points]}` for the chart-overlay path.

- [ ] **Step 1: Read `paths.py` and `history.py` to confirm current shape**

Run: `cat windows/src/sanduhr/paths.py windows/src/sanduhr/history.py`
Expected: confirms `paths.history_file()` returns the legacy single path; `history.py` operates on `{tier_key: [points]}`.

- [ ] **Step 2: Write failing tests for per-account history**

Create `windows/tests/test_history_per_account.py`:

```python
"""Tests for per-account history routing + legacy migration."""

import json
import tempfile
import pytest
from pathlib import Path


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch, tmp_path):
    monkeypatch.setenv("APPDATA", str(tmp_path))
    yield


@pytest.fixture
def _registered_accounts(_fake_keyring):
    """Fixture from test_accounts.py — set up two accounts."""
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-p")
    accounts.add_account("Work", session_key="placeholder-w")
    yield


def test_history_writes_to_active_account_file(_registered_accounts):
    from sanduhr import accounts, history, paths
    accounts.set_active("Personal")
    history.append("five_hour", 50)
    p = paths.history_file_for("Personal")
    assert p.exists()
    data = json.loads(p.read_text())
    assert data["five_hour"][0]["v"] == 50


def test_history_per_account_isolated(_registered_accounts):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30)
    accounts.set_active("Work")
    history.append("five_hour", 70)
    accounts.set_active("Personal")
    assert history.load("five_hour") == [30]
    accounts.set_active("Work")
    assert history.load("five_hour") == [70]


def test_explicit_account_override(_registered_accounts):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30, account="Work")
    accounts.set_active("Work")
    assert history.load("five_hour") == [30]


def test_legacy_migration_renames_file_to_active_account(_registered_accounts):
    from sanduhr import accounts, history, paths
    accounts.set_active("Personal")
    legacy = paths.history_file()
    legacy.write_text(json.dumps({"five_hour": [{"t": "2026-05-01T00:00:00+00:00", "v": 42}]}))
    history.load_history()  # triggers migration
    assert not legacy.exists()
    new = paths.history_file_for("Personal")
    assert new.exists()
    assert json.loads(new.read_text())["five_hour"][0]["v"] == 42


def test_aggregate_window_returns_all_accounts(_registered_accounts):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30)
    accounts.set_active("Work")
    history.append("five_hour", 70)
    agg = history.aggregate_window("five_hour", days=7)
    assert set(agg.keys()) == {"Personal", "Work"}
    assert len(agg["Personal"]) == 1
    assert len(agg["Work"]) == 1
    assert agg["Personal"][0]["v"] == 30
    assert agg["Work"][0]["v"] == 70


def test_clear_all_only_clears_active_account(_registered_accounts):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30)
    accounts.set_active("Work")
    history.append("five_hour", 70)
    accounts.set_active("Personal")
    history.clear_all()
    assert history.load_history() == {}
    accounts.set_active("Work")
    assert history.load("five_hour") == [70]
```

> Note: `_fake_keyring` fixture should be moved to `conftest.py` so it's shared across test files. Implementer: extract during Step 4.

- [ ] **Step 3: Run tests — verify they fail**

Run: `cd windows && python -m pytest tests/test_history_per_account.py -v`
Expected: FAIL — `paths.history_file_for` doesn't exist; `history` doesn't accept `account` param.

- [ ] **Step 4: Implement per-account paths + history**

Modify `windows/src/sanduhr/paths.py`:

```python
def history_file_for(account: str) -> Path:
    """Per-account history file: history.{label}.json."""
    return appdata_dir() / f"history.{account}.json"
```

Keep `history_file()` returning the legacy path — used by migration.

Modify `windows/src/sanduhr/history.py`:

- All public functions accept an optional `account: Optional[str] = None` kwarg.
- When `account` is None, resolve to `accounts.get_active()`.
- `load_history()` first checks for legacy migration: if `paths.history_file()` exists and `paths.history_file_for(active)` does not, rename.
- New `aggregate_window(tier_key, days)` — iterate `accounts.list_accounts()`, call `load_window` per account, return dict.

```python
from sanduhr import accounts, paths

def _resolve_account(account: Optional[str]) -> Optional[str]:
    return account if account is not None else accounts.get_active()


def _migrate_legacy_history_if_needed(active: Optional[str]) -> None:
    if active is None:
        return
    legacy = paths.history_file()
    new = paths.history_file_for(active)
    if legacy.exists() and not new.exists():
        legacy.rename(new)


def load_history(account: Optional[str] = None) -> dict:
    active = _resolve_account(account)
    _migrate_legacy_history_if_needed(active)
    if active is None:
        return {}
    p = paths.history_file_for(active)
    if not p.exists():
        return {}
    return json.loads(p.read_text())


def save_history(data: dict, account: Optional[str] = None) -> None:
    active = _resolve_account(account)
    if active is None:
        return  # nothing to save against
    p = paths.history_file_for(active)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(data))


def append(tier_key: str, util: int, account: Optional[str] = None) -> list[int]:
    data = load_history(account=account)
    series = data.get(tier_key, [])
    series.append({"t": _now_iso(), "v": util})
    series = series[-MAX_HISTORY:]
    data[tier_key] = series
    save_history(data, account=account)
    return [p["v"] for p in series]


def load(tier_key: str, account: Optional[str] = None) -> list[int]:
    data = load_history(account=account)
    return [p["v"] for p in data.get(tier_key, [])]


def load_window(tier_key: str, days: int, account: Optional[str] = None) -> list[dict]:
    data = load_history(account=account)
    # ... existing windowing logic ...


def clear_all(account: Optional[str] = None) -> None:
    active = _resolve_account(account)
    if active is None:
        return
    p = paths.history_file_for(active)
    try:
        p.unlink()
    except FileNotFoundError:
        pass


def aggregate_window(tier_key: str, days: int) -> dict[str, list[dict]]:
    """Return {account_label: [points]} across all registered accounts."""
    return {
        label: load_window(tier_key, days, account=label)
        for label in accounts.list_accounts()
    }
```

- [ ] **Step 5: Move `_fake_keyring` fixture to `conftest.py`**

Extract the in-memory keyring fixture from `test_accounts.py` into `windows/tests/conftest.py` so per-account history tests can use it.

- [ ] **Step 6: Re-run history tests**

Run: `cd windows && python -m pytest tests/test_history_per_account.py tests/test_history.py tests/test_history_retention.py -v`
Expected: PASS. Adjust legacy `test_history.py` assertions if any rely on the legacy file path.

---

## Task 3: Add `extra_usage` tier (small additive)

**Files:**
- Modify: `windows/src/sanduhr/fetcher.py`
- Modify: `windows/src/sanduhr/history_chart.py`

The `/usage` API returns `extra_usage: {is_enabled, monthly_limit, used_credits, utilization, currency}`. The `utilization` field is already 0-100 — fits the existing chart model.

- [ ] **Step 1: Add `extra_usage` to `_HISTORY_TIERS`**

Append `"extra_usage"` to the `_HISTORY_TIERS` tuple in `fetcher.py`.

- [ ] **Step 2: Add chart label**

Add to `_TIER_LABELS` in `history_chart.py`:

```python
"extra_usage": "API Credits",
```

- [ ] **Step 3: Smoke-test**

Run the app, watch `extra_usage` populate in the History chart over a few fetch cycles.

---

## Task 4: Accounts settings tab

**Files:**
- Create: `windows/src/sanduhr/accounts_dialog.py`
- Modify: `windows/src/sanduhr/settings_dialog.py`

**UI shape:**

- New "Accounts" tab between "Pacing" and "History".
- Top: list view (`QListWidget`) of account labels. Active account marked (e.g., bullet prefix or bold).
- Below the list: row of buttons — `Add…`, `Rename…`, `Set Active`, `Remove…`.
- `Add…` opens a small dialog: Name field, Session Key field (paste), Cloudflare clearance field (optional). On accept, calls `accounts.add_account()` and refreshes.
- `Remove…` requires confirmation; if it's the only account, disable the button (or warn that the app effectively becomes signed-out).
- Set Active triggers a re-fetch on the new account immediately (signal back to the widget).

- [ ] **Step 1: Implement `accounts_dialog.py`**
- [ ] **Step 2: Wire into `settings_dialog.py`** as a new tab.
- [ ] **Step 3: Add UI test** (`test_accounts_dialog.py`) for construction + basic interactions using pytest-qt.

---

## Task 5: Active-account label in widget

**Files:**
- Modify: `windows/src/sanduhr/widget.py`

- Add a small `QLabel` showing the active account name (e.g., "Personal").
- Place: below the bars, above the resets-in label.
- Click handler: cycles to next account in the registry. If only one account, no-op.
- Update label whenever active account changes (via signal from accounts_dialog).

- [ ] **Step 1: Add label widget + click handler.**
- [ ] **Step 2: Wire signal: accounts_dialog → widget.**
- [ ] **Step 3: Visual test in dev.**

---

## Task 6: History tab per-account toggle + aggregated view

**Files:**
- Modify: `windows/src/sanduhr/history_chart.py`
- Modify: `windows/src/sanduhr/settings_dialog.py`
- Create: `windows/tests/test_aggregated_chart.py`

**UI shape:**

- New `QComboBox` above the chart: "All accounts" + each account label.
- "All accounts" → chart renders multiple lines per tier, color-coded by account, with a small legend.
- Single account → chart renders just that account's data (current behavior).
- CSV export: when "All accounts" selected, exports include an `account` column. When single account, no account column (back-compat).

**Color mapping:** stable per-account palette assigned at chart-build time (e.g., colors by account-list order: blue, orange, green, purple).

- [ ] **Step 1: Add combo box to History tab.**
- [ ] **Step 2: Extend `HistoryChart` to accept `account: str | None`.**
- [ ] **Step 3: Implement aggregated-render path** — iterate `aggregate_window`, draw one line per account per tier.
- [ ] **Step 4: Tests for aggregated rendering** — assert correct color per account, correct line count.
- [ ] **Step 5: CSV export update** — add `account` column when aggregated.

---

## Task 7: Sign-out scope behavior

**Files:**
- Modify: `windows/src/sanduhr/widget.py`
- Modify: `windows/tests/test_clear_credentials.py`

**Behavior:**

- "Save blank session key" (the existing sign-out gesture) clears ONLY the active account. The `_on_credentials_cleared` handler now:
  1. Removes the active account via `accounts.remove_account()`.
  2. Wipes `history.{label}.json` for that account.
  3. If there are remaining accounts, re-fetches with the new active account.
  4. If no accounts remain, shows the un-signed-in state (existing flow).
- A new explicit "Remove all accounts" action lives in the Accounts tab (separate Tasks 4 work).

- [ ] **Step 1: Update widget._on_credentials_cleared.**
- [ ] **Step 2: Update test_clear_credentials.py** for the new account-scoped behavior.

---

## Task 8: Docs + version bump

- [ ] Bump `windows/pyproject.toml` and `windows/src/sanduhr/__init__.py` to `2.2.0`.
- [ ] `CHANGELOG.md` — new entry at top:
  ```
  ## v2.2.0 — Multi-account support
  - Track multiple Claude accounts in one install (personal + work + …).
  - New Settings → Accounts tab: add / rename / set-active / remove.
  - History tab: per-account view + aggregated overlay across accounts.
  - Active-account label in the widget; click to cycle accounts.
  - Sign-out (blank sessionKey) now scoped to the active account only.
  - New "API Credits" (extra_usage) tier in history.
  - Migration: existing single-account installs auto-promote to a "Personal" account on first launch.
  ```
- [ ] `docs/PRIVACY.md` — note that multiple session keys may be stored in keyring, one per named account. Each account's history file is stored separately under `history.{label}.json` in `%APPDATA%`.
- [ ] `SECURITY.md` — clarify the multi-account keyring shape.
- [ ] `README.md` — Features bullet ("Multi-account support — track multiple Claude accounts in one install"); roadmap update.

---

## Task 9: Open the PR

- [ ] Push branch: `git push -u origin feature/multi-account`.
- [ ] `gh pr create --base main --head feature/multi-account` with title `feat(accounts): multi-account support + extra_usage tier (v2.2.0)` and a body summarizing all subsystems + a manual test plan.

---

## Notes / Open items

- **Routines tracking (parked, candidate for v2.3.0):** Routines is a brand-new Claude Code feature (shipped 2026-04-14, research preview): cloud-hosted scheduled / API / GitHub-triggered Claude Code runs. Daily count quota: Pro 5/day, Max 15/day, Team & Enterprise 25/day. The `/usage` endpoint (what Sanduhr fetches) does NOT include it — it surfaces in a different part of claude.ai/settings/usage as "0/15" daily-routine quota, populated from a different API endpoint we haven't sniffed yet. Adding Routines tracking would mean: (1) discovering the routines-usage endpoint, (2) accommodating count-based rendering (`used / limit` instead of utilization 0-100%), (3) deciding whether to render alongside the existing time-based tiers or in a separate "Skills & Quotas" panel. Out of scope for v2.2.0 — proposed v2.3.0 PR.
- **`tangelo` key:** new mystery key in the API response, currently null. Same family as `iguana_necktie` (internal codename for an unlaunched/hidden tier). Leave untracked — pattern matches `iguana_necktie`.
- **Aggregation semantics:** "summing" utilization percentages across accounts isn't strictly meaningful (50% + 50% ≠ 100%). The aggregated view shows OVERLAID lines, not a sum, so each account's utilization remains visible in its own y-axis position.
- **API lag (known limitation, not in v2.2.0 scope):** Anthropic's `/usage` endpoint updates slowly and lags actual consumption. Community tool `ccusage` (`npx ccusage@latest`) reads local Claude Code logs for a more real-time view. Future PR could add a local-logs data source alongside the API for tighter visibility, but that's a separate feature — not part of multi-account work.

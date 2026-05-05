"""Per-account usage history storage.

Append-only rolling window of utilization percentages per tier, persisted
per account at %APPDATA%\\Sanduhr\\history.{account}.json. Functions
default to the active account; pass `account=...` to target a specific one.

Legacy single-file history (history.json from v2.0.x / v2.1.x) is migrated
to history.{active}.json on first access — one-shot, idempotent. If the
per-account file already exists, the legacy file is left alone.

When no active account is configured, all reads return empty and writes
no-op silently — there's nowhere to route the data.
"""

import json
from datetime import datetime, timedelta, timezone
from typing import Optional

from sanduhr import accounts, paths

MAX_HISTORY = 8640  # 30 days × 288 five-minute ticks per day.
# Sizing assumes the 5-min fetch cadence from fetcher._tick. Across 8
# tiers that's ~3.5 MB per history.{account}.json — fine on modern disks.


def _resolve_account(account: Optional[str]) -> Optional[str]:
    return account if account is not None else accounts.get_active()


def _migrate_legacy_if_needed(active: Optional[str]) -> None:
    """Rename history.json → history.{active}.json on first access.

    Idempotent: no-op if there's no active account, no legacy file, or the
    per-account file already exists (don't clobber the active account's
    real data with an older legacy snapshot)."""
    if active is None:
        return
    legacy = paths.history_file()
    target = paths.history_file_for(active)
    if not legacy.exists() or target.exists():
        return
    legacy.rename(target)


def _read_account_file(account: str) -> dict:
    p = paths.history_file_for(account)
    if not p.exists():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def _write_account_file(account: str, data: dict) -> None:
    paths.history_file_for(account).write_text(json.dumps(data), encoding="utf-8")


def load_history(account: Optional[str] = None) -> dict:
    """Return the full history dict for the given (or active) account."""
    active = _resolve_account(account)
    _migrate_legacy_if_needed(active)
    if active is None:
        return {}
    return _read_account_file(active)


def save_history(data: dict, account: Optional[str] = None) -> None:
    """Overwrite the history file for the given (or active) account.

    Internal/test helper — external callers should prefer `append()`.
    No-op when there's no active account and `account` wasn't passed."""
    active = _resolve_account(account)
    if active is None:
        return
    _write_account_file(active, data)


def append(tier_key: str, util: int, account: Optional[str] = None) -> list[int]:
    """Record a new utilization data point for the given (or active)
    account. Returns the current series. No-op (returns []) when no
    active account is configured."""
    active = _resolve_account(account)
    if active is None:
        return []
    _migrate_legacy_if_needed(active)
    data = _read_account_file(active)
    series = data.get(tier_key, [])
    series.append({"t": datetime.now(timezone.utc).isoformat(), "v": int(util)})
    series = series[-MAX_HISTORY:]
    data[tier_key] = series
    _write_account_file(active, data)
    return [p["v"] for p in series]


def load(tier_key: str, account: Optional[str] = None) -> list[int]:
    """Return the list of utilization values for a tier, oldest first."""
    data = load_history(account=account)
    return [p["v"] for p in data.get(tier_key, [])]


def load_window(
    tier_key: str, days: int, account: Optional[str] = None
) -> list[dict]:
    """Return history points for `tier_key` within the last `days` days.

    Points are returned in chronological order. Empty list if the tier
    has no history or the window contains no points."""
    cutoff = datetime.now(timezone.utc) - timedelta(days=days)
    all_points = load_history(account=account).get(tier_key, [])
    out = []
    for p in all_points:
        try:
            t = datetime.fromisoformat(p["t"].replace("Z", "+00:00"))
        except (ValueError, KeyError, TypeError, AttributeError):
            continue
        if t >= cutoff:
            out.append(p)
    return out


def aggregate_window(tier_key: str, days: int) -> dict[str, list[dict]]:
    """Return {account_label: [points]} across all registered accounts.

    Used by the 'All accounts' view in the History tab — overlays one
    line per account on a single chart per tier. Each account's entry is
    the same shape `load_window` returns; an account with no recorded
    points for the tier gets an empty list."""
    return {
        label: load_window(tier_key, days, account=label)
        for label in accounts.list_accounts()
    }


def clear_all(account: Optional[str] = None) -> None:
    """Wipe the history file for the given (or active) account.

    Writes an empty dict rather than unlinking — preserves the
    file-existence invariant that downstream readers depend on (and that
    test_history_retention.py locks in). Other accounts' history is left
    untouched. No-op when there's no active account and `account` wasn't
    passed."""
    active = _resolve_account(account)
    if active is None:
        return
    _write_account_file(active, {})

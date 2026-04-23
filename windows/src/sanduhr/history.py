"""Sparkline history storage.

Append-only rolling window of utilization percentages per tier,
persisted to %APPDATA%\\Sanduhr\\history.json.
"""

import json
from datetime import datetime, timedelta, timezone

from sanduhr import paths

MAX_HISTORY = 8640  # 30 days × 288 five-minute ticks per day


def _read_raw():
    path = paths.history_file()
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def _write_raw(data):
    paths.history_file().write_text(json.dumps(data), encoding="utf-8")


def load_history() -> dict:
    """Return the full history dict (tier_key -> list of {t, v} points)."""
    return _read_raw()


def save_history(data: dict) -> None:
    """Overwrite the history file with `data`."""
    _write_raw(data)


def append(tier_key: str, util: int) -> list[int]:
    """Record a new utilization data point. Returns the current series."""
    data = _read_raw()
    series = data.get(tier_key, [])
    series.append({"t": datetime.now(timezone.utc).isoformat(), "v": int(util)})
    series = series[-MAX_HISTORY:]
    data[tier_key] = series
    _write_raw(data)
    return [p["v"] for p in series]


def load(tier_key: str) -> list[int]:
    """Return the list of utilization values for a tier, oldest first."""
    data = _read_raw()
    return [p["v"] for p in data.get(tier_key, [])]


def load_window(tier_key: str, days: int) -> list[dict]:
    """Return history points for `tier_key` within the last `days` days.

    Points are returned in chronological order. Empty list if the tier
    has no history or the window contains no points."""
    cutoff = datetime.now(timezone.utc) - timedelta(days=days)
    all_points = load_history().get(tier_key, [])
    out = []
    for p in all_points:
        try:
            t = datetime.fromisoformat(p["t"].replace("Z", "+00:00"))
        except (ValueError, KeyError):
            continue
        if t >= cutoff:
            out.append(p)
    return out


def clear_all() -> None:
    """Wipe the entire history file. Used by the sign-out flow and the
    Settings → History → Clear history button."""
    save_history({})

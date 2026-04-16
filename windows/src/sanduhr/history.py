"""Sparkline history storage.

Append-only rolling window of utilization percentages per tier,
persisted to %APPDATA%\\Sanduhr\\history.json.
"""

import json
from datetime import datetime, timezone

from sanduhr import paths

MAX_POINTS = 24  # 24 points x 5 min = 2 hours


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


def append(tier_key: str, util: int) -> list[int]:
    """Record a new utilization data point. Returns the current series."""
    data = _read_raw()
    series = data.get(tier_key, [])
    series.append({"t": datetime.now(timezone.utc).isoformat(), "v": int(util)})
    series = series[-MAX_POINTS:]
    data[tier_key] = series
    _write_raw(data)
    return [p["v"] for p in series]


def load(tier_key: str) -> list[int]:
    """Return the list of utilization values for a tier, oldest first."""
    data = _read_raw()
    return [p["v"] for p in data.get(tier_key, [])]

"""Tests for the extended history retention window and query API.

History is now a 30-day rolling window (8640 5-min ticks per tier) instead
of the old 2-hour window. Query API returns filtered windows for charting.
"""

import json
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def test_retention_keeps_8640_points():
    """MAX_HISTORY must be 8640 — 30 days of 5-min ticks (30 * 288)."""
    from sanduhr import history
    assert history.MAX_HISTORY == 8640


def test_append_prunes_beyond_retention():
    """Appending the 8641st point drops the oldest."""
    from sanduhr import history
    base = datetime(2026, 1, 1, tzinfo=timezone.utc)
    data = {
        "five_hour": [
            {"t": (base + timedelta(minutes=5 * i)).isoformat(), "v": i % 100}
            for i in range(8640)
        ]
    }
    history.save_history(data)
    result = history.append("five_hour", 99)
    assert len(result) == 8640


def test_load_window_filters_by_days():
    """load_window(tier, 7) returns only the last 7 days of points."""
    from sanduhr import history
    now = datetime.now(timezone.utc)
    data = {
        "five_hour": [
            {"t": (now - timedelta(hours=i)).isoformat(), "v": i % 100}
            for i in range(240)
        ]
    }
    history.save_history(data)
    window = history.load_window("five_hour", days=7)
    assert 160 <= len(window) <= 170


def test_load_window_returns_empty_for_unknown_tier():
    """Querying a tier with no history returns an empty list."""
    from sanduhr import history
    assert history.load_window("iguana_necktie", days=7) == []


def test_clear_all_wipes_file():
    """clear_all() removes every tier's history."""
    from sanduhr import history
    history.save_history({"five_hour": [{"t": "2026-04-21T00:00:00+00:00", "v": 50}]})
    history.clear_all()
    assert history.load_history() == {}


def test_clear_all_preserves_file():
    """clear_all writes {} rather than unlinking — preserves the file-
    existence invariant that downstream readers (load, load_window) depend
    on. Locks this behavior in against a future 'simplification' that
    swaps to Path.unlink(missing_ok=True)."""
    from sanduhr import history, paths
    history.save_history({"five_hour": [{"t": "2026-04-21T00:00:00+00:00", "v": 50}]})
    history.clear_all()
    assert paths.history_file().exists()

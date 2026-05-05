"""Tests for sanduhr.history."""

import json
from datetime import datetime, timedelta, timezone

from sanduhr import history, paths


def test_append_creates_file(tmp_appdata):
    history.append("five_hour", 42)
    assert paths.history_file().exists()


def test_append_adds_entry(tmp_appdata):
    history.append("five_hour", 42)
    values = history.load("five_hour")
    assert values == [42]


def test_append_multiple_values(tmp_appdata):
    for v in [10, 20, 30]:
        history.append("five_hour", v)
    assert history.load("five_hour") == [10, 20, 30]


def test_append_different_tiers_isolated(tmp_appdata):
    history.append("five_hour", 10)
    history.append("seven_day", 99)
    assert history.load("five_hour") == [10]
    assert history.load("seven_day") == [99]


def test_load_missing_tier_returns_empty_list(tmp_appdata):
    assert history.load("nonexistent") == []


def test_load_missing_file_returns_empty_list(tmp_appdata):
    assert history.load("five_hour") == []


def test_append_rolls_over_at_max(tmp_appdata):
    # Seed the file at MAX_HISTORY then append a few more — avoids doing
    # 8645 individual read-modify-write cycles just to exercise pruning.
    base = datetime(2026, 1, 1, tzinfo=timezone.utc)
    seed = {"five_hour": [{"t": (base + timedelta(minutes=5 * i)).isoformat(), "v": i}
                          for i in range(history.MAX_HISTORY)]}
    history.save_history(seed)
    for i in range(history.MAX_HISTORY, history.MAX_HISTORY + 5):
        history.append("five_hour", i)
    values = history.load("five_hour")
    assert len(values) == history.MAX_HISTORY
    assert values[-1] == history.MAX_HISTORY + 4


def test_corrupt_file_does_not_crash(tmp_appdata):
    paths.history_file().write_text("not valid json {{{")
    assert history.load("five_hour") == []


def test_corrupt_file_repaired_on_append(tmp_appdata):
    paths.history_file().write_text("garbage")
    history.append("five_hour", 50)
    data = json.loads(paths.history_file().read_text())
    assert data["five_hour"][-1]["v"] == 50

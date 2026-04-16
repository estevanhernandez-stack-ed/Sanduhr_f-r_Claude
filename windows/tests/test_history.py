"""Tests for sanduhr.history."""

import json

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
    for i in range(history.MAX_POINTS + 5):
        history.append("five_hour", i)
    values = history.load("five_hour")
    assert len(values) == history.MAX_POINTS
    assert values[-1] == history.MAX_POINTS + 4


def test_corrupt_file_does_not_crash(tmp_appdata):
    paths.history_file().write_text("not valid json {{{")
    assert history.load("five_hour") == []


def test_corrupt_file_repaired_on_append(tmp_appdata):
    paths.history_file().write_text("garbage")
    history.append("five_hour", 50)
    data = json.loads(paths.history_file().read_text())
    assert data["five_hour"][-1]["v"] == 50

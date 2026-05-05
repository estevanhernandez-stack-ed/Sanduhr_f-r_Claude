"""Tests for the usage history CSV export.

The export produces a flat CSV that any LLM or spreadsheet can parse:
one row per (tier, timestamp) pair, columns timestamp / tier / util_pct.
"""

import csv
import tempfile
from pathlib import Path

import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def test_export_writes_header_and_rows(tmp_path):
    from sanduhr import history, csv_export
    history.save_history({
        "five_hour": [
            {"t": "2026-04-21T10:00:00+00:00", "v": 25},
            {"t": "2026-04-21T10:05:00+00:00", "v": 30},
        ],
        "seven_day": [
            {"t": "2026-04-21T10:00:00+00:00", "v": 45},
        ],
    })
    dest = tmp_path / "export.csv"
    row_count = csv_export.export_to_csv(dest)
    assert row_count == 3

    with open(dest, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    assert len(rows) == 3
    assert set(rows[0].keys()) == {"timestamp", "tier", "util_pct"}
    # Every tier appears
    tiers = {r["tier"] for r in rows}
    assert tiers == {"five_hour", "seven_day"}
    # Values preserved
    assert any(r["util_pct"] == "25" for r in rows)


def test_export_empty_history_writes_header_only(tmp_path):
    """Empty history still produces a readable CSV with just the header."""
    from sanduhr import history, csv_export
    history.save_history({})
    dest = tmp_path / "empty.csv"
    row_count = csv_export.export_to_csv(dest)
    assert row_count == 0
    with open(dest, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    assert rows == []
    # But the header row is there
    with open(dest, encoding="utf-8") as f:
        first_line = f.readline().strip()
    assert first_line == "timestamp,tier,util_pct"


def test_export_rows_are_chronological(tmp_path):
    """Rows sorted by timestamp ascending within the CSV."""
    from sanduhr import history, csv_export
    history.save_history({
        "five_hour": [
            {"t": "2026-04-21T10:05:00+00:00", "v": 30},
            {"t": "2026-04-21T10:00:00+00:00", "v": 25},
            {"t": "2026-04-21T10:10:00+00:00", "v": 35},
        ],
    })
    dest = tmp_path / "sorted.csv"
    csv_export.export_to_csv(dest)
    with open(dest, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    timestamps = [r["timestamp"] for r in rows]
    assert timestamps == sorted(timestamps)


def test_export_escapes_special_characters(tmp_path):
    """Tier keys or values with commas/quotes are CSV-escaped, not smashed.

    Current tier keys are all ASCII identifiers, so this path isn't
    exercised in production today — this test locks in correct quoting
    against future refactors (e.g. user-authored tier aliases)."""
    from sanduhr import history, csv_export
    history.save_history({
        'tricky,key"': [
            {"t": "2026-04-21T10:00:00+00:00", "v": 42},
        ],
    })
    dest = tmp_path / "escaped.csv"
    csv_export.export_to_csv(dest)
    with open(dest, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    assert rows[0]["tier"] == 'tricky,key"'

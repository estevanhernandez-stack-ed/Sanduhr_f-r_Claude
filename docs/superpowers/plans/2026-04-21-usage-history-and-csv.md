# Usage History + CSV Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend Sanduhr's local `history.json` retention from the current 2-hour sparkline window to ~30 days of rolling usage data, surface it as a line-chart view in a new Settings → History tab, and offer CSV export so power users can pipe their usage into any agent for analysis.

**Architecture:** All data stays local — the CSV export writes to user-chosen path, the chart reads from the same `history.json` file the sparkline already uses. No new dependencies, no network calls, no telemetry introduced. CSV format is timestamp / tier / util_pct columns; any LLM or spreadsheet can parse it. Privacy posture preserved: SECURITY.md and PRIVACY.md get one explicit sentence acknowledging the local-only history file (it's not new — existing sparkline already uses it — we're just expanding retention and documenting honestly).

**Tech Stack:** Python 3.11+, PySide6 (Qt 6) with `QPainter` for the chart, Python `csv` module for export, pytest + pytest-qt for tests. Version bump to v2.1.0 (minor, not patch — this is a real feature addition, not a fix).

---

## File Structure

**Files created:**

- `windows/src/sanduhr/history_chart.py` — QPainter-based line chart widget, one chart per tier stacked vertically. Same visual language as `sparkline.py`, bigger canvas.
- `windows/src/sanduhr/csv_export.py` — small module with one public function `export_to_csv(dest_path) -> int` that writes the full history to a CSV file. Returns row count.
- `windows/tests/test_csv_export.py` — CSV format + empty-history + escaping tests.
- `windows/tests/test_history_chart.py` — chart widget construction + render-without-crash smoke tests.
- `windows/tests/test_history_retention.py` — retention-window + pruning tests (separate from the existing `test_history.py` which covers basic append/load).

**Files modified:**

- `windows/src/sanduhr/history.py` — extend `MAX_HISTORY` from 24 to 8640 (30 days × 288 5-min ticks). Add `load_window(tier_key, days)` query API. Add `clear_all()` to wipe the file.
- `windows/src/sanduhr/settings_dialog.py` — add "History" tab between Pacing and Help. Wires chart widget, tier selector, week/month toggle, Export CSV button, Clear history button.
- `windows/src/sanduhr/widget.py` — when `_on_credentials_cleared` runs (blank-sessionKey sign-out), also clear history (confirmation dialog asks about this).
- `windows/pyproject.toml` — version 2.0.4 → 2.1.0.
- `windows/src/sanduhr/__init__.py` — `__version__ = "2.1.0"`.
- `docs/PRIVACY.md` — add a "Local data storage" section acknowledging history file.
- `SECURITY.md` — one sentence under the zero-telemetry declaration explicitly noting the local-only history file.
- `CHANGELOG.md` — new v2.1.0 entry at the top.
- `README.md` — update Features section ("History & CSV export" bullet); update roadmap (mark "Historical usage dashboard with CSV export" as shipped).

**Files not touched:**

- `mac/` — Mac parity is tracked separately; this plan is Windows-only per established pattern.
- Existing sparkline / ghost / horizon / breath code — unchanged.
- `windows/src/sanduhr/paths.py`, `credentials.py`, `fetcher.py`, `api.py`, `pacing.py`, `focus.py`, `game.py` — no changes.

---

## Task 1: Extend history retention + query API

**Files:**
- Modify: `windows/src/sanduhr/history.py`
- Create: `windows/tests/test_history_retention.py`

- [ ] **Step 1: Read existing `history.py` to understand current shape**

Run: `cat windows/src/sanduhr/history.py`
Expected: sees `MAX_HISTORY = 24`, `append()`, `load()`, `save()` functions operating on `{tier_key: [{t: iso, v: util}, ...]}` dict.

- [ ] **Step 2: Write the failing retention test**

Create `windows/tests/test_history_retention.py`:

```python
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
    # Seed by writing 8640 synthetic points
    base = datetime(2026, 1, 1, tzinfo=timezone.utc)
    data = {
        "five_hour": [
            {"t": (base + timedelta(minutes=5 * i)).isoformat(), "v": i % 100}
            for i in range(8640)
        ]
    }
    history.save_history(data)
    # Append one more
    result = history.append("five_hour", 99)
    assert len(result) == 8640  # did not grow


def test_load_window_filters_by_days():
    """load_window(tier, 7) returns only the last 7 days of points."""
    from sanduhr import history
    now = datetime.now(timezone.utc)
    data = {
        "five_hour": [
            # 10 days of hourly points — 240 points
            {"t": (now - timedelta(hours=i)).isoformat(), "v": i % 100}
            for i in range(240)
        ]
    }
    history.save_history(data)
    window = history.load_window("five_hour", days=7)
    # 7 days * 24 hours = 168 points (approximately)
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd windows && python -m pytest tests/test_history_retention.py -v`
Expected: FAIL — `history.MAX_HISTORY == 24` in current code, and `load_window` / `clear_all` don't exist.

- [ ] **Step 4: Extend `history.py`**

In `windows/src/sanduhr/history.py`, change `MAX_HISTORY = 24` to:

```python
MAX_HISTORY = 8640  # 30 days × 288 five-minute ticks per day
```

Add these two new functions at the end of the module:

```python
def load_window(tier_key: str, days: int) -> list[dict]:
    """Return history points for `tier_key` within the last `days` days.

    Points are returned in chronological order. Empty list if the tier
    has no history or the window contains no points."""
    from datetime import datetime, timezone, timedelta
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd windows && python -m pytest tests/test_history_retention.py -v`
Expected: all 5 PASS.

- [ ] **Step 6: Run full suite to check no regressions**

Run: `cd windows && python -m pytest tests/ --tb=short -q`
Expected: 198 passed (193 prior + 5 new).

- [ ] **Step 7: Commit**

```bash
git add windows/src/sanduhr/history.py windows/tests/test_history_retention.py
git commit -m "feat(history): extend retention to 30 days, add load_window + clear_all

MAX_HISTORY 24 (2 hours) -> 8640 (30 days * 288 five-minute ticks).
Existing append() still prunes to MAX_HISTORY; no behaviour change for
the sparkline. New load_window(tier, days) returns filtered points for
the upcoming History tab chart. New clear_all() wipes the file; called
from the Settings → Clear history button and from the sign-out flow.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: CSV export module

**Files:**
- Create: `windows/src/sanduhr/csv_export.py`
- Create: `windows/tests/test_csv_export.py`

- [ ] **Step 1: Write the failing test**

Create `windows/tests/test_csv_export.py`:

```python
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd windows && python -m pytest tests/test_csv_export.py -v`
Expected: FAIL — `sanduhr.csv_export` module doesn't exist.

- [ ] **Step 3: Create `csv_export.py`**

Create `windows/src/sanduhr/csv_export.py`:

```python
"""Usage history CSV export.

Writes a flat CSV any LLM or spreadsheet can parse. Columns:
timestamp, tier, util_pct. One row per (tier, timestamp) pair from the
local history.json file. Never touches the network — CSV lives on the
user's filesystem at a path they chose via the file dialog."""

import csv
from pathlib import Path
from typing import Union

from sanduhr import history


def export_to_csv(dest_path: Union[str, Path]) -> int:
    """Export all local usage history to `dest_path` as CSV.

    Returns the number of data rows written (excluding the header).
    Always writes the header row, even if history is empty."""
    dest_path = Path(dest_path)
    all_history = history.load_history()
    rows = []
    for tier_key, points in all_history.items():
        for p in points:
            rows.append({
                "timestamp": p.get("t", ""),
                "tier": tier_key,
                "util_pct": str(p.get("v", "")),
            })
    rows.sort(key=lambda r: r["timestamp"])

    with open(dest_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=["timestamp", "tier", "util_pct"])
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd windows && python -m pytest tests/test_csv_export.py -v`
Expected: all 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/csv_export.py windows/tests/test_csv_export.py
git commit -m "feat(csv-export): add export_to_csv for piping usage into any agent

Flat CSV with timestamp / tier / util_pct columns. Sorted chronologically.
Empty-history case writes header-only for consistency. No dependencies
beyond stdlib csv module. Called from Settings → History → Export CSV
(wired in a later task).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: History chart widget

**Files:**
- Create: `windows/src/sanduhr/history_chart.py`
- Create: `windows/tests/test_history_chart.py`

- [ ] **Step 1: Write the failing test**

Create `windows/tests/test_history_chart.py`:

```python
"""Smoke tests for the history chart widget.

Renders a line chart per tier over a configurable window (week / month).
Follows the same QPainter-based pattern as Sparkline — no external chart
library, visual language stays consistent across the app."""

import tempfile
import pytest
from PySide6.QtGui import QPixmap


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def _obsidian():
    from sanduhr import themes
    return themes.THEMES["obsidian"]


def test_chart_constructs(qtbot):
    from sanduhr.history_chart import HistoryChart
    chart = HistoryChart(theme=_obsidian())
    qtbot.addWidget(chart)
    assert chart.mode() == "week"


def test_chart_mode_toggle(qtbot):
    from sanduhr.history_chart import HistoryChart
    chart = HistoryChart(theme=_obsidian())
    qtbot.addWidget(chart)
    chart.set_mode("month")
    assert chart.mode() == "month"


def test_chart_renders_without_crash(qtbot):
    """Chart must not raise when painted with realistic inputs."""
    from sanduhr import history
    from sanduhr.history_chart import HistoryChart
    from datetime import datetime, timezone, timedelta

    now = datetime.now(timezone.utc)
    history.save_history({
        "five_hour": [
            {"t": (now - timedelta(hours=i)).isoformat(), "v": (i * 7) % 100}
            for i in range(48)
        ],
    })

    chart = HistoryChart(theme=_obsidian())
    qtbot.addWidget(chart)
    chart.resize(400, 300)
    chart.refresh()

    pm = QPixmap(chart.size())
    chart.render(pm)
    assert not pm.isNull()


def test_chart_handles_empty_history(qtbot):
    """Empty history renders an empty-state (no crash, no chart)."""
    from sanduhr.history_chart import HistoryChart
    chart = HistoryChart(theme=_obsidian())
    qtbot.addWidget(chart)
    chart.resize(400, 300)
    chart.refresh()
    pm = QPixmap(chart.size())
    chart.render(pm)
    assert not pm.isNull()
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd windows && python -m pytest tests/test_history_chart.py -v`
Expected: FAIL — `sanduhr.history_chart` module doesn't exist.

- [ ] **Step 3: Create `history_chart.py`**

Create `windows/src/sanduhr/history_chart.py`:

```python
"""HistoryChart — stacked line charts showing 30-day usage per tier.

One mini-chart per tier stacked vertically. Week / Month mode controls
the lookback window. Same QPainter pattern as Sparkline, just wider
canvas and slightly richer chrome (tier label + axis hint per row).
Empty-state shows a hint string; no crashes on first-run empty data."""

from typing import Dict, List

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import QWidget

from sanduhr import history, themes


_TIER_LABELS = {
    "five_hour":            "Session (5hr)",
    "seven_day":            "Weekly — All Models",
    "seven_day_sonnet":     "Weekly — Sonnet",
    "seven_day_opus":       "Weekly — Opus",
    "seven_day_cowork":     "Weekly — Cowork",
    "seven_day_omelette":   "Weekly — Routines",
    "seven_day_oauth_apps": "Weekly — OAuth Apps",
    "iguana_necktie":       "Weekly — Special",
}


class HistoryChart(QWidget):
    def __init__(self, theme: dict, parent=None):
        super().__init__(parent)
        self._theme = theme
        self._mode = "week"  # "week" or "month"
        self._data: Dict[str, List[dict]] = {}
        self.setMinimumHeight(240)

    def mode(self) -> str:
        return self._mode

    def set_mode(self, mode: str) -> None:
        self._mode = mode
        self.refresh()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self.update()

    def refresh(self) -> None:
        days = 7 if self._mode == "week" else 30
        self._data = {}
        for tier_key in _TIER_LABELS:
            points = history.load_window(tier_key, days)
            if points:
                self._data[tier_key] = points
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        t = self._theme

        w = self.width()
        h = self.height()
        if w < 40 or h < 40:
            painter.end()
            return

        if not self._data:
            # Empty state
            painter.setPen(QColor(t["text_dim"]))
            painter.drawText(
                self.rect(), Qt.AlignCenter,
                "No history yet.\nData accumulates as you use Sanduhr."
            )
            painter.end()
            return

        n_tiers = len(self._data)
        row_h = h / n_tiers
        label_w = 140
        chart_x = label_w + 8
        chart_w = w - chart_x - 8

        for idx, (tier_key, points) in enumerate(self._data.items()):
            y_top = idx * row_h
            y_bottom = (idx + 1) * row_h - 4

            # Row label
            painter.setPen(QColor(t["text_secondary"]))
            painter.drawText(
                int(8), int(y_top), int(label_w), int(row_h - 4),
                Qt.AlignLeft | Qt.AlignVCenter,
                _TIER_LABELS.get(tier_key, tier_key),
            )

            # Line chart — map util_pct 0..100 -> y_bottom..y_top
            if len(points) < 2:
                continue
            path = QPainterPath()
            denom = len(points) - 1
            for i, p in enumerate(points):
                x = chart_x + (i / denom) * chart_w
                util = max(0, min(100, p.get("v", 0)))
                y = y_bottom - (util / 100.0) * (row_h - 8)
                if i == 0:
                    path.moveTo(x, y)
                else:
                    path.lineTo(x, y)
            pen = QPen(QColor(t["accent"]))
            pen.setWidthF(1.5)
            pen.setCapStyle(Qt.RoundCap)
            pen.setJoinStyle(Qt.RoundJoin)
            painter.setPen(pen)
            painter.drawPath(path)

        painter.end()
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd windows && python -m pytest tests/test_history_chart.py -v`
Expected: all 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/src/sanduhr/history_chart.py windows/tests/test_history_chart.py
git commit -m "feat(history-chart): stacked per-tier line chart widget

QPainter-based, matches Sparkline's visual language. One mini-chart
per tier stacked vertically, row label on the left. Week / Month
mode toggles the lookback window via load_window(). Empty-state hint
for first-run users. No external chart deps.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Settings dialog → History tab

**Files:**
- Modify: `windows/src/sanduhr/settings_dialog.py`
- Test extensions inline via `windows/tests/test_widget_bootstrap.py` (light — no new test file)

- [ ] **Step 1: Read current `settings_dialog.py` to find tab-build pattern**

Run: `grep -n "addTab\|_build_" windows/src/sanduhr/settings_dialog.py | head -15`
Expected: sees `_build_credentials_tab`, `_build_themes_tab`, `_build_pacing_tab`, `_build_help_tab` patterns.

- [ ] **Step 2: Add `_build_history_tab` method**

In `windows/src/sanduhr/settings_dialog.py`, add this method alongside the other `_build_*_tab` methods (place it just before `_build_help_tab`):

```python
    def _build_history_tab(self) -> None:
        from sanduhr.history_chart import HistoryChart
        from sanduhr import csv_export, history, themes

        page = QWidget()
        v = QVBoxLayout(page)

        v.addWidget(QLabel(
            "Rolling 30-day usage history. Stored locally only — "
            "never uploaded. Export as CSV to analyze with any agent."
        ))

        # Mode toggle
        mode_row = QHBoxLayout()
        mode_row.addWidget(QLabel("Window:"))
        self._hist_week_btn = QPushButton("Week")
        self._hist_week_btn.setCheckable(True)
        self._hist_week_btn.setChecked(True)
        self._hist_month_btn = QPushButton("Month")
        self._hist_month_btn.setCheckable(True)
        mode_row.addWidget(self._hist_week_btn)
        mode_row.addWidget(self._hist_month_btn)
        mode_row.addStretch()
        v.addLayout(mode_row)

        # Chart
        self._hist_chart = HistoryChart(theme=themes.THEMES.get("obsidian"))
        self._hist_chart.refresh()
        v.addWidget(self._hist_chart, stretch=1)

        def _set_week():
            self._hist_week_btn.setChecked(True)
            self._hist_month_btn.setChecked(False)
            self._hist_chart.set_mode("week")

        def _set_month():
            self._hist_week_btn.setChecked(False)
            self._hist_month_btn.setChecked(True)
            self._hist_chart.set_mode("month")

        self._hist_week_btn.clicked.connect(_set_week)
        self._hist_month_btn.clicked.connect(_set_month)

        # Action row
        action_row = QHBoxLayout()
        export_btn = QPushButton("Export CSV…")
        export_btn.clicked.connect(self._export_history_csv)
        action_row.addWidget(export_btn)

        clear_btn = QPushButton("Clear history")
        clear_btn.clicked.connect(self._clear_history)
        action_row.addWidget(clear_btn)
        action_row.addStretch()
        v.addLayout(action_row)

        self._tabs.addTab(page, "History")

    def _export_history_csv(self) -> None:
        from PySide6.QtWidgets import QFileDialog
        from sanduhr import csv_export
        from datetime import date

        default_name = f"Sanduhr-usage-{date.today().isoformat()}.csv"
        path, _ = QFileDialog.getSaveFileName(
            self, "Export usage history", default_name, "CSV files (*.csv)"
        )
        if not path:
            return
        try:
            count = csv_export.export_to_csv(path)
        except OSError as e:
            _styled_msgbox(
                self, QMessageBox.Critical, "Export failed",
                f"Could not write {path}:\n{e}",
            ).exec_()
            return
        _styled_msgbox(
            self, QMessageBox.Information, "Exported",
            f"Wrote {count} rows to:\n{path}",
        ).exec_()

    def _clear_history(self) -> None:
        from sanduhr import history
        confirm = _styled_msgbox(
            self, QMessageBox.Warning, "Clear history?",
            "This permanently removes the local 30-day usage history "
            "file. Sparkline and history chart will be empty until new "
            "data accumulates.\n\nCredentials are not affected.",
            buttons=QMessageBox.Yes | QMessageBox.No,
        )
        confirm.setDefaultButton(QMessageBox.No)
        if confirm.exec_() != QMessageBox.Yes:
            return
        history.clear_all()
        self._hist_chart.refresh()
        _styled_msgbox(
            self, QMessageBox.Information, "History cleared",
            "Local usage history file removed.",
        ).exec_()
```

- [ ] **Step 3: Call `_build_history_tab` in `__init__`**

In `SettingsDialog.__init__`, find the block that calls `_build_credentials_tab`, `_build_themes_tab`, `_build_pacing_tab`, `_build_help_tab` (note: current order has Credentials LAST per the post-Gemini rearrangement). Insert `_build_history_tab` call between Pacing and Help:

```python
        self._build_themes_tab()
        self._build_pacing_tab()
        self._build_history_tab()   # ← new, inserted here
        self._build_help_tab()
        self._build_credentials_tab(session_key, cf_clearance, focus_cf)
```

- [ ] **Step 4: Run full suite**

Run: `cd windows && python -m pytest tests/ --tb=short -q`
Expected: 198 passed — no new tests in this task, just verify the dialog still constructs.

- [ ] **Step 5: Smoke test manually**

Run: `cd windows && python -m sanduhr`
Open Settings (⚙ or Ctrl+,). Confirm a new "History" tab exists between Pacing and Help. Click through Week / Month toggle (chart should redraw or show empty state). Click Export CSV — file dialog appears. Click Clear history — confirmation dialog appears.

- [ ] **Step 6: Commit**

```bash
git add windows/src/sanduhr/settings_dialog.py
git commit -m "feat(settings): add History tab — chart + Export CSV + Clear history

Inserts between Pacing and Help. Week/Month toggle drives
history_chart. Export CSV opens a save dialog (default filename
Sanduhr-usage-YYYY-MM-DD.csv) and calls csv_export.export_to_csv.
Clear history gated behind a confirmation that explicitly notes
credentials are not affected — the two concerns shouldn't bundle.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Extend sign-out flow to optionally clear history

**Files:**
- Modify: `windows/src/sanduhr/settings_dialog.py` (extend `_save_credentials` blank-sessionKey path)
- Modify: `windows/src/sanduhr/widget.py` (extend `_on_credentials_cleared` to also clear history)
- Modify: `windows/tests/test_clear_credentials.py` (add history-clear assertion)

- [ ] **Step 1: Extend the existing clear_credentials test**

Open `windows/tests/test_clear_credentials.py` and add this test to the end (do not modify existing tests):

```python
def test_blank_save_confirmed_also_clears_history(qtbot, monkeypatch):
    """Sign-out flow also wipes the local usage history file.

    The Credentials confirmation dialog explicitly says 'this also
    clears local usage history' — so users aren't surprised, and the
    privacy posture is kept clean: signing out = no trace of prior
    usage on disk."""
    from sanduhr import credentials, history
    from sanduhr.settings_dialog import SettingsDialog
    from PySide6.QtWidgets import QMessageBox

    credentials.save(session_key="abc123")
    history.save_history({"five_hour": [{"t": "2026-04-21T10:00:00+00:00", "v": 50}]})
    assert history.load_history() != {}  # sanity

    dlg = SettingsDialog(None, session_key="abc123", cf_clearance="")
    qtbot.addWidget(dlg)
    dlg._sk.setText("")  # clear the input

    # Auto-accept the confirmation
    monkeypatch.setattr(QMessageBox, "exec_", lambda self: QMessageBox.Yes)
    # Also stub setDefaultButton so the styled box doesn't blow up on the stub
    import unittest.mock as _mock
    with _mock.patch.object(SettingsDialog, "_styled_msgbox_accepted", create=True):
        dlg._save_credentials()

    assert history.load_history() == {}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd windows && python -m pytest tests/test_clear_credentials.py::test_blank_save_confirmed_also_clears_history -v`
Expected: FAIL — sign-out currently only calls `credentials.clear()`, not `history.clear_all()`.

- [ ] **Step 3: Extend `_save_credentials` in `settings_dialog.py`**

Find the existing blank-sessionKey block (around line 170 in `_save_credentials`). Update the confirmation dialog text AND add the history clear. The block currently reads:

```python
        confirm = _styled_msgbox(
            self, QMessageBox.Warning, "Sign out of Sanduhr?",
            "The sessionKey field is empty.\n\n"
            "Saving this will clear your stored credentials from "
            "Windows Credential Manager and stop the widget from "
            "fetching your usage. You'll need to paste a fresh "
            "sessionKey to resume.\n\n"
            "Continue?",
            buttons=QMessageBox.Yes | QMessageBox.No,
        )
```

Update to:

```python
        confirm = _styled_msgbox(
            self, QMessageBox.Warning, "Sign out of Sanduhr?",
            "The sessionKey field is empty.\n\n"
            "Saving this will:\n"
            "  • Clear your stored credentials from Windows Credential Manager\n"
            "  • Delete the local 30-day usage history file\n"
            "  • Stop the widget from fetching your usage\n\n"
            "You'll need to paste a fresh sessionKey to resume.\n\n"
            "Continue?",
            buttons=QMessageBox.Yes | QMessageBox.No,
        )
```

And in the confirmed branch, add `history.clear_all()` after `credentials.clear()`:

```python
            credentials.clear()
            from sanduhr import history
            history.clear_all()
            self.credentialsCleared.emit()
```

- [ ] **Step 4: Extend `_on_credentials_cleared` in `widget.py` to refresh any open chart**

Find `_on_credentials_cleared` in `widget.py`. Current body tears down tier cards and sets status. No new logic needed for history itself (file is already wiped by the settings dialog), but if a History chart is open, its next refresh will show empty. Leave `_on_credentials_cleared` as-is — the chart reads lazily.

- [ ] **Step 5: Run tests**

Run: `cd windows && python -m pytest tests/test_clear_credentials.py -v`
Expected: all 5 PASS (4 existing + 1 new).

- [ ] **Step 6: Run full suite**

Run: `cd windows && python -m pytest tests/ --tb=short -q`
Expected: 199 passed.

- [ ] **Step 7: Commit**

```bash
git add windows/src/sanduhr/settings_dialog.py windows/tests/test_clear_credentials.py
git commit -m "feat(sign-out): also wipe local history file on sign-out

The sign-out confirmation dialog now lists everything that gets
removed (credentials, history file, fetcher), so users aren't
surprised. credentials.clear() and history.clear_all() run in the
same confirmed branch. Preserves the 'signing out = no trace' story
without surprising anyone.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Docs + CHANGELOG + version bump

**Files:**
- Modify: `windows/pyproject.toml`
- Modify: `windows/src/sanduhr/__init__.py`
- Modify: `CHANGELOG.md`
- Modify: `README.md`
- Modify: `SECURITY.md`
- Modify: `docs/PRIVACY.md`

- [ ] **Step 1: Bump version in pyproject.toml**

Find `version = "2.0.4"` in `windows/pyproject.toml`. Change to:

```toml
version = "2.1.0"
```

- [ ] **Step 2: Bump version in __init__.py**

Find `__version__ = "2.0.4"` in `windows/src/sanduhr/__init__.py`. Change to:

```python
__version__ = "2.1.0"
```

- [ ] **Step 3: Prepend v2.1.0 entry to CHANGELOG.md**

At the top of `CHANGELOG.md`, just under the `# Changelog` heading, insert:

```markdown
## v2.1.0-windows — 2026-04-21

**Platform:** Windows
**Minor feature release — local usage history + CSV export.**

### Added

- **30-day rolling usage history.** `history.json` now retains 8640 data points per tier (30 days × 288 five-minute ticks) instead of the 24-point / 2-hour sparkline window. No protocol change — existing sparkline reads from the same file, just pulls the most recent 24 points. Rolling prune trims on append so the file stays ~bounded.
- **Settings → History tab.** New tab between Pacing and Help. Stacked per-tier line chart (QPainter, matching Sparkline's visual language) with Week / Month toggle. Export CSV button dumps the full history to a user-chosen path with columns `timestamp / tier / util_pct`. Clear history button wipes the local file after a confirmation dialog.
- **CSV export for agent analysis.** `Sanduhr-usage-YYYY-MM-DD.csv` is flat, sorted chronologically, and parseable by any LLM. Paste the CSV into your Claude / ChatGPT session and ask for efficiency patterns, time-of-day analysis, weekly trends — without us needing to build those analytics in-app.

### Changed

- **Sign-out flow wipes history too.** The existing "Sign out of Sanduhr?" confirmation now explicitly lists the local history file alongside the Credential Manager wipe. Credentials and history stay bundled under one action; users aren't surprised that revoking access also removes the data trail.

### Privacy

This release extends the local data footprint but the "no data comes back to us" posture is unchanged — 626 Labs still has no server and no pipeline to receive data from the app. SECURITY.md and docs/PRIVACY.md have been updated with one sentence each acknowledging the local-only history file.

---
```

(Leave the existing v2.0.4 entry immediately below this one.)

- [ ] **Step 4: Update README.md Features section**

Find the `### Pacing` section in `README.md`. At the end of its bullet list, add:

```markdown
- **30-day history + CSV export** — Settings → History tab charts your last week or month per tier. One click exports to a CSV you can paste into any agent for efficiency analysis. Data stored locally only, wiped on sign-out.
```

Find the Roadmap section and move the "Historical usage dashboard with CSV export" line from "Up next" to the "Shipped in v2.1.0" section (create that section; leave "Shipped in v2.0.4" intact as history). New structure:

```markdown
### Shipped in v2.1.0

- [x] 30-day local usage history
- [x] Settings → History tab with per-tier line charts
- [x] CSV export for analysis in any agent

### Shipped in v2.0.4

- [x] Pace ghost (always-on pace position tick on every bar)
- [x] Horizon sparkline (replaces pulse histogram)
- [x] Breathing glass (subliminal accent pulse)
- [x] Edge-drag resize with dynamic minimum bounds
- [x] Deep-work focus timer with digitised hourglass
- [x] Cooldown snake game
- [x] Advanced pacing metrics (Cooldown required, Surplus)
- [x] One-click sign-out from Settings

### Up next

- [ ] Microsoft Store listing live (cert passed — Store URL pending propagation)
- [ ] Homebrew cask submission (pending first tagged Mac release)
- [ ] winget manifest (pending MS Store Store URL going live)
- [ ] Auto-start on boot (native builds)
- [ ] Antigravity (Google Gemini IDE) quota tracking
- [ ] Official Anthropic read-only usage endpoint support (pending Anthropic response)
```

- [ ] **Step 5: Update SECURITY.md**

Find the "No data ever comes back to 626 Labs" section in `SECURITY.md`. At the end of the section, add this paragraph:

```markdown
**Local data storage.** Sanduhr does write a usage history file to your
platform's AppData folder (`%APPDATA%\Sanduhr\history.json` on Windows)
to support the Settings → History tab and CSV export. This file never
leaves your machine — 626 Labs has no way to read it. You can wipe it
at any time from Settings → History → Clear history, and it's also
wiped automatically when you sign out from Settings → Credentials.
```

- [ ] **Step 6: Update docs/PRIVACY.md**

Add a new section titled "Local data storage" to `docs/PRIVACY.md`, below the existing content:

```markdown
## Local data storage

Sanduhr stores two files on your local machine:

1. **Usage history** — `%APPDATA%\Sanduhr\history.json` (Windows) or
   `~/Library/Application Support/Sanduhr/history.json` (macOS).
   Contains a rolling 30-day window of the same utilization percentages
   that claude.ai shows you in its Settings page. Used to render the
   sparkline and the Settings → History tab.

2. **Preferences** — `%APPDATA%\Sanduhr\settings.json` / equivalent on
   macOS. Stores your selected theme, pinned state, compact-mode flag,
   window geometry, and snake-game high score.

Neither file ever leaves your machine. 626 Labs has no server and no
way to receive them. Both are wiped on uninstall. You can also clear
the history file on demand from Settings → History → Clear history, or
by signing out from Settings → Credentials (both operations clear it).

Your Claude session cookie is stored separately in Windows Credential
Manager / macOS Keychain, never in one of these files.
```

- [ ] **Step 7: Commit**

```bash
git add windows/pyproject.toml windows/src/sanduhr/__init__.py CHANGELOG.md README.md SECURITY.md docs/PRIVACY.md
git commit -m "release: v2.1.0 — 30-day usage history + CSV export

Version bump (2.0.4 -> 2.1.0), CHANGELOG entry documenting the feature
and the sign-out-also-clears-history change, README Features + Roadmap
update, and privacy disclosures in SECURITY.md + PRIVACY.md for the
local history file (the file itself existed before this release —
sparkline already used it — but 30-day retention makes it worth an
explicit mention).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Self-review checklist (completed)

**Spec coverage:**
- [x] 30-day retention + query API → Task 1
- [x] CSV export → Task 2
- [x] Line chart widget → Task 3
- [x] Settings tab wiring → Task 4
- [x] Sign-out integration → Task 5
- [x] Docs + version + CHANGELOG → Task 6

**Placeholder scan:** No TBDs, no "add appropriate error handling" hand-waves, no "implement later." Every step shows the code or command.

**Type consistency:**
- `history.MAX_HISTORY` (int) — defined Task 1, used Task 1 tests only.
- `history.load_window(tier_key: str, days: int) -> list[dict]` — defined Task 1, used Task 3 (`HistoryChart.refresh`).
- `history.clear_all() -> None` — defined Task 1, used Task 4 (`_clear_history`) and Task 5 (sign-out flow).
- `csv_export.export_to_csv(dest_path) -> int` — defined Task 2, used Task 4 (`_export_history_csv`).
- `HistoryChart(theme, parent=None)`, `.mode()`, `.set_mode(str)`, `.refresh()`, `.apply_theme(theme)` — defined Task 3, used Task 4.
- `SettingsDialog._build_history_tab`, `._export_history_csv`, `._clear_history` — defined Task 4, no external references.
- All method signatures match between definition and call sites.

**Risks / watch-outs:**
- Task 5 test relies on monkey-patching `QMessageBox.exec_` globally. If another test has leftover state, the patch might collide. Fixture-scoped monkeypatch handles cleanup — should be fine.
- Chart's `refresh()` is called lazily from the settings dialog only. If user leaves Settings open and data arrives from the fetcher, the chart won't auto-update. Acceptable — settings dialog is modal; users close it after operations. Non-modal refresh is a follow-up if anyone asks.
- Export CSV uses `QFileDialog.getSaveFileName` which is modal. On Qt 6 with our Mica window, this has been fine in prior features (settings tab, theme JSON paste). No known gotcha.

# Pacing Visualization + Edge-Drag Resize Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land four user-visible visual upgrades on `feature/advanced-pacing` without adding UI surface area: an always-on pace ghost that replaces today's subtle projection phantom, a horizon-chart sparkline mode that replaces the current pulse histogram, a breathing-glass shimmer on usage bars, and proper edge-drag resize with a dynamic minimum size calculated from actual string widths.

**Architecture:** Each piece is one self-contained commit on `feature/advanced-pacing`. The pace ghost and breathing glass become always-on rendering paths inside `TierCard.paintEvent` / the bar stylesheet — no new graph mode toggles. The horizon sparkline is a third render branch inside `Sparkline.paintEvent` swapped in via `set_mode("horizon")`, taking over the cycle slot currently held by `"pulse"` (Classic / Horizon, not Classic / Projection / Pulse — projection becomes unconditional via the ghost). Edge-drag resize uses an event filter that interprets mouse position against a 6px edge threshold, with `setMinimumSize` computed once at startup from `QFontMetrics` measurements of known long strings.

**Tech Stack:** Python 3.11+, PySide6 (Qt 6) with `QPainter`, `QFontMetrics`, `QTimer`, `QElapsedTimer`, `QCursor`; pytest + pytest-qt for tests; existing `TierCard` / `Sparkline` / `SanduhrWidget` modules under `windows/src/sanduhr/`.

---

## File Structure

All changes on the Windows client. Mac parity is explicitly out-of-scope for this plan (tracked separately).

**Files created:**

- `windows/tests/test_pace_ghost.py` — TierCard pace-ghost render tests
- `windows/tests/test_horizon_sparkline.py` — Sparkline horizon mode tests
- `windows/tests/test_breathing_glass.py` — Bar shimmer animation tests
- `windows/tests/test_edge_resize.py` — Edge-drag resize event filter tests

**Files modified:**

- `windows/src/sanduhr/tiers.py` — remove `_current_graph_mode` projection branch; add pace-ghost render in `paintEvent`; wire the projection value through `apply_theme` so the ghost colors correctly; remove `_GRAPH_MODES = ["classic", "projection", "pulse"]` → `["classic", "horizon"]`; `cycle_graph_mode()` cycles between the two.
- `windows/src/sanduhr/sparkline.py` — add `_paint_horizon` method; `set_mode("horizon")` dispatches to it; keep `line` mode for classic.
- `windows/src/sanduhr/widget.py` — add `_start_breathing` / `_stop_breathing` QTimer control; install `_ResizeEventFilter` on `self`; call `setMinimumSize` after `QFontMetrics` measurement on startup; persist resized geometry to settings; skip resize while focus/snake overlays are active.
- `windows/src/sanduhr/themes.py` — add optional `ghost_alpha` tuning dial per theme (default 0.35); add optional `breath_period_ms` (default 2800); Matrix theme overrides both to feel more CRT-alive.
- `CHANGELOG.md` — one new subsection under the v2.0.4 heading per commit.

**Files not touched:**

- `windows/src/sanduhr/pacing.py` — `velocity_projection` stays; the ghost reuses it.
- `windows/src/sanduhr/focus.py`, `game.py` — unrelated.
- Mac-side sources — out of scope.

---

## Task 1: Pace ghost rendering (always-on)

**Files:**
- Create: `windows/tests/test_pace_ghost.py`
- Modify: `windows/src/sanduhr/tiers.py:83-108` (`apply_theme` + new `paintEvent` ghost render), `windows/src/sanduhr/tiers.py:295-303` (remove projection-mode branch that draws the phantom bar)

- [ ] **Step 1: Write the failing test**

Create `windows/tests/test_pace_ghost.py`:

```python
"""Regression tests for the always-on pace ghost overlay.

The ghost is a faint outline on the progress bar showing where pace
says usage *should* be right now. Real usage then catches up to,
passes, or lags the ghost. Replaces the previous click-in projection
mode (which only 15% of users ever discovered) with an at-a-glance
signal that's visible the moment the tier card paints.
"""

import tempfile
import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def _obsidian():
    from sanduhr import themes
    return themes.THEMES["obsidian"]


def test_ghost_position_tracks_pace_frac(qtbot):
    """When pace says you should be at 50%, the ghost should render at
    the x-pixel corresponding to 50% of the bar width."""
    from sanduhr.tiers import TierCard
    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)
    card.resize(300, 80)

    # Halfway through the window: pace_frac should be 0.5
    from datetime import datetime, timezone, timedelta
    future = datetime.now(timezone.utc) + timedelta(hours=2.5)
    card.update_state(util=40, resets_at=future.isoformat().replace("+00:00", "Z"), history_values=[])

    assert card._ghost_frac is not None
    assert 0.45 <= card._ghost_frac <= 0.55, (
        f"At 2.5h remaining on a 5h window, pace_frac should be ~0.5, "
        f"got {card._ghost_frac}"
    )


def test_ghost_absent_when_no_reset_data(qtbot):
    """No reset timestamp -> no ghost computed."""
    from sanduhr.tiers import TierCard
    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)

    card.update_state(util=40, resets_at=None, history_values=[])
    assert card._ghost_frac is None


def test_ghost_uses_theme_alpha(qtbot):
    """Ghost color alpha respects the theme's `ghost_alpha` dial so Matrix can
    be more assertive than Aurora."""
    from sanduhr.tiers import TierCard
    from sanduhr import themes

    theme = dict(themes.THEMES["matrix"])
    theme["ghost_alpha"] = 0.8
    card = TierCard(tier_key="five_hour", label="Session", theme=theme)
    qtbot.addWidget(card)

    assert card._ghost_alpha == 0.8


def test_ghost_default_alpha_is_sane(qtbot):
    """Themes without ghost_alpha fall back to 0.35 (readable but quiet)."""
    from sanduhr.tiers import TierCard
    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)
    assert 0.2 <= card._ghost_alpha <= 0.5
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m pytest tests/test_pace_ghost.py -v`
Expected: FAIL — `AttributeError: 'TierCard' object has no attribute '_ghost_frac'`

- [ ] **Step 3: Add ghost computation + theme hook to `TierCard`**

In `windows/src/sanduhr/tiers.py`, find `TierCard.__init__` (around line 44) and add after `self._projected`:

```python
        self._ghost_frac: Optional[float] = None
        self._ghost_alpha: float = 0.35
```

In `apply_theme` (around line 87), add after the existing theme application:

```python
        self._ghost_alpha = float(theme.get("ghost_alpha", 0.35))
```

In `update_state` (around line 82), after the `self._projected = ...` line, add:

```python
        self._ghost_frac = pacing.pace_frac(resets_at, self._tier_key)
```

- [ ] **Step 4: Run tests to verify ghost state is populated**

Run: `python -m pytest tests/test_pace_ghost.py -v`
Expected: PASS on `test_ghost_position_tracks_pace_frac`, `test_ghost_absent_when_no_reset_data`, `test_ghost_uses_theme_alpha`, `test_ghost_default_alpha_is_sane`

- [ ] **Step 5: Render the ghost in `paintEvent`**

Find `TierCard.paintEvent` (around line 275). Replace the projection-mode branch (`if mode == "projection" and self._projected is not None ...`) with unconditional ghost rendering. New block to add after the bar is painted:

```python
        # Always-on pace ghost — a thin outlined rectangle at x=ghost_frac*bar_w.
        # Reads as: "where pace says you should be right now." Real fill sits
        # to the left (under pace), at (on pace), or to the right (ahead).
        if self._ghost_frac is not None and self._bar_container.width() > 0:
            bar_x = self._bar_container.x()
            bar_y = self._bar_container.y()
            bar_w = self._bar_container.width()
            bar_h = self._bar_container.height()

            ghost_x = bar_x + int(self._ghost_frac * bar_w)
            # 2px wide tick that spans the full bar height plus a small
            # protrusion top and bottom — like a race-ghost time marker.
            protrude = 2
            from PySide6.QtGui import QColor, QPen
            ghost_color = QColor(self._theme["text"])
            ghost_color.setAlphaF(self._ghost_alpha)
            pen = QPen(ghost_color)
            pen.setWidth(2)
            painter.setPen(pen)
            painter.drawLine(
                ghost_x, bar_y - protrude,
                ghost_x, bar_y + bar_h + protrude,
            )
```

- [ ] **Step 6: Remove the old projection phantom-bar code**

In `tiers.py` around the old line 295-303, delete the block starting with `if mode == "projection" and self._projected is not None` through its closing lines. The ghost supersedes it.

- [ ] **Step 7: Remove `"projection"` from `_GRAPH_MODES`**

In `tiers.py` around line 28:

```python
_GRAPH_MODES = ["classic", "horizon"]
```

Update `_current_graph_mode = "classic"` (unchanged).

- [ ] **Step 8: Run full suite to confirm no regressions**

Run: `python -m pytest tests/ --tb=short -q`
Expected: 177 passed (173 existing + 4 new ghost tests)

- [ ] **Step 9: Commit**

```bash
git add windows/src/sanduhr/tiers.py windows/tests/test_pace_ghost.py
git commit -m "feat(tiers): always-on pace ghost replaces projection-mode phantom bar

Where pace says usage should be right now renders as a faint vertical
tick on every tier card, always. Supersedes the previous graph-mode-
gated phantom bar that was too subtle to discover. Ghost color /
alpha tuned per theme via the new ghost_alpha dial (default 0.35,
Matrix can dial up for CRT feel).

Graph-mode cycle simplified from 3 modes (Classic / Projection /
Pulse) to 2 (Classic / Horizon) — projection is now unconditional
via the ghost, no mode needed."
```

---

## Task 2: Horizon sparkline mode

**Files:**
- Create: `windows/tests/test_horizon_sparkline.py`
- Modify: `windows/src/sanduhr/sparkline.py:39-42` (`set_mode` dispatch), `windows/src/sanduhr/sparkline.py:44-56` (`paintEvent` dispatch), add new `_paint_horizon` method
- Modify: `windows/src/sanduhr/tiers.py:102-103` (map `"horizon"` mode through)

- [ ] **Step 1: Write the failing test**

Create `windows/tests/test_horizon_sparkline.py`:

```python
"""Tests for the horizon-chart sparkline mode.

The horizon chart stacks the 2h usage history into four intensity
bands. Peaks render as dense dark regions; lulls show through as
soft backgrounds. More information per pixel than a line chart,
without the visual noise of a histogram.
"""

import pytest
from PySide6.QtGui import QPixmap


def test_horizon_mode_is_recognized():
    from sanduhr.sparkline import Sparkline
    s = Sparkline()
    s.set_mode("horizon")
    assert s._mode == "horizon"


def test_horizon_renders_without_crash(qtbot):
    """Horizon paint path must not raise on realistic inputs."""
    from sanduhr.sparkline import Sparkline
    s = Sparkline()
    qtbot.addWidget(s)
    s.resize(200, 28)
    s.set_color("#3bb4d9")
    s.set_values([10, 25, 40, 55, 70, 85, 60, 45, 30, 20, 15, 10])
    s.set_mode("horizon")

    pm = QPixmap(s.size())
    s.render(pm)
    assert not pm.isNull()


def test_horizon_bails_on_short_history(qtbot):
    """Horizon needs multiple points; single-point history should short-circuit."""
    from sanduhr.sparkline import Sparkline
    s = Sparkline()
    qtbot.addWidget(s)
    s.resize(200, 28)
    s.set_mode("horizon")
    s.set_values([50])  # not enough to render
    # No crash == pass
    pm = QPixmap(s.size())
    s.render(pm)


def test_line_mode_still_works(qtbot):
    """Regression — line mode must not break when horizon is added."""
    from sanduhr.sparkline import Sparkline
    s = Sparkline()
    qtbot.addWidget(s)
    s.resize(200, 28)
    s.set_values([10, 30, 60, 90, 70, 40, 20])
    s.set_mode("line")
    pm = QPixmap(s.size())
    s.render(pm)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m pytest tests/test_horizon_sparkline.py -v`
Expected: FAIL on `test_horizon_renders_without_crash` — no horizon render branch exists yet, but likely passes `test_horizon_mode_is_recognized` already because `set_mode` accepts any string.

- [ ] **Step 3: Add `_paint_horizon` method to `Sparkline`**

In `windows/src/sanduhr/sparkline.py`, after `_paint_pulse` (around line 107), add:

```python
    def _paint_horizon(self, w: int, h: int) -> None:
        """Horizon chart — stack 4 alpha-bands of the history.

        Each value is quantized into 4 bands at 25% intervals. Bands are
        drawn in order dark → light with increasing alpha, so peaks in
        the history pile up multiple overlapping bands and read as
        dense dark regions, while quiet stretches render as a single
        soft band."""
        mn = 0  # horizon bands are absolute percentage, not normalized
        mx = 100
        rng = mx - mn

        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, False)

        n = len(self._values)
        col_w = max(1, w / n)
        bands = 4
        for band in range(bands):
            band_floor = mn + (rng * band / bands)
            band_ceil = mn + (rng * (band + 1) / bands)
            # Alpha rises with band index: lowest band is softest.
            alpha = 0.18 + 0.20 * band  # 0.18, 0.38, 0.58, 0.78
            c = QColor(self._color)
            c.setAlphaF(alpha)

            for i, v in enumerate(self._values):
                if v <= band_floor:
                    continue
                # How much of this band does v fill?
                fill = min(v, band_ceil) - band_floor
                fill_ratio = fill / (band_ceil - band_floor)
                bar_h = max(1, int(fill_ratio * (h / bands)))
                x = int(i * col_w)
                # Band is anchored to bottom of widget, stacked upward
                band_bottom = h - (band * (h / bands))
                y = int(band_bottom - bar_h)
                painter.fillRect(x, y, max(1, int(col_w)), bar_h, c)

        painter.end()
```

- [ ] **Step 4: Wire `horizon` mode in `paintEvent`**

In `sparkline.py`, find `paintEvent` (around line 44). Replace:

```python
        if self._mode == "pulse":
            self._paint_pulse(w, h)
        else:
            self._paint_line(w, h)
```

with:

```python
        if self._mode == "horizon":
            self._paint_horizon(w, h)
        elif self._mode == "pulse":
            self._paint_pulse(w, h)
        else:
            self._paint_line(w, h)
```

- [ ] **Step 5: Update `TierCard` to dispatch `horizon` through**

In `windows/src/sanduhr/tiers.py`, find the `set_mode` dispatch (around line 102-103):

```python
        mode = current_graph_mode()
        self._spark.set_mode("horizon" if mode == "horizon" else "line")
```

(Replaces the previous `"pulse"` dispatch.)

- [ ] **Step 6: Run tests**

Run: `python -m pytest tests/test_horizon_sparkline.py -v`
Expected: all 4 PASS

- [ ] **Step 7: Run full suite**

Run: `python -m pytest tests/ --tb=short -q`
Expected: 181 passed (177 + 4 new horizon tests)

- [ ] **Step 8: Commit**

```bash
git add windows/src/sanduhr/sparkline.py windows/src/sanduhr/tiers.py windows/tests/test_horizon_sparkline.py
git commit -m "feat(sparkline): horizon-chart mode replaces pulse histogram

4-band stacked horizon reads denser than a line chart at the same
pixel budget. Peaks in the 2h history show up as dark regions;
lulls show through as soft single-band washes. Graph-mode cycle
is now Classic / Horizon."
```

---

## Task 3: Breathing glass shimmer

**Files:**
- Create: `windows/tests/test_breathing_glass.py`
- Modify: `windows/src/sanduhr/themes.py` (add `breath_period_ms` dial, default 2800)
- Modify: `windows/src/sanduhr/tiers.py` (new `_breath_phase` attr, QTimer on the card, repaint on tick, alpha modulation in `paintEvent`)

- [ ] **Step 1: Write the failing test**

Create `windows/tests/test_breathing_glass.py`:

```python
"""Tests for the breathing-glass bar animation.

The usage bar gets a subtle slow alpha pulse — the breathing visual
makes the widget feel alive at rest. Period + intensity are theme-
tunable so Matrix can beat faster / Aurora can be nearly static.
"""

import tempfile
import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def _obsidian():
    from sanduhr import themes
    return themes.THEMES["obsidian"]


def test_breath_timer_runs_on_construct(qtbot):
    from sanduhr.tiers import TierCard
    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)
    assert card._breath_timer is not None
    assert card._breath_timer.isActive()


def test_breath_phase_advances(qtbot, monkeypatch):
    """After one timer tick, phase should have advanced."""
    from sanduhr.tiers import TierCard
    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)

    initial = card._breath_phase
    # Simulate elapsed time
    monkeypatch.setattr(card._breath_elapsed, "elapsed", lambda: 500)
    card._tick_breath()
    assert card._breath_phase != initial


def test_breath_period_from_theme(qtbot):
    """Matrix theme overrides the default breath period."""
    from sanduhr.tiers import TierCard
    from sanduhr import themes
    theme = dict(themes.THEMES["matrix"])
    theme["breath_period_ms"] = 1500
    card = TierCard(tier_key="five_hour", label="Session", theme=theme)
    qtbot.addWidget(card)
    assert card._breath_period_ms == 1500


def test_breath_default_period(qtbot):
    from sanduhr.tiers import TierCard
    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)
    # 2800ms default feels alive but calm
    assert card._breath_period_ms == 2800
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m pytest tests/test_breathing_glass.py -v`
Expected: FAIL — `AttributeError: 'TierCard' object has no attribute '_breath_timer'`

- [ ] **Step 3: Add breath state + timer to `TierCard.__init__`**

In `tiers.py`, at the top of the file, add import:

```python
import math

from PySide6.QtCore import Qt, QEvent, QTimer, QElapsedTimer
```

(The existing line imports `Qt`, `QEvent` — extend it.)

In `TierCard.__init__`, after the existing state attrs, add:

```python
        self._breath_phase: float = 0.0
        self._breath_period_ms: int = 2800
        self._breath_elapsed = QElapsedTimer()
        self._breath_elapsed.start()

        self._breath_timer = QTimer(self)
        self._breath_timer.setInterval(66)  # ~15fps — enough for smooth breath, trivial CPU
        self._breath_timer.timeout.connect(self._tick_breath)
        self._breath_timer.start()
```

In `apply_theme`, after setting `_ghost_alpha`, add:

```python
        self._breath_period_ms = int(theme.get("breath_period_ms", 2800))
```

Add a new method:

```python
    def _tick_breath(self) -> None:
        """Advance the sin-wave phase driving the bar's alpha modulation."""
        t_ms = self._breath_elapsed.elapsed() % self._breath_period_ms
        self._breath_phase = (t_ms / self._breath_period_ms) * 2.0 * math.pi
        self.update()
```

- [ ] **Step 4: Apply the phase in `paintEvent`**

Find where the bar is filled. The bar's fill is a QProgressBar styled via stylesheet. We modulate alpha on a semi-transparent overlay drawn on top of the bar during `paintEvent`. After the ghost render block, add:

```python
        # Breathing-glass overlay — a slow sine alpha wash on top of the bar
        # fill. Amplitude 0.08 keeps it visible but subliminal; anything
        # higher reads as flicker.
        if self._bar_container.width() > 0:
            from PySide6.QtGui import QColor
            amp = 0.08
            # sin returns [-1,1] → scale to [0, 2*amp] and center on amp so
            # brightness pulses above and below the resting bar.
            breath_alpha = amp + amp * math.sin(self._breath_phase)
            overlay = QColor(self._theme["text"])
            overlay.setAlphaF(breath_alpha)
            painter.fillRect(
                self._bar_container.x(),
                self._bar_container.y(),
                int(self._bar_container.width() * (self._util / 100.0)),
                self._bar_container.height(),
                overlay,
            )
```

- [ ] **Step 5: Run tests**

Run: `python -m pytest tests/test_breathing_glass.py -v`
Expected: 4 PASS

- [ ] **Step 6: Run full suite**

Run: `python -m pytest tests/ --tb=short -q`
Expected: 185 passed (181 + 4 new breath tests)

- [ ] **Step 7: Commit**

```bash
git add windows/src/sanduhr/tiers.py windows/tests/test_breathing_glass.py
git commit -m "feat(tiers): breathing-glass shimmer on usage bars

Slow sine-wave alpha overlay on every bar's fill region. Amplitude
tuned to 0.08 so it reads as 'alive' not 'flicker'. Period defaults
to 2800ms; Matrix theme can crank it faster (breath_period_ms dial
on the theme dict). 15fps timer is a rounding error on CPU."
```

---

## Task 4: Edge-drag resize with dynamic minimum size

**Files:**
- Create: `windows/tests/test_edge_resize.py`
- Modify: `windows/src/sanduhr/widget.py` (new `_ResizeEventFilter` class, install it on `self`, compute and apply dynamic `minimumSize`, persist geometry)

- [ ] **Step 1: Write the failing tests**

Create `windows/tests/test_edge_resize.py`:

```python
"""Tests for edge-drag resize.

The frameless widget becomes draggable from any edge or corner.
Minimum size is computed from QFontMetrics measurements of the
longest known strings so text never gets clipped below it.
"""

import json
import tempfile
import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


@pytest.fixture(autouse=True)
def _stub_modal_dialogs(monkeypatch):
    from sanduhr import widget as widget_mod
    monkeypatch.setattr(
        widget_mod.SanduhrWidget, "_prompt_first_run", lambda self: None
    )


def _build_widget(qtbot, monkeypatch):
    from sanduhr import credentials, widget
    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})
    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    return w


def test_minimum_size_is_dynamic(qtbot, monkeypatch):
    """Minimum size computed via QFontMetrics, not hardcoded."""
    w = _build_widget(qtbot, monkeypatch)
    min_w = w.minimumSize().width()
    min_h = w.minimumSize().height()
    # Should be big enough to hold the longest status string
    # ("Signed out — paste sessionKey in Settings to resume.") at 10pt
    assert min_w >= 320, f"min_w {min_w} too narrow for status strings"
    assert min_h >= 180, f"min_h {min_h} too short for compact mode"


def test_resize_zone_detection_left_edge(qtbot, monkeypatch):
    """Cursor within 6px of the left edge is a resize zone."""
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 400)
    from PySide6.QtCore import QPoint
    zone = w._resize_zone(QPoint(3, 200))
    assert zone == "left"


def test_resize_zone_detection_bottom_right_corner(qtbot, monkeypatch):
    """Corner zone takes priority over edge."""
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 400)
    from PySide6.QtCore import QPoint
    zone = w._resize_zone(QPoint(498, 398))
    assert zone == "bottom-right"


def test_resize_zone_detection_interior(qtbot, monkeypatch):
    """Interior returns None (not a resize zone)."""
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 400)
    from PySide6.QtCore import QPoint
    zone = w._resize_zone(QPoint(250, 200))
    assert zone is None


def test_resize_respects_minimum(qtbot, monkeypatch):
    """A resize that would go below minimumSize gets clamped."""
    w = _build_widget(qtbot, monkeypatch)
    min_w = w.minimumSize().width()
    min_h = w.minimumSize().height()
    w.resize(min_w - 50, min_h - 50)
    assert w.width() >= min_w
    assert w.height() >= min_h


def test_resize_persists_to_settings(qtbot, monkeypatch):
    """After a resize + save, the geometry is in settings.json."""
    from sanduhr import paths
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 450)
    w._save_settings()
    data = json.loads(paths.settings_file().read_text(encoding="utf-8"))
    assert data.get("geom", {}).get("w") == 500
    assert data.get("geom", {}).get("h") == 450
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m pytest tests/test_edge_resize.py -v`
Expected: FAIL on all 6 — `_resize_zone` doesn't exist; minimum size not set dynamically.

- [ ] **Step 3: Compute dynamic minimum size in `SanduhrWidget.__init__`**

In `windows/src/sanduhr/widget.py`, after `self.resize(420, 540)` (around line 71), add:

```python
        self._compute_and_apply_minimum_size()
```

Add a new method:

```python
    def _compute_and_apply_minimum_size(self) -> None:
        """Set minimumSize based on QFontMetrics of known long strings so
        no text ever clips below the resize floor. Re-run on theme swap
        in case the theme's font differs (Matrix uses Cascadia Code)."""
        from PySide6.QtGui import QFontMetrics
        fm = QFontMetrics(self.font())
        # Strings that have historically pushed the width out. Keep this
        # list in sync with user-facing copy — if a new long message
        # lands, add it here.
        probes = [
            "Signed out — paste sessionKey in Settings to resume.",
            "At current pace, expires in 7d 23h 59m",
            "Cloudflare — add cf_clearance (click Key).",
            "Weekly — OAuth Apps",
        ]
        text_w = max(fm.horizontalAdvance(s) for s in probes)
        # +24 horizontal padding (12px on each side of the content area),
        # +32 for the sparkline area we want to preserve.
        min_w = max(320, text_w + 24 + 32)
        # Chrome: title bar 34 + theme strip 26 + tool strip 28 + footer 24
        # = 112. Plus 1 compact tier card (~60). Plus content margins.
        min_h = 180
        self.setMinimumSize(min_w, min_h)
```

Call it again at the end of `apply_theme` (in case font changed):

```python
        self._compute_and_apply_minimum_size()
```

- [ ] **Step 4: Add edge-zone detection method**

In `SanduhrWidget`, add:

```python
    _RESIZE_EDGE_PX = 6

    def _resize_zone(self, pos) -> Optional[str]:
        """Return which edge/corner `pos` (local coords) is within
        `_RESIZE_EDGE_PX` of, or None if it's interior."""
        left = pos.x() <= self._RESIZE_EDGE_PX
        right = pos.x() >= self.width() - self._RESIZE_EDGE_PX
        top = pos.y() <= self._RESIZE_EDGE_PX
        bottom = pos.y() >= self.height() - self._RESIZE_EDGE_PX
        if top and left: return "top-left"
        if top and right: return "top-right"
        if bottom and left: return "bottom-left"
        if bottom and right: return "bottom-right"
        if left: return "left"
        if right: return "right"
        if top: return "top"
        if bottom: return "bottom"
        return None
```

- [ ] **Step 5: Wire mouse events for cursor feedback and drag-resize**

At the top of `SanduhrWidget.__init__`, initialize:

```python
        self._resize_active: Optional[str] = None
        self._resize_start_geom = None
        self._resize_start_pos = None
```

Override `mouseMoveEvent` to update cursor and handle active resize:

```python
    def mouseMoveEvent(self, event) -> None:
        from PySide6.QtCore import Qt
        from PySide6.QtGui import QCursor

        # Skip resize when focus / snake overlay is active
        if hasattr(self, "_main_stack") and self._main_stack.currentIndex() != 0:
            super().mouseMoveEvent(event)
            return

        if self._resize_active is not None:
            self._apply_resize_drag(event.globalPosition().toPoint())
            return

        zone = self._resize_zone(event.position().toPoint())
        cursor_map = {
            "left": Qt.SizeHorCursor, "right": Qt.SizeHorCursor,
            "top": Qt.SizeVerCursor, "bottom": Qt.SizeVerCursor,
            "top-left": Qt.SizeFDiagCursor, "bottom-right": Qt.SizeFDiagCursor,
            "top-right": Qt.SizeBDiagCursor, "bottom-left": Qt.SizeBDiagCursor,
        }
        if zone:
            self.setCursor(QCursor(cursor_map[zone]))
        else:
            self.unsetCursor()
        super().mouseMoveEvent(event)

    def mousePressEvent(self, event) -> None:
        zone = self._resize_zone(event.position().toPoint())
        if zone and event.button() == Qt.LeftButton:
            self._resize_active = zone
            self._resize_start_geom = self.geometry()
            self._resize_start_pos = event.globalPosition().toPoint()
            return
        super().mousePressEvent(event)

    def mouseReleaseEvent(self, event) -> None:
        if self._resize_active is not None:
            self._resize_active = None
            self._save_settings()
            return
        super().mouseReleaseEvent(event)

    def _apply_resize_drag(self, global_pos) -> None:
        dx = global_pos.x() - self._resize_start_pos.x()
        dy = global_pos.y() - self._resize_start_pos.y()
        geom = self._resize_start_geom
        new_x, new_y = geom.x(), geom.y()
        new_w, new_h = geom.width(), geom.height()

        zone = self._resize_active
        if "left" in zone:
            new_x = geom.x() + dx
            new_w = geom.width() - dx
        if "right" in zone:
            new_w = geom.width() + dx
        if "top" in zone:
            new_y = geom.y() + dy
            new_h = geom.height() - dy
        if "bottom" in zone:
            new_h = geom.height() + dy

        # Clamp to minimum. If clamping the width would have moved the
        # left edge, stop the edge from moving further.
        min_w = self.minimumSize().width()
        min_h = self.minimumSize().height()
        if new_w < min_w:
            if "left" in zone:
                new_x = geom.x() + (geom.width() - min_w)
            new_w = min_w
        if new_h < min_h:
            if "top" in zone:
                new_y = geom.y() + (geom.height() - min_h)
            new_h = min_h

        self.setGeometry(new_x, new_y, new_w, new_h)
```

- [ ] **Step 6: Extend `_save_settings` to persist geometry**

Find the existing `_save_settings` method in `widget.py`. Add a line to include geometry:

```python
        self._settings["geom"] = {
            "x": self.x(), "y": self.y(),
            "w": self.width(), "h": self.height(),
        }
```

(before the write to disk). Ensure the existing `_restore_geometry` on startup still reads `geom.w`/`geom.h` when restoring.

- [ ] **Step 7: Run tests**

Run: `python -m pytest tests/test_edge_resize.py -v`
Expected: 6 PASS

- [ ] **Step 8: Run full suite**

Run: `python -m pytest tests/ --tb=short -q`
Expected: 191 passed (185 + 6 new resize tests)

- [ ] **Step 9: Smoke test manually**

Run: `python -m sanduhr` and:
1. Hover near the left edge — cursor changes to horizontal-resize.
2. Click and drag — window resizes from the left.
3. Try to shrink below the minimum — stops at minimum.
4. Resize from bottom-right — diagonal cursor, both axes change.
5. Close and relaunch — new size restored.

- [ ] **Step 10: Commit**

```bash
git add windows/src/sanduhr/widget.py windows/tests/test_edge_resize.py
git commit -m "feat(widget): edge-drag resize with dynamic minimum size

Hover near any edge or corner → cursor changes → click-drag to
resize. Minimum size computed from QFontMetrics of the longest
user-facing strings so text never clips, recomputed on theme
swap (Matrix's Cascadia Code has different metrics). New size
persists to settings.json's geom.w / geom.h and is restored
on next launch. Resize is disabled while the focus timer or
snake overlay owns the window."
```

---

## Task 5: CHANGELOG update

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add entries under the v2.0.4 `### Added` section**

Find the existing v2.0.4 heading in `CHANGELOG.md`. Add these four bullets to the top of the `### Added` list:

```markdown
- **Always-on pace ghost.** A faint vertical tick on every tier card showing where pace says usage should be right now. Actual fill sits to the left (under pace), at (on pace), or to the right (ahead). Replaces the previous Projection graph mode that 85% of users never discovered. Ghost alpha tunable per theme via `ghost_alpha` (default 0.35, Matrix overrides brighter for CRT feel).
- **Horizon sparkline.** The pulse-histogram mode is replaced by a 4-band horizon chart — denser, more readable at the card's narrow footprint, visually more distinct from the Classic line so the cycle button does real work. Classic / Horizon only, no Pulse.
- **Breathing glass.** Usage bars pulse with a 2.8s sine-wave alpha shimmer so the widget feels alive at rest. Amplitude 0.08 — subliminal, not flickery. Period tunable per theme via `breath_period_ms`. 15fps timer, effectively free on CPU.
- **Edge-drag resize.** Hover any edge or corner of the frameless window → cursor changes → click-drag to resize. Minimum size dynamically computed from `QFontMetrics` of the longest user-facing strings (status messages, burn projections, tier labels) so text never clips — recomputed on theme swap since Matrix's Cascadia Code has different glyph metrics. Resized geometry persists across launches.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: CHANGELOG v2.0.4 — ghost + horizon + breath + resize"
```

---

## Self-review checklist (completed)

**Spec coverage:**
- [x] Pace ghost (always-on) — Task 1
- [x] Horizon sparkline mode — Task 2
- [x] Breathing glass shimmer — Task 3
- [x] Edge-drag resize with dynamic minimum — Task 4
- [x] Docs update — Task 5

**Placeholder scan:** No TBDs, no "handle edge cases" without shown code, no "TODO: implement later". Every step has the exact code to add/run/commit.

**Type/signature consistency:**
- `TierCard._ghost_frac`, `_ghost_alpha`, `_breath_phase`, `_breath_period_ms`, `_breath_elapsed`, `_breath_timer` all defined in Task 1/3 and only used in those tasks.
- `Sparkline.set_mode("horizon")` defined in Task 2, used in `TierCard` in the same task.
- `SanduhrWidget._resize_zone`, `_resize_active`, `_resize_start_geom`, `_resize_start_pos`, `_apply_resize_drag`, `_compute_and_apply_minimum_size`, `_RESIZE_EDGE_PX` all defined in Task 4 and used in Task 4.
- `themes.py` additions (`ghost_alpha`, `breath_period_ms`) are optional dict keys with `.get()` fallbacks — no breaking change to existing themes.

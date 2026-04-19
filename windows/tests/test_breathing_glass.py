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


def test_breath_renders_without_crash(qtbot):
    """Smoke test — the breath overlay must not raise when painted at
    typical sizes and utilizations. Matches the shape of Task 2's
    `test_horizon_renders_without_crash`."""
    from sanduhr.tiers import TierCard
    from PySide6.QtGui import QPixmap

    card = TierCard(tier_key="five_hour", label="Session", theme=_obsidian())
    qtbot.addWidget(card)
    card.resize(300, 80)

    # Simulate a typical mid-session state so the breath overlay has
    # something to paint (guard requires _util > 0).
    from datetime import datetime, timezone, timedelta
    future = datetime.now(timezone.utc) + timedelta(hours=2.5)
    card.update_state(
        util=50,
        resets_at=future.isoformat().replace("+00:00", "Z"),
        history_values=[],
    )

    pm = QPixmap(card.size())
    card.render(pm)
    assert not pm.isNull()

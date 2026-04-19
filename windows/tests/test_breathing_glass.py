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

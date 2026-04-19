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

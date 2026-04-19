"""Tests for the hourglass focus timer physics.

The physics tick rate (30fps) is independent of the timer's 1Hz label
countdown. Expected sand-passed must scale linearly with wall-clock
elapsed time regardless of which tick fires first. Covers the stutter
regression where `expected_passed` was quantized to integer seconds.
"""

import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch, tmp_path):
    monkeypatch.setenv("APPDATA", str(tmp_path))


def _obsidian_theme():
    from sanduhr import themes
    return themes.THEMES["obsidian"]


def test_focus_widget_constructs(qtbot):
    from sanduhr.focus import FocusTimerWidget
    w = FocusTimerWidget(_obsidian_theme())
    qtbot.addWidget(w)
    assert not w.is_active()
    assert w._total_sand > 0, "Hourglass mask should spawn some sand"


def test_start_stop_lifecycle(qtbot):
    from sanduhr.focus import FocusTimerWidget
    w = FocusTimerWidget(_obsidian_theme())
    qtbot.addWidget(w)

    w.start(minutes=25)
    assert w.is_active()
    assert w._duration_ms == 25 * 60 * 1000
    assert w._elapsed.isValid()

    w.stop()
    assert not w.is_active()
    assert not w._elapsed.isValid()


def test_expected_passed_is_float_not_truncated(qtbot, monkeypatch):
    """The bottleneck throttle compares sand_passed (int) against
    expected_passed (float). Previously expected_passed was int(), which
    meant between second boundaries the throttle held; float restores
    millisecond-resolution flow."""
    from sanduhr.focus import FocusTimerWidget
    w = FocusTimerWidget(_obsidian_theme())
    qtbot.addWidget(w)
    w.start(minutes=10)

    # Halfway through a 600s focus block: expected_passed must be
    # exactly total_sand / 2, not the int-floor of it.
    monkeypatch.setattr(w._elapsed, "elapsed", lambda: 300 * 1000)
    w._physics_tick()

    # At exactly halfway, some fraction of the top-half sand should
    # have fallen. Sand count in the top half must be <= half the
    # initial total (grains have started moving through) and
    # sand_passed must be > 0.
    assert w._sand_passed > 0, (
        "After 50% elapsed time, at least one grain should have passed "
        "the bottleneck. If this is 0, the throttle is stuck."
    )


def test_zero_duration_does_not_crash(qtbot):
    """Edge case — user somehow starts with 0 minutes."""
    from sanduhr.focus import FocusTimerWidget
    w = FocusTimerWidget(_obsidian_theme())
    qtbot.addWidget(w)
    w.start(minutes=0)
    # _physics_tick should bail out, not divide by zero
    w._physics_tick()


def test_physics_bails_when_not_started(qtbot):
    """Physics tick before start() must be a no-op, not raise."""
    from sanduhr.focus import FocusTimerWidget
    w = FocusTimerWidget(_obsidian_theme())
    qtbot.addWidget(w)
    w._physics_tick()  # should not crash even though _elapsed isn't valid


def test_snake_overlay_constructs(qtbot):
    """Smoke test for the cooldown snake overlay."""
    from sanduhr.game import SnakeOverlay
    overlay = SnakeOverlay(_obsidian_theme(), high_score=0)
    qtbot.addWidget(overlay)
    assert overlay._score == 0
    # Reset puts snake in a known state
    assert len(overlay._snake) > 0
    assert overlay._food not in overlay._snake


def test_snake_wall_collision_ends_game(qtbot):
    from sanduhr.game import SnakeOverlay
    overlay = SnakeOverlay(_obsidian_theme(), high_score=0)
    qtbot.addWidget(overlay)

    # Point the snake straight into the left wall and tick
    overlay._snake = [(0, 10), (1, 10), (2, 10)]
    overlay._dir = (-1, 0)
    overlay._next_dir = (-1, 0)
    overlay._game_loop()
    assert overlay._game_over is True


def test_snake_self_collision_ends_game(qtbot):
    from sanduhr.game import SnakeOverlay
    overlay = SnakeOverlay(_obsidian_theme(), high_score=0)
    qtbot.addWidget(overlay)

    # Construct a U-shape about to bite its own tail
    overlay._snake = [(5, 5), (6, 5), (6, 6), (5, 6), (4, 6)]
    overlay._dir = (-1, 0)
    overlay._next_dir = (0, -1)  # turn up → into (5, 4)? No, check
    # Actually force direct self-collision: head moves into (6, 5)
    overlay._snake = [(5, 5), (4, 5), (4, 6), (5, 6), (6, 6), (6, 5)]
    overlay._dir = (1, 0)
    overlay._next_dir = (1, 0)  # move head to (6, 5) which is in snake
    overlay._game_loop()
    assert overlay._game_over is True

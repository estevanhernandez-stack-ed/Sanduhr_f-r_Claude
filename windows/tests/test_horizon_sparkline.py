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

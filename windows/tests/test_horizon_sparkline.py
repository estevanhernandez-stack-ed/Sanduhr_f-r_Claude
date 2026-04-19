"""Tests for the horizon-chart sparkline mode.

The horizon chart stacks the 2h usage history into four intensity
bands. Peaks render as dense dark regions; lulls show through as
soft backgrounds. More information per pixel than a line chart,
without the visual noise of a histogram.
"""

from PySide6.QtCore import Qt
from PySide6.QtGui import QPixmap


def test_horizon_mode_is_recognized(qtbot):
    from sanduhr.sparkline import Sparkline
    s = Sparkline()
    qtbot.addWidget(s)
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


def test_horizon_peaks_render_darker_than_lulls(qtbot):
    """The core visual claim of the horizon chart is that peaks accumulate
    alpha (multiple bands overlap) while lulls render single-band-soft.
    Sample a peak column vs a lull column and confirm the peak column
    contains more ink.

    Note: QWidget.render() flattens to opaque so we can't measure alpha
    directly. Instead we paint white ink over the black-flattened
    transparent background and measure accumulated RGB brightness —
    more overlaps = more white = higher sum."""
    from sanduhr.sparkline import Sparkline
    from PySide6.QtGui import QPixmap, QColor

    s = Sparkline()
    qtbot.addWidget(s)
    s.resize(200, 40)
    s.set_color("#ffffff")
    # Peak in the middle, lulls at the edges
    s.set_values([5, 5, 5, 5, 100, 100, 100, 100, 5, 5, 5, 5])
    s.set_mode("horizon")

    pm = QPixmap(s.size())
    pm.fill(Qt.transparent)
    s.render(pm)
    img = pm.toImage()

    mid_x = s.width() // 2
    edge_x = 10

    def column_brightness(x: int) -> int:
        total = 0
        for y in range(s.height()):
            c = QColor(img.pixel(x, y))
            total += c.red() + c.green() + c.blue()
        return total

    peak_brightness = column_brightness(mid_x)
    lull_brightness = column_brightness(edge_x)

    assert peak_brightness > lull_brightness * 1.5, (
        f"Peak column should be substantially brighter than lull column "
        f"(more accumulated white ink). Got peak={peak_brightness}, "
        f"lull={lull_brightness}."
    )

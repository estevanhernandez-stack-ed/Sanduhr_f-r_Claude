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


def test_chart_apply_theme_does_not_raise(qtbot):
    """Swapping themes at runtime must not raise and must still paint.
    Catches 'theme dict shape drifted' bugs that would otherwise only
    surface in manual QA."""
    from sanduhr.history_chart import HistoryChart
    from sanduhr import themes
    chart = HistoryChart(theme=themes.THEMES["obsidian"])
    qtbot.addWidget(chart)
    chart.apply_theme(themes.THEMES["aurora"])
    chart.resize(400, 300)
    pm = QPixmap(chart.size())
    chart.render(pm)
    assert not pm.isNull()

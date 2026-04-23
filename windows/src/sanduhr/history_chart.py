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

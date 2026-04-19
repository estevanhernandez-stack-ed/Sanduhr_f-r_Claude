"""Anti-aliased sparkline / pulse histogram widget drawn with QPainter.

Supports two visual modes:
  - "line"  : the classic smooth sparkline curve (default)
  - "pulse" : dense vertical bar histogram (Pulse mode)

Paints transparently over its parent's background so it sits cleanly
on card glass.
"""

from typing import List, Optional

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import QWidget


class Sparkline(QWidget):
    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._values: List[int] = []
        self._color = QColor("#ffffff")
        self._stroke_width = 1.5
        self._mode = "line"  # "line" or "pulse"
        self.setAttribute(Qt.WA_TranslucentBackground, True)

    def set_values(self, values: List[int]) -> None:
        self._values = list(values)
        self.update()

    def set_color(self, color: str) -> None:
        self._color = QColor(color)
        self.update()

    def set_stroke_width(self, width: float) -> None:
        self._stroke_width = width
        self.update()

    def set_mode(self, mode: str) -> None:
        """Switch between 'line' (classic sparkline) and 'pulse' (histogram)."""
        self._mode = mode
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        if len(self._values) < 2:
            return

        w = self.width()
        h = self.height()
        if w < 10 or h < 4:
            return

        if self._mode == "pulse":
            self._paint_pulse(w, h)
        else:
            self._paint_line(w, h)

    def _paint_line(self, w: int, h: int) -> None:
        mn = min(self._values)
        mx = max(self._values)
        rng = (mx - mn) if mx != mn else 1

        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        pen = QPen(self._color)
        pen.setWidthF(self._stroke_width)
        pen.setCapStyle(Qt.RoundCap)
        pen.setJoinStyle(Qt.RoundJoin)
        painter.setPen(pen)

        path = QPainterPath()
        denom = len(self._values) - 1
        for i, v in enumerate(self._values):
            x = (i / denom) * w
            y = h - ((v - mn) / rng) * (h - 2) - 1
            if i == 0:
                path.moveTo(x, y)
            else:
                path.lineTo(x, y)
        painter.drawPath(path)
        painter.end()

    def _paint_pulse(self, w: int, h: int) -> None:
        """Draw a dense bar histogram — each value becomes a vertical bar."""
        mn = min(self._values)
        mx = max(self._values)
        rng = (mx - mn) if mx != mn else 1

        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, False)

        n = len(self._values)
        gap = 1  # 1px gap between bars
        bar_w = max(1, (w - (n - 1) * gap) / n)

        for i, v in enumerate(self._values):
            norm = (v - mn) / rng  # 0..1
            bar_h = max(1, int(norm * (h - 2)))
            x = int(i * (bar_w + gap))
            y = h - bar_h

            # Subtle alpha gradient from bottom (strong) to top (vivid)
            c = QColor(self._color)
            c.setAlphaF(0.4 + 0.6 * norm)
            painter.fillRect(int(x), int(y), max(1, int(bar_w)), bar_h, c)

        painter.end()

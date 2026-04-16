"""Anti-aliased sparkline widget drawn with QPainter.

Shows the last N utilization values as a smooth line scaled to the
widget bounds. Paints transparently over its parent's background so
it sits cleanly on card glass.
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

    def paintEvent(self, event) -> None:  # noqa: N802
        if len(self._values) < 2:
            return

        w = self.width()
        h = self.height()
        if w < 10 or h < 4:
            return

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

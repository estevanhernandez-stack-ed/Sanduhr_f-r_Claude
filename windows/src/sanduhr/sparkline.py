"""Anti-aliased sparkline / horizon-chart widget drawn with QPainter.

Supports two visual modes:
  - "line"    : the classic smooth sparkline curve (default)
  - "horizon" : 4-band stacked horizon chart (dense, low noise)

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
        self._mode = "line"  # "line" or "horizon"
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
        """Switch between 'line' (classic sparkline) and 'horizon'
        (stacked horizon chart — dense info, low noise)."""
        self._mode = mode
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        if len(self._values) < 2:
            return

        w = self.width()
        h = self.height()
        if w < 10 or h < 4:
            return

        if self._mode == "horizon":
            self._paint_horizon(w, h)
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

    def _paint_horizon(self, w: int, h: int) -> None:
        """Horizon chart — stack 4 alpha-bands of the history.

        Each value is quantized into 4 bands at 25% intervals. Bands are
        drawn in order dark → light with increasing alpha, so peaks in
        the history pile up multiple overlapping bands and read as
        dense dark regions, while quiet stretches render as a single
        soft band."""
        mn = 0  # horizon bands are absolute percentage, not normalized
        mx = 100
        rng = mx - mn

        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, False)

        n = len(self._values)
        col_w = max(1, w / n)
        bands = 4
        for band in range(bands):
            band_floor = mn + (rng * band / bands)
            band_ceil = mn + (rng * (band + 1) / bands)
            # Alpha rises with band index: lowest band is softest.
            alpha = 0.18 + 0.20 * band  # 0.18, 0.38, 0.58, 0.78
            c = QColor(self._color)
            c.setAlphaF(alpha)

            for i, v in enumerate(self._values):
                if v <= band_floor:
                    continue
                # How much of this band does v fill?
                fill = min(v, band_ceil) - band_floor
                fill_ratio = fill / (band_ceil - band_floor)
                bar_h = max(1, int(fill_ratio * (h / bands)))
                x = int(i * col_w)
                # Band is anchored to bottom of widget, stacked upward
                band_bottom = h - (band * (h / bands))
                y = int(band_bottom - bar_h)
                painter.fillRect(x, y, max(1, int(col_w)), bar_h, c)

        painter.end()

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
        """Horizon chart — classic Heer/Tufte style.

        All 4 bands render from the widget bottom. Each band's bar
        height is proportional to how far the value reaches within
        that band's slice of the overall range, measured against the
        full widget height. A value of 100 fills band 0 to h/4, band
        1 to 2h/4, band 2 to 3h/4, band 3 to h — so the four
        bottom-anchored rectangles stack into 4-way overlap at the
        base and step down one band at a time as they reach toward
        the top. Alpha compositing turns that overlap into dense
        dark regions at peaks and single-band soft washes at lulls."""
        mn = 0  # horizon bands are absolute percentage, not normalized
        mx = 100

        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, False)

        n = len(self._values)
        col_w = max(1, w / n)
        bands = 4
        band_step = (mx - mn) / bands  # 25 per band

        for band in range(bands):
            band_floor = mn + band * band_step
            band_ceil = mn + (band + 1) * band_step
            # Alpha rises with band index — lowest band softest,
            # top band densest. A tall value crosses every band
            # and so its column gets all four alphas composited.
            alpha = 0.18 + 0.20 * band  # 0.18, 0.38, 0.58, 0.78
            c = QColor(self._color)
            c.setAlphaF(alpha)

            for i, v in enumerate(self._values):
                if v <= band_floor:
                    continue
                # Bar height is (v clamped to this band's ceiling)
                # mapped onto the FULL widget height — not scaled to
                # just this band's slice. That mapping is what makes
                # tall values reach high while still having every
                # band anchored at the bottom.
                effective = min(v, band_ceil)
                bar_h = max(1, int((effective - mn) / (mx - mn) * h))
                x = int(i * col_w)
                y = int(h - bar_h)
                painter.fillRect(x, y, max(1, int(col_w)), bar_h, c)

        painter.end()

"""TierCard -- one rendered usage tier, updates in place.

Handles label, sparkline, percentage number, progress bar with pace
marker, reset countdown, pacing label, burn projection. Receives a
theme dict and draws its card chrome (glass fill, border, shadow,
inner highlight) based on the theme's glass-tuning dials.
"""

from typing import List, Optional

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPen
from PySide6.QtWidgets import (
    QFrame,
    QGraphicsDropShadowEffect,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QVBoxLayout,
    QWidget,
)

from sanduhr import pacing, themes
from sanduhr.sparkline import Sparkline


def _rgba(hex_color: str, alpha: float) -> str:
    """Return 'rgba(r,g,b,a)' string from #rrggbb + alpha in [0,1]."""
    c = QColor(hex_color)
    return f"rgba({c.red()},{c.green()},{c.blue()},{alpha:.3f})"


class TierCard(QFrame):
    def __init__(
        self, tier_key: str, label: str, theme: dict, parent: Optional[QWidget] = None
    ):
        super().__init__(parent)
        self._tier_key = tier_key
        self._label = label
        self._theme = theme
        self._resets_at: Optional[str] = None
        self._util: int = 0
        self._show_deep_math = False

        self._build()
        self.apply_theme(theme)

    # -- public API -----------------------------------------------

    def update_state(
        self, util: int, resets_at: Optional[str], history_values: List[int]
    ) -> None:
        """Mutate labels and bar in place -- no rebuild."""
        self._util = util
        self._resets_at = resets_at

        color = themes.usage_color(util)
        self._pct.setText(f"{util}%")
        self._pct.setStyleSheet(f"color: {color};")

        self._bar.setValue(util)
        self._bar.setStyleSheet(self._bar_qss(color))

        self._reset_lbl.setText(
            "" if not resets_at else f"Resets in {pacing.time_until(resets_at)}"
        )
        self._reset_dt_lbl.setText(pacing.reset_datetime_str(resets_at))

        self._update_pace_lbl()

        burn = pacing.burn_projection(util, resets_at, self._tier_key)
        self._burn_lbl.setText(burn[0] if burn else "")
        self._burn_lbl.setStyleSheet(
            f"color: {burn[1]};" if burn else f"color: {self._theme['text_muted']};"
        )

        self._spark.set_values(history_values)
        self._spark.set_color(self._theme["sparkline"])

        self._update_pace_marker()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self.setStyleSheet(self._card_qss())
        self._lbl.setStyleSheet(f"color: {theme['text_secondary']};")
        self._pct.setStyleSheet(f"color: {theme['text']};")
        self._reset_lbl.setStyleSheet(f"color: {theme['text_dim']};")
        self._reset_dt_lbl.setStyleSheet(f"color: {theme['text_muted']};")
        self._spark.set_color(theme["sparkline"])
        self._apply_shadow()
        self._bar.setStyleSheet(self._bar_qss(themes.usage_color(self._util)))

    # -- for tests -------------------------------------------------

    def label_text(self) -> str:
        return self._lbl.text()

    def percentage_text(self) -> str:
        return self._pct.text()

    def reset_text(self) -> str:
        return self._reset_lbl.text()

    def current_theme_name(self) -> str:
        return self._theme["name"]

    # -- build -----------------------------------------------------

    def _build(self) -> None:
        self.setObjectName("TierCard")
        outer = QVBoxLayout(self)
        outer.setContentsMargins(14, 12, 14, 12)
        outer.setSpacing(6)

        row1 = QHBoxLayout()
        row1.setSpacing(8)
        self._lbl = QLabel(self._label)
        self._lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row1.addWidget(self._lbl)
        row1.addStretch()
        self._spark = Sparkline()
        self._spark.setFixedSize(50, 16)
        row1.addWidget(self._spark)
        self._pct = QLabel("0%")
        self._pct.setAttribute(Qt.WA_TranslucentBackground, True)
        row1.addWidget(self._pct)
        outer.addLayout(row1)

        self._bar_container = QWidget()
        self._bar_container.setFixedHeight(16)
        bar_layout = QHBoxLayout(self._bar_container)
        bar_layout.setContentsMargins(0, 0, 0, 0)
        self._bar = QProgressBar()
        self._bar.setRange(0, 100)
        self._bar.setValue(0)
        self._bar.setTextVisible(False)
        self._bar.setFixedHeight(16)
        bar_layout.addWidget(self._bar)
        self._pace_marker = QWidget(self._bar_container)
        self._pace_marker.setFixedWidth(3)
        self._pace_marker.setFixedHeight(16)
        self._pace_marker.hide()
        outer.addWidget(self._bar_container)

        row3 = QHBoxLayout()
        self._reset_lbl = QLabel("")
        self._reset_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row3.addWidget(self._reset_lbl)
        row3.addStretch()
        self._pace_lbl = QLabel("")
        self._pace_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        self._pace_lbl.setCursor(Qt.PointingHandCursor)
        self._pace_lbl.mousePressEvent = self._toggle_deep_math
        row3.addWidget(self._pace_lbl)
        outer.addLayout(row3)

        row4 = QHBoxLayout()
        self._reset_dt_lbl = QLabel("")
        self._reset_dt_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row4.addWidget(self._reset_dt_lbl)
        row4.addStretch()
        self._burn_lbl = QLabel("")
        self._burn_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row4.addWidget(self._burn_lbl)
        outer.addLayout(row4)

    def _toggle_deep_math(self, event) -> None:
        if event.button() == Qt.LeftButton:
            self._show_deep_math = not self._show_deep_math
            self._update_pace_lbl()

    def _update_pace_lbl(self) -> None:
        if self._show_deep_math:
            cooldown = pacing.calculate_cooldown(self._util, self._resets_at, self._tier_key)
            if cooldown:
                self._pace_lbl.setText(f"Cool down: {cooldown}")
                self._pace_lbl.setStyleSheet(f"color: {self._theme['text_dim']};")
                return
            surplus = pacing.calculate_surplus(self._util, self._resets_at, self._tier_key)
            if surplus:
                self._pace_lbl.setText(f"Surplus: {surplus}%")
                self._pace_lbl.setStyleSheet(f"color: {self._theme['text_dim']};")
                return
                
        pace = pacing.pace_info(self._util, self._resets_at, self._tier_key)
        self._pace_lbl.setText(pace[0] if pace else "")
        self._pace_lbl.setStyleSheet(
            f"color: {pace[1]};" if pace else f"color: {self._theme['text_dim']};"
        )

    def _card_qss(self) -> str:
        t = self._theme
        radius = t.get("card_corner_radius", 10)
        glass_color = t.get("glass_on_mica", t["glass"])
        alpha = t.get("glass_alpha", 0.65)
        border_tint = t.get("border_tint") or t["border"]
        border_alpha = t.get("border_alpha", 0.4)
        return f"""
        QFrame#TierCard {{
            background-color: {_rgba(glass_color, alpha)};
            border: 1px solid {_rgba(border_tint, border_alpha)};
            border-radius: {radius}px;
        }}
        """

    def _bar_qss(self, fill: str) -> str:
        t = self._theme
        bar_bg = t.get("bar_bg", "#2a2a2a")
        return f"""
        QProgressBar {{
            border: none;
            border-radius: 4px;
            background-color: {bar_bg};
        }}
        QProgressBar::chunk {{
            background-color: {fill};
            border-radius: 4px;
        }}
        """

    def _apply_shadow(self) -> None:
        effect = QGraphicsDropShadowEffect(self)
        effect.setOffset(0, 2)
        effect.setBlurRadius(12)
        effect.setColor(QColor(0, 0, 0, 64))
        self.setGraphicsEffect(effect)

    def _update_pace_marker(self) -> None:
        f = pacing.pace_frac(self._resets_at, self._tier_key)
        if f is None:
            self._pace_marker.hide()
            return
        w = self._bar_container.width()
        if w <= 0:
            self._pace_marker.hide()
            return
        self._pace_marker.setStyleSheet(
            f"background-color: {self._theme['pace_marker']};"
        )
        self._pace_marker.move(int(f * w), 0)
        self._pace_marker.show()

    def paintEvent(self, event) -> None:  # noqa: N802
        super().paintEvent(event)
        hl = self._theme.get("inner_highlight")
        if not hl:
            return
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        color = QColor(hl["color"])
        color.setAlphaF(hl["alpha"])
        pen = QPen(color)
        pen.setWidthF(1.0)
        painter.setPen(pen)
        r = self.rect()
        radius = self._theme.get("card_corner_radius", 10)
        painter.drawLine(r.left() + radius, r.top() + 1, r.right() - radius, r.top() + 1)
        painter.end()

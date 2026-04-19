"""TierCard -- one rendered usage tier, updates in place.

Handles label, sparkline, percentage number, progress bar with pace
marker, reset countdown, pacing label, burn projection. Receives a
theme dict and draws its card chrome (glass fill, border, shadow,
inner highlight) based on the theme's glass-tuning dials.
"""

from typing import List, Optional

from PySide6.QtCore import Qt, QEvent
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


# Graph view modes — shared across all cards.
_GRAPH_MODES = ["classic", "projection", "pulse"]
_current_graph_mode = "classic"


def cycle_graph_mode() -> str:
    """Advance the shared graph mode and return the new mode name."""
    global _current_graph_mode
    idx = _GRAPH_MODES.index(_current_graph_mode)
    _current_graph_mode = _GRAPH_MODES[(idx + 1) % len(_GRAPH_MODES)]
    return _current_graph_mode


def current_graph_mode() -> str:
    return _current_graph_mode


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
        self._projected: Optional[float] = None

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

        # Cache projection for velocity shadow drawing
        self._projected = pacing.velocity_projection(util, resets_at, self._tier_key)

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

        # Sync sparkline display mode
        mode = current_graph_mode()
        self._spark.set_mode("pulse" if mode == "pulse" else "line")

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
        self._spark.setFixedSize(100, 16)
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

        self._pace_tick = QWidget(self._bar_container)
        self._pace_tick.setFixedHeight(16)
        self._pace_tick.setFixedWidth(3)
        self._pace_tick.hide()

        outer.addWidget(self._bar_container)

        row3 = QHBoxLayout()
        self._reset_lbl = QLabel("")
        self._reset_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        row3.addWidget(self._reset_lbl)
        row3.addStretch()
        self._pace_lbl = QLabel("")
        self._pace_lbl.setAttribute(Qt.WA_TranslucentBackground, True)
        self._pace_lbl.setCursor(Qt.PointingHandCursor)
        self._pace_lbl.installEventFilter(self)
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

    def eventFilter(self, obj, event) -> bool:
        if obj == self._pace_lbl:
            if event.type() == QEvent.Enter:
                self._show_deep_math = True
                self._update_pace_lbl()
                return True
            elif event.type() == QEvent.Leave:
                self._show_deep_math = False
                self._update_pace_lbl()
                return True
        return super().eventFilter(obj, event)

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
        if f is not None and self._bar_container.width() > 0:
            w = self._bar_container.width()
            rx = int(f * w)
            self._pace_tick.setStyleSheet(f"background-color: {self._theme['pace_marker']};")
            self._pace_tick.move(rx, 0)
            self._pace_tick.show()
            self._pace_tick.raise_()
        else:
            self._pace_tick.hide()

        # Just queue a repaint for the absolute geometry drawing
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        super().paintEvent(event)
        hl = self._theme.get("inner_highlight")
        
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        
        if hl:
            color = QColor(hl["color"])
            color.setAlpha(int(hl.get("alpha", 0.05) * 255))
            pen = QPen(color)
            pen.setWidth(1)
            painter.setPen(pen)
            r = self.rect()
            r.adjust(1, 1, -1, -1)
            radius = self._theme.get("card_corner_radius", 10) - 1
            painter.drawRoundedRect(r, radius, radius)

        # Draw velocity shadow (projection bar) when in projection mode
        mode = current_graph_mode()
        if mode == "projection" and self._projected is not None and self._bar_container.width() > 0:
            bar_w = self._bar_container.width()
            bar_x = self._bar_container.x()
            bar_y = self._bar_container.y()
            bar_h = self._bar_container.height()
            actual_end = bar_x + int((self._util / 100.0) * bar_w)
            proj_end = bar_x + int(min(self._projected, 100.0) / 100.0 * bar_w)
            if proj_end > actual_end:
                shadow_c = QColor(themes.usage_color(self._util))
                shadow_c.setAlphaF(0.25)
                painter.fillRect(actual_end, bar_y, proj_end - actual_end, bar_h, shadow_c)
                # Thin bright leading edge
                edge_c = QColor(themes.usage_color(self._util))
                edge_c.setAlphaF(0.6)
                painter.fillRect(proj_end - 1, bar_y, 2, bar_h, edge_c)

        # Pace tick is now drawn via an absolute positioned widget (_pace_tick)
        painter.end()

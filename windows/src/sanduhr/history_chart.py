"""HistoryChart — stacked line charts showing 30-day usage per tier.

One mini-chart per tier stacked vertically. Week / Month mode controls
the lookback window. Same QPainter pattern as Sparkline, just wider
canvas and slightly richer chrome (tier label + axis hint per row).

Multi-account aware: when set_account(None) is active, the chart
renders one colored line per account per tier (overlaid). When
set_account(label) is active, only that account's data is drawn in
the theme accent color (preserves single-account look). Empty-state
hint when no data; no crashes on first-run empty data.
"""

from typing import Dict, List, Optional

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import QWidget

from sanduhr import accounts, history


_TIER_LABELS = {
    "five_hour":            "Session (5hr)",
    "seven_day":            "Weekly — All Models",
    "seven_day_sonnet":     "Weekly — Sonnet",
    "seven_day_opus":       "Weekly — Opus",
    "seven_day_cowork":     "Weekly — Cowork",
    "seven_day_omelette":   "Weekly — Design",
    "seven_day_oauth_apps": "Weekly — OAuth Apps",
    "iguana_necktie":       "Weekly — Special",
    "extra_usage":          "API Credits",
}


# Stable per-account color palette for aggregate-view overlay. Picked
# for readability against both light and dark themes; cycles after the
# 5th registered account (most users have ≤2 — Personal + Work).
ACCOUNT_COLORS = (
    "#7DD3FC",  # sky-300
    "#FCA5A5",  # red-300
    "#86EFAC",  # green-300
    "#C4B5FD",  # violet-300
    "#FDE68A",  # yellow-300
)


def color_for_account(label: str) -> str:
    """Return the assigned color for an account label. Stable across
    refreshes — keyed on the account's position in the registry list."""
    labels = accounts.list_accounts()
    if label not in labels:
        return ACCOUNT_COLORS[0]
    return ACCOUNT_COLORS[labels.index(label) % len(ACCOUNT_COLORS)]


class HistoryChart(QWidget):
    def __init__(self, theme: dict, parent=None):
        super().__init__(parent)
        self._theme = theme
        self._mode = "week"  # "week" or "month"
        self._account: Optional[str] = None  # None = all accounts overlaid
        # Internal data shape is always {tier_key: {account_label: [points]}}
        # — uniform whether we're in aggregate or single-account mode, so the
        # paint loop doesn't branch.
        self._data: Dict[str, Dict[str, List[dict]]] = {}
        self.setMinimumHeight(240)

    def mode(self) -> str:
        return self._mode

    def set_mode(self, mode: str) -> None:
        self._mode = mode
        self.refresh()

    def account(self) -> Optional[str]:
        return self._account

    def set_account(self, account: Optional[str]) -> None:
        """Pass None for aggregate (all accounts overlaid), or a label
        to render only that account."""
        self._account = account
        self.refresh()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self.update()

    def refresh(self) -> None:
        days = 7 if self._mode == "week" else 30
        self._data = {}
        for tier_key in _TIER_LABELS:
            if self._account is None:
                # Aggregate: ask history for {account: [points]} per tier,
                # filter out empty accounts so paint doesn't draw blanks.
                agg = history.aggregate_window(tier_key, days)
                agg = {a: pts for a, pts in agg.items() if pts}
            else:
                pts = history.load_window(tier_key, days, account=self._account)
                agg = {self._account: pts} if pts else {}
            if agg:
                self._data[tier_key] = agg
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        t = self._theme

        fm = painter.fontMetrics()
        label_w = max(fm.horizontalAdvance(lbl) for lbl in _TIER_LABELS.values()) + 12

        w = self.width()
        h = self.height()
        if w < 180 or h < 40:
            painter.end()
            return

        if not self._data:
            painter.setPen(QColor(t["text_dim"]))
            painter.drawText(
                self.rect(), Qt.AlignCenter,
                "No history yet.\nData accumulates as you use Sanduhr."
            )
            painter.end()
            return

        n_tiers = len(self._data)
        row_h = h / n_tiers
        chart_x = label_w + 8
        chart_w = w - chart_x - 8

        for idx, (tier_key, per_account) in enumerate(self._data.items()):
            y_top = idx * row_h
            y_bottom = (idx + 1) * row_h - 4

            painter.setPen(QColor(t["text_secondary"]))
            painter.drawText(
                int(8), int(y_top), int(label_w), int(row_h - 4),
                Qt.AlignLeft | Qt.AlignVCenter,
                _TIER_LABELS.get(tier_key, tier_key),
            )

            # Line per account per tier — one in aggregate (could be many),
            # exactly one in single-account mode.
            for label, points in per_account.items():
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
                # Single-account view uses the theme accent so existing
                # users don't see a color change. Aggregate view uses the
                # per-account palette.
                if self._account is not None:
                    color = QColor(t["accent"])
                else:
                    color = QColor(color_for_account(label))
                pen = QPen(color)
                pen.setWidthF(1.5)
                pen.setCapStyle(Qt.RoundCap)
                pen.setJoinStyle(Qt.RoundJoin)
                painter.setPen(pen)
                painter.drawPath(path)

        painter.end()

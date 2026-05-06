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

from sanduhr import accounts, history, pacing


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
    "routines":             "Daily Routines",
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
        self.setMinimumHeight(280)

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
        # Reserve a column on the right for current-state labels — rendered
        # only in single-account mode. Two-line stacked when resets_at is
        # known ('95% left' / '3h 46m'); single-line when it's not ('47%').
        # Width sized to the wider of '100% left' and a 3-part time string
        # like '30d 23h 59m', plus a small gutter.
        value_w = max(
            fm.horizontalAdvance("100% left"),
            fm.horizontalAdvance("30d 23h 59m"),
        ) + 8

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

        # Reserve a strip at the bottom for time-axis tick marks +
        # 'older' / 'now' anchors. This is shared by all tier rows since
        # they all use the same x-scale. 22 px = a tick-mark band + a
        # font-line + descender clearance so anchors don't get clipped
        # by the parent layout.
        axis_h = 22
        rows_h = h - axis_h
        n_tiers = len(self._data)
        row_h = rows_h / n_tiers
        chart_x = label_w + 8
        chart_w = w - chart_x - value_w - 8

        # Light reference gridlines at 25 / 50 / 75 / 100 %. Locks the
        # eye onto a scale instead of letting the lines float.
        grid_color = QColor(t["text_dim"])
        grid_color.setAlpha(60)
        grid_pcts = (25, 50, 75, 100)

        for idx, (tier_key, per_account) in enumerate(self._data.items()):
            y_top = idx * row_h
            y_bottom = (idx + 1) * row_h - 4

            # Tier label (left column).
            painter.setPen(QColor(t["text_secondary"]))
            painter.drawText(
                int(8), int(y_top), int(label_w), int(row_h - 4),
                Qt.AlignLeft | Qt.AlignVCenter,
                _TIER_LABELS.get(tier_key, tier_key),
            )

            # Gridlines — drawn behind fills/lines.
            grid_pen = QPen(grid_color)
            grid_pen.setWidthF(1.0)
            grid_pen.setStyle(Qt.DotLine)
            painter.setPen(grid_pen)
            for pct in grid_pcts:
                gy = y_bottom - (pct / 100.0) * (row_h - 8)
                painter.drawLine(
                    int(chart_x), int(gy),
                    int(chart_x + chart_w), int(gy),
                )

            # Line + area-fill per account per tier — one in aggregate
            # (could be many), exactly one in single-account mode.
            most_recent = None
            for label, points in per_account.items():
                if len(points) < 2:
                    continue
                # Choose color: theme accent for single-account view
                # (preserves existing visual); per-account palette for
                # aggregate overlay.
                if self._account is not None:
                    color = QColor(t["accent"])
                else:
                    color = QColor(color_for_account(label))

                # Build the line path.
                path = QPainterPath()
                denom = len(points) - 1
                first_x = last_x = chart_x
                for i, p in enumerate(points):
                    x = chart_x + (i / denom) * chart_w
                    util = max(0, min(100, p.get("v", 0)))
                    y = y_bottom - (util / 100.0) * (row_h - 8)
                    if i == 0:
                        path.moveTo(x, y)
                        first_x = x
                    else:
                        path.lineTo(x, y)
                    last_x = x

                # Area fill under the line — adds visual mass so the
                # rows don't feel empty when values are small.
                fill_path = QPainterPath(path)
                fill_path.lineTo(last_x, y_bottom)
                fill_path.lineTo(first_x, y_bottom)
                fill_path.closeSubpath()
                fill_color = QColor(color)
                fill_color.setAlpha(40)
                painter.fillPath(fill_path, fill_color)

                # Stroke the line on top of the fill.
                pen = QPen(color)
                pen.setWidthF(2.0)
                pen.setCapStyle(Qt.RoundCap)
                pen.setJoinStyle(Qt.RoundJoin)
                painter.setPen(pen)
                painter.drawPath(path)

                if self._account is not None and points:
                    most_recent = (color, points[-1])

            # Right-edge state label (single-account mode only) — turns
            # the chart from 'historical scribble' into 'how much budget
            # do I have left and when does it reset'. Falls back to bare
            # '%' when resets_at is missing (older history points or
            # tiers like seven_day_omelette where the API returns null).
            if most_recent is not None:
                value_color, last_point = most_recent
                util = max(0, min(100, int(last_point.get("v", 0))))
                resets_at = last_point.get("resets_at")
                painter.setPen(value_color)
                if resets_at:
                    # Two-line stacked: '95% left' over '3h 46m'. Splits
                    # the row's vertical space evenly so each line gets
                    # half. Aligns the % to bottom of upper half and the
                    # time to top of lower half so they read as a single
                    # block centered on the chart row.
                    h_per_line = (row_h - 4) / 2
                    painter.drawText(
                        int(chart_x + chart_w + 4), int(y_top),
                        int(value_w - 4), int(h_per_line),
                        Qt.AlignRight | Qt.AlignBottom,
                        f"{100 - util}% left",
                    )
                    painter.drawText(
                        int(chart_x + chart_w + 4),
                        int(y_top + h_per_line),
                        int(value_w - 4), int(h_per_line),
                        Qt.AlignRight | Qt.AlignTop,
                        pacing.time_until(resets_at),
                    )
                else:
                    painter.drawText(
                        int(chart_x + chart_w + 4), int(y_top),
                        int(value_w - 4), int(row_h - 4),
                        Qt.AlignRight | Qt.AlignVCenter,
                        f"{util}%",
                    )

        # Time-axis tick marks along the bottom (shared x-scale).
        tick_count = 7 if self._mode == "week" else 4
        tick_y = rows_h - 1
        axis_pen = QColor(t["text_dim"])
        axis_pen_dim = QColor(t["text_dim"])
        axis_pen_dim.setAlpha(140)
        painter.setPen(axis_pen_dim)
        for i in range(tick_count + 1):
            x = chart_x + (i / tick_count) * chart_w
            painter.drawLine(int(x), int(tick_y), int(x), int(tick_y + 4))

        # 'older' / 'now' anchor labels.
        painter.setPen(axis_pen_dim)
        anchor_text_y = tick_y + 5
        anchor_h = axis_h - 5
        older_label = "7 days ago" if self._mode == "week" else "30 days ago"
        painter.drawText(
            int(chart_x), int(anchor_text_y),
            int(chart_w // 2), int(anchor_h),
            Qt.AlignLeft | Qt.AlignTop,
            older_label,
        )
        painter.drawText(
            int(chart_x + chart_w // 2), int(anchor_text_y),
            int(chart_w // 2), int(anchor_h),
            Qt.AlignRight | Qt.AlignTop,
            "now",
        )

        painter.end()

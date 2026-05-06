"""Local CC settings tab — read-only view of the local Claude Code
session-log aggregations.

Shows three things:
  1. Today's token total (prominent number)
  2. Last 30 days as a horizontal bar strip
  3. Per-project + per-skill breakdown tables (last 30 days)

The widget refreshes on show and every 30 s while visible. All data
comes from sanduhr.cc_logs — no network, no writes."""

from datetime import date, datetime, timedelta, timezone
from typing import Optional

from PySide6.QtCore import Qt, QTimer, Signal
from PySide6.QtGui import QColor, QPainter
from PySide6.QtWidgets import (
    QCheckBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QSizePolicy,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from sanduhr import cc_logs
from sanduhr.tiers import _format_tokens_compact


_LOOKBACK_DAYS = 30
_REFRESH_MS = 30_000  # match the widget's tick cadence


class _DailyBarStrip(QWidget):
    """Horizontal bar strip — one bar per day for the trailing
    _LOOKBACK_DAYS days. Bar height proportional to that day's token
    total relative to the max in the window. Days with zero usage
    render as a tiny baseline tick so the strip's day count is still
    readable."""

    def __init__(self, theme: dict, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._theme = theme
        self._by_day: dict[date, int] = {}
        self.setMinimumHeight(60)
        self.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)

    def set_data(self, by_day: dict[date, int]) -> None:
        self._by_day = dict(by_day)
        self.update()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self.update()

    def paintEvent(self, event) -> None:  # noqa: N802
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        t = self._theme
        w = self.width()
        h = self.height()

        # Build a contiguous date list so days with zero usage still
        # render as a baseline marker — gaps would make the strip
        # misread as 'a few sparse events' instead of '30 days'.
        today = datetime.now(timezone.utc).astimezone().date()
        days = [today - timedelta(days=i) for i in range(_LOOKBACK_DAYS - 1, -1, -1)]
        max_v = max(self._by_day.values(), default=0)

        if not days or max_v == 0:
            painter.setPen(QColor(t["text_dim"]))
            painter.drawText(
                self.rect(), Qt.AlignCenter,
                "No local CC activity in the last 30 days."
            )
            painter.end()
            return

        # 1px gap between bars; bar width is whatever divides cleanly.
        gap = 1
        bar_area = max(1, w - 4)
        bar_w = max(2, (bar_area - gap * (_LOOKBACK_DAYS - 1)) // _LOOKBACK_DAYS)
        x = 2
        baseline_y = h - 4

        bar_color = QColor(t["accent"])
        baseline_color = QColor(t["text_dim"])
        baseline_color.setAlpha(80)

        for d in days:
            v = self._by_day.get(d, 0)
            if v == 0:
                # Show a 1px baseline tick so empty days are visible
                painter.fillRect(int(x), int(baseline_y), int(bar_w), 1, baseline_color)
            else:
                bar_h = max(2, int((v / max_v) * (h - 8)))
                painter.fillRect(
                    int(x), int(baseline_y - bar_h), int(bar_w), int(bar_h),
                    bar_color,
                )
            x += bar_w + gap

        painter.end()


class LocalCCTab(QWidget):
    """Settings tab body — Local Claude Code usage summary."""

    # Emitted when the breakdown-tables visibility checkbox toggles.
    # Parent SettingsDialog persists this via settingsSaved.
    showBreakdownsChanged = Signal(bool)

    def __init__(
        self,
        theme: Optional[dict] = None,
        show_breakdowns: bool = True,
        parent: Optional[QWidget] = None,
    ):
        super().__init__(parent)
        self._theme = theme or {}

        v = QVBoxLayout(self)

        intro = QLabel(
            "Token usage from local Claude Code sessions on this "
            "machine. Read directly from the session JSONL files in "
            "your ~/.claude folder — no network, no upload. "
            "Anthropic's /usage endpoint lags real consumption by "
            "minutes; this is the canonical local view of what's "
            "actually being burned."
        )
        intro.setWordWrap(True)
        v.addWidget(intro)

        # Two prominent totals side by side — Today vs Last 30 Days.
        # Earlier version was a single 'Today's local CC' headline that
        # got read as the 30-day total because the bar strip's
        # 'Last 30 days' caption sat right next to it. Showing both
        # numbers explicitly removes the ambiguity.
        totals_row = QHBoxLayout()

        today_box = QVBoxLayout()
        self._today_caption = QLabel("Today")
        self._today_caption.setStyleSheet("font-size: 9pt;")
        today_box.addWidget(self._today_caption)
        self._today_lbl = QLabel("—")
        self._today_lbl.setStyleSheet("font-size: 22pt; font-weight: bold;")
        today_box.addWidget(self._today_lbl)
        totals_row.addLayout(today_box)

        month_box = QVBoxLayout()
        self._month_caption = QLabel(f"Last {_LOOKBACK_DAYS} days")
        self._month_caption.setStyleSheet("font-size: 9pt;")
        month_box.addWidget(self._month_caption)
        self._month_lbl = QLabel("—")
        self._month_lbl.setStyleSheet("font-size: 22pt; font-weight: bold;")
        month_box.addWidget(self._month_lbl)
        totals_row.addLayout(month_box)

        totals_row.addStretch()
        v.addLayout(totals_row)

        # 30-day bar strip — caption removed; the 'Last 30 days' number
        # above already declares scope, no need to repeat.
        self._bars = _DailyBarStrip(self._theme)
        v.addWidget(self._bars)

        # Show/hide toggle for the breakdown tables. Some users prefer
        # the lean today + bars view without scrolling past per-project
        # detail. Persisted via showBreakdownsChanged signal so the
        # preference sticks across launches.
        self._chk_show_breakdowns = QCheckBox("Show project & skill breakdown")
        self._chk_show_breakdowns.setChecked(show_breakdowns)
        self._chk_show_breakdowns.toggled.connect(self._on_show_breakdowns_toggled)
        v.addWidget(self._chk_show_breakdowns)

        # Per-project + per-skill tables side by side. Wrapped in a
        # container QWidget so the whole row hides cleanly when the
        # checkbox is off (hiding individual layouts is fiddlier).
        self._tables_widget = QWidget()
        tables_row = QHBoxLayout(self._tables_widget)
        tables_row.setContentsMargins(0, 0, 0, 0)

        proj_box = QVBoxLayout()
        proj_caption = QLabel(f"Top projects (last {_LOOKBACK_DAYS} days)")
        proj_caption.setStyleSheet("font-size: 9pt;")
        proj_box.addWidget(proj_caption)
        self._proj_table = self._make_table()
        proj_box.addWidget(self._proj_table)
        tables_row.addLayout(proj_box)

        skill_box = QVBoxLayout()
        skill_caption = QLabel(f"Top skills (last {_LOOKBACK_DAYS} days)")
        skill_caption.setStyleSheet("font-size: 9pt;")
        skill_box.addWidget(skill_caption)
        self._skill_table = self._make_table()
        skill_box.addWidget(self._skill_table)
        tables_row.addLayout(skill_box)

        v.addWidget(self._tables_widget)
        self._tables_widget.setVisible(show_breakdowns)

        # Refresh timer — runs only while the tab is visible (start in
        # showEvent, stop in hideEvent) so we don't burn disk I/O on a
        # tab the user can't see.
        self._refresh_timer = QTimer(self)
        self._refresh_timer.setInterval(_REFRESH_MS)
        self._refresh_timer.timeout.connect(self.refresh)

    def _on_show_breakdowns_toggled(self, checked: bool) -> None:
        self._tables_widget.setVisible(checked)
        self.showBreakdownsChanged.emit(checked)

    def _make_table(self) -> QTableWidget:
        t = QTableWidget(0, 2)
        t.setHorizontalHeaderLabels(["Name", "Tokens"])
        t.verticalHeader().setVisible(False)
        t.setSelectionMode(QTableWidget.NoSelection)
        t.setEditTriggers(QTableWidget.NoEditTriggers)
        t.setShowGrid(False)
        t.setAlternatingRowColors(False)
        t.horizontalHeader().setStretchLastSection(False)
        t.setColumnWidth(1, 80)
        t.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        return t

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self._bars.apply_theme(theme)
        secondary = theme.get("text_secondary", "")
        primary = theme.get("text", "")
        for cap in (self._today_caption, self._month_caption):
            cap.setStyleSheet(f"font-size: 9pt; color: {secondary};")
        for lbl in (self._today_lbl, self._month_lbl):
            lbl.setStyleSheet(
                f"font-size: 22pt; font-weight: bold; color: {primary};"
            )

    def showEvent(self, event) -> None:  # noqa: N802
        super().showEvent(event)
        self.refresh()
        self._refresh_timer.start()

    def hideEvent(self, event) -> None:  # noqa: N802
        super().hideEvent(event)
        self._refresh_timer.stop()

    def refresh(self) -> None:
        """Re-read the session logs and update all three widgets.
        Single-pass aggregation via cc_logs.aggregate_for_local_cc_tab
        with a 30 s cache — fast enough that the 30 s timer doesn't
        burn disk I/O even on heavy CC users."""
        try:
            agg = cc_logs.aggregate_for_local_cc_tab(days_back=_LOOKBACK_DAYS)
        except Exception:
            agg = {"by_day": {}, "by_project": {}, "by_skill": {}}
        by_day = agg["by_day"]
        by_proj = agg["by_project"]
        by_skill = agg["by_skill"]

        # Today's total — local date
        today = datetime.now(timezone.utc).astimezone().date()
        today_total = by_day.get(today, 0)
        self._today_lbl.setText(
            f"{_format_tokens_compact(today_total)} tokens"
            if today_total > 0
            else "No activity yet"
        )

        # Last-N-days aggregate — sum of all daily buckets.
        month_total = sum(by_day.values())
        self._month_lbl.setText(
            f"{_format_tokens_compact(month_total)} tokens"
            if month_total > 0
            else "No activity"
        )

        # 30-day bar strip
        self._bars.set_data(by_day)

        # Tables — top 10 each, sorted desc by token count.
        # For projects: combine entries that share a display basename.
        # Different absolute cwds (e.g., a project accessed via symlink
        # AND directly) otherwise show as duplicate rows in the table.
        proj_by_name: dict[str, int] = {}
        for cwd, tokens in by_proj.items():
            name = cc_logs.project_display_name(cwd)
            proj_by_name[name] = proj_by_name.get(name, 0) + tokens
        self._fill_table(
            self._proj_table,
            sorted(proj_by_name.items(), key=lambda kv: kv[1], reverse=True)[:10],
        )
        self._fill_table(
            self._skill_table,
            sorted(by_skill.items(), key=lambda kv: kv[1], reverse=True)[:10],
        )

    def _fill_table(self, table: QTableWidget, rows: list[tuple[str, int]]) -> None:
        table.setRowCount(len(rows))
        for i, (name, tokens) in enumerate(rows):
            name_item = QTableWidgetItem(name)
            tokens_item = QTableWidgetItem(_format_tokens_compact(tokens))
            tokens_item.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
            table.setItem(i, 0, name_item)
            table.setItem(i, 1, tokens_item)

"""SettingsDialog — consolidated user-facing settings.

Five tabs:
  - Themes:   paste JSON, open themes folder, copy agent prompt, reload
  - Pacing:   pacing tools toggle, session-end reminder
  - Accounts: multi-account registry — add / rename / set-active / remove.
              Replaces the old single-credentials tab; the same Add… modal
              collects sessionKey + cf_clearance.
  - History:  30-day usage chart with per-account / All-accounts toggle,
              Export CSV, Clear history.
  - Help:     getting started + DevTools cookie copy instructions.

The owning widget wires it up via `open(parent, ...)` and reacts to the
`credentialsSaved`, `credentialsCleared`, `themesChanged`, `settingsSaved`,
and `accountsChanged` signals. Tab indices are exposed as TAB_* class
constants so callers don't hardcode integers."""

import json
import logging
import re
from pathlib import Path
from typing import Optional

from PySide6.QtCore import QTimer, QUrl, Qt, Signal
from PySide6.QtGui import QDesktopServices, QGuiApplication
from PySide6.QtWidgets import (
    QAbstractItemView,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QTabWidget,
    QVBoxLayout,
    QWidget,
    QCheckBox,
    QSpinBox,
)

from sanduhr import paths, sounds, startup, themes
from sanduhr.accounts_dialog import AccountsTab
from sanduhr.history_chart import _TIER_LABELS, SPECULATIVE_TIERS
from sanduhr.local_cc_dialog import LocalCCTab

_log = logging.getLogger(__name__)


def _slugify(name: str) -> str:
    """'Sunset Neon' -> 'sunset-neon'."""
    s = re.sub(r"[^a-zA-Z0-9]+", "-", name.strip().lower())
    return s.strip("-") or "theme"


def _themes_dir() -> Path:
    d = paths.app_data_dir() / "themes"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _styled_msgbox(
    parent,
    icon,
    title: str,
    text: str,
    buttons=None,
):
    """Create a themed QMessageBox AND play our matching chime in
    place of the OS system sound. We suppress the OS sound by
    setting QMessageBox.NoIcon (the OS only beeps on Critical /
    Warning / Information icon-styled boxes); the icon argument is
    used to pick which of our tones to play (and to colour the
    title-text accent so the user still reads severity at a glance)."""
    box = QMessageBox(parent)
    # NoIcon suppresses the platform's automatic icon-style system
    # beep. Our chime fires below.
    box.setIcon(QMessageBox.NoIcon)
    box.setWindowTitle(title)

    # Severity-colored accent line in the title — replaces the
    # system icon as the visual hint that this is an
    # error / warning / info dialog.
    if icon == QMessageBox.Critical:
        marker = "⛔"
    elif icon == QMessageBox.Warning:
        marker = "⚠"
    elif icon == QMessageBox.Question:
        marker = "?"
    else:
        marker = ""
    box.setText(f"{marker} {text}" if marker else text)

    if buttons is not None:
        box.setStandardButtons(buttons)

    # Inherit the root widget's stylesheet so the dark theme applies
    # from the first paint, not the second.
    root = parent.window() if parent is not None else None
    if root is not None:
        box.setStyleSheet(root.styleSheet())

    # Play our chime instead of the OS beep.
    if icon == QMessageBox.Critical or icon == QMessageBox.Warning:
        sounds.play_error()
    elif icon == QMessageBox.Information:
        sounds.play_info()
    # Question dialogs are silent — they're prompts, not events.

    return box


def _agent_prompt_path() -> Path:
    """Find docs/themes/AGENT_PROMPT.md relative to the package.

    Works from source (`windows/src/sanduhr/` -> `../../../docs/themes/`)
    and from PyInstaller bundle (_MEIPASS/docs/themes/).
    """
    import sys
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        return Path(meipass) / "docs" / "themes" / "AGENT_PROMPT.md"
    here = Path(__file__).resolve().parent
    return here.parent.parent.parent / "docs" / "themes" / "AGENT_PROMPT.md"


class SettingsDialog(QDialog):
    # Tab indices — public so callers can pass them via initial_tab without
    # hardcoding integers that drift when the build order changes.
    TAB_THEMES = 0
    TAB_CARDS = 1
    # Backwards-compat alias — prior name pre-2026-05-06 was TAB_PACING.
    TAB_PACING = 1
    TAB_ACCOUNTS = 2
    TAB_HISTORY = 3
    TAB_LOCAL_CC = 4
    TAB_HELP = 5

    credentialsSaved = Signal(str, object)  # (session_key, cf_clearance | None)
    credentialsCleared = Signal()
    themesChanged = Signal()
    themeApplied = Signal(str)  # user clicked Apply on an installed theme
    settingsSaved = Signal(dict)
    accountsChanged = Signal()  # any registry mutation — widget re-reads label

    def __init__(
        self,
        parent: Optional[QWidget] = None,
        initial_tab: int = 0,
        settings: Optional[dict] = None,
        theme: Optional[dict] = None,
        trigger_add_account: bool = False,
    ):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setModal(True)
        self.resize(520, 520)

        self._settings = settings or {}
        self._theme = theme or themes.THEMES.get("obsidian")

        layout = QVBoxLayout(self)
        self._tabs = QTabWidget()
        layout.addWidget(self._tabs)

        self._build_themes_tab()
        self._build_cards_tab()
        self._build_accounts_tab()
        self._build_history_tab()
        self._build_local_cc_tab()
        self._build_help_tab()

        btns = QDialogButtonBox(QDialogButtonBox.Close)
        btns.rejected.connect(self.reject)
        btns.accepted.connect(self.accept)
        layout.addWidget(btns)

        self._tabs.setCurrentIndex(initial_tab)

        # First-run UX: when the welcome dialog has just told the user to
        # paste their sessionKey, drop them straight into the Add Account
        # modal. QTimer hop ensures the parent dialog is fully laid out
        # first so the modal renders on top correctly.
        if trigger_add_account:
            QTimer.singleShot(0, self._accounts_tab._on_add)

    # ── Themes tab ───────────────────────────────────────────────

    def _build_themes_tab(self) -> None:
        page = QWidget()
        v = QVBoxLayout(page)

        v.addWidget(QLabel(
            "Drop a theme JSON below (or paste what an AI agent returned).\n"
            "See docs/themes/AGENT_PROMPT.md for a ready-to-paste prompt."
        ))

        self._paste = QPlainTextEdit()
        self._paste.setPlaceholderText('{"name": "Sunset", "bg": "#1a0a1f", ...}')
        self._paste.setMinimumHeight(140)
        v.addWidget(self._paste)

        name_row = QHBoxLayout()
        name_row.addWidget(QLabel("Filename:"))
        self._filename = QLineEdit()
        self._filename.setPlaceholderText("auto-filled from JSON \"name\"")
        name_row.addWidget(self._filename)
        name_row.addWidget(QLabel(".json"))
        v.addLayout(name_row)

        self._paste.textChanged.connect(self._autofill_filename)

        # Action row
        act_row = QHBoxLayout()
        save_btn = QPushButton("Save && Apply")
        save_btn.clicked.connect(self._save_and_apply_theme)
        act_row.addWidget(save_btn)
        copy_btn = QPushButton("Copy agent prompt")
        copy_btn.clicked.connect(self._copy_agent_prompt)
        act_row.addWidget(copy_btn)
        open_btn = QPushButton("Open themes folder")
        open_btn.clicked.connect(self._open_themes_folder)
        act_row.addWidget(open_btn)
        act_row.addStretch()
        v.addLayout(act_row)

        # Installed list
        v.addWidget(QLabel("Installed user themes:"))
        self._list = QListWidget()
        self._list.setMaximumHeight(120)
        v.addWidget(self._list)

        list_row = QHBoxLayout()
        apply_btn = QPushButton("Apply Selected")
        apply_btn.clicked.connect(self._apply_selected_theme)
        list_row.addWidget(apply_btn)
        reload_btn = QPushButton("Reload themes")
        reload_btn.clicked.connect(self._reload_themes)
        list_row.addWidget(reload_btn)
        del_btn = QPushButton("Delete selected")
        del_btn.clicked.connect(self._delete_selected)
        list_row.addWidget(del_btn)
        list_row.addStretch()
        v.addLayout(list_row)

        # Double-click on a list item also applies — same gesture
        # the user expects on a 'pickable list'.
        self._list.itemDoubleClicked.connect(
            lambda _: self._apply_selected_theme()
        )

        self._refresh_list()
        self._tabs.addTab(page, "Themes")

    # ── Cards tab ────────────────────────────────────────────────

    def _build_cards_tab(self) -> None:
        page = QWidget()
        v = QVBoxLayout(page)

        v.addWidget(QLabel("<b>Pacing tools</b>"))
        v.addWidget(QLabel(
            "Changes save automatically. Toggle and the widget "
            "reflects it immediately."
        ))

        self._chk_pacing_tools = QCheckBox("Enable Pacing Calculators")
        self._chk_pacing_tools.setChecked(self._settings.get("pacing_tools_enabled", True))
        self._chk_pacing_tools.toggled.connect(self._auto_save_cards)
        v.addWidget(self._chk_pacing_tools)

        self._chk_remind = QCheckBox("Show reminder at 100% of session")
        self._chk_remind.setChecked(self._settings.get("remind_session_end", False))
        self._chk_remind.toggled.connect(self._auto_save_cards)
        v.addWidget(self._chk_remind)

        v.addSpacing(16)
        v.addWidget(QLabel("<b>Startup</b>"))
        if startup.is_packaged():
            note = QLabel(
                "Manage whether Sanduhr starts with Windows from "
                "Windows Settings → Startup apps."
            )
            note.setWordWrap(True)
            note.setObjectName("HelpText")
            note.setStyleSheet("font-size: 8pt;")
            v.addWidget(note)
            btn = QPushButton("Open Windows Startup settings…")
            btn.clicked.connect(lambda: startup.open_startup_settings())
            v.addWidget(btn)
        else:
            self._chk_autostart = QCheckBox("Start Sanduhr when Windows starts")
            self._chk_autostart.setChecked(startup.is_enabled())
            self._chk_autostart.toggled.connect(self._on_autostart_toggled)
            v.addWidget(self._chk_autostart)

        v.addSpacing(16)
        v.addWidget(QLabel("<b>Visible tier cards</b>"))
        v.addWidget(QLabel(
            "Drag to reorder. Uncheck to hide. Order and visibility "
            "save automatically."
        ))

        # Drag-and-drop reorderable list with checkable items. Replaces
        # the prior per-tier QCheckBox column — same visibility control
        # plus user-defined ordering. Persisted as two settings keys:
        #   `hidden_tiers` — list of unchecked tier_keys (matches the
        #     prior schema so old settings still resolve cleanly)
        #   `tier_order`  — list of tier_keys in user-chosen order;
        #     keys not in the saved order render after, in the default
        #     order, so newly-added tiers show up without surprise.
        self._tier_list = QListWidget()
        self._tier_list.setDragDropMode(QAbstractItemView.InternalMove)
        self._tier_list.setSelectionMode(QAbstractItemView.SingleSelection)
        self._tier_list.setMinimumHeight(220)

        # All tiers default to checked. Speculative ones won't pollute
        # the widget surface anyway (the render filter drops tiers with
        # null utilization) — leaving them checked means they auto-
        # appear if Anthropic ever ships data for your account, which
        # is a free discovery affordance. Users who don't want them in
        # the Cards list can uncheck explicitly.
        hidden_tiers = set(self._settings.get("hidden_tiers", []))
        saved_order = self._settings.get("tier_order", []) or []
        # Resolve final order: saved positions first, then any tiers
        # the saved order didn't know about (new release added tiers).
        seen: set[str] = set()
        ordered_keys: list[str] = []
        for key in saved_order:
            if key in _TIER_LABELS and key not in seen:
                ordered_keys.append(key)
                seen.add(key)
        for key in _TIER_LABELS:
            if key not in seen:
                ordered_keys.append(key)
                seen.add(key)

        for key in ordered_keys:
            label = _TIER_LABELS[key]
            # Soft tag on speculative tiers so the user understands why
            # they default to hidden — these rows correspond to API
            # fields most consumer accounts return null for. Tooltip
            # carries the longer explanation; the inline tag keeps the
            # row scannable.
            if key in SPECULATIVE_TIERS:
                label = f"{label}  ·  future use"
            item = QListWidgetItem(label)
            item.setData(Qt.UserRole, key)
            item.setFlags(
                item.flags()
                | Qt.ItemIsUserCheckable
                | Qt.ItemIsDragEnabled
            )
            if key in SPECULATIVE_TIERS:
                item.setToolTip(
                    "Future-use tier — the upstream API references "
                    "this name but most consumer accounts return null "
                    "for it. The widget only renders a card when real "
                    "data arrives, so leaving this checked is a free "
                    "discovery cue if Anthropic ever ships data here. "
                    "Uncheck to hide it from this list entirely."
                )
            item.setCheckState(
                Qt.Unchecked if key in hidden_tiers else Qt.Checked
            )
            self._tier_list.addItem(item)

        # itemChanged fires on check-state toggle. rowsMoved on the
        # underlying model fires on drag-drop reorder. Both routed to
        # the same auto-save handler.
        self._tier_list.itemChanged.connect(lambda _i: self._auto_save_cards())
        self._tier_list.model().rowsMoved.connect(
            lambda *_a: self._auto_save_cards()
        )
        v.addWidget(self._tier_list)

        v.addStretch()

        self._tabs.addTab(page, "Cards")

    def _on_autostart_toggled(self, checked: bool) -> None:
        """Persist the run-on-login preference (unpackaged build)."""
        try:
            startup.set_enabled(checked)
        except OSError:
            _log.exception("Failed to update run-on-login state")
            return
        sounds.play_toggle()

    def _auto_save_cards(self) -> None:
        """Auto-save handler — wired to every Cards-tab change.
        No explicit Save button; the visible change on the widget IS
        the confirmation. Subtle click tone confirms the toggle/move
        registered."""
        self._settings["pacing_tools_enabled"] = self._chk_pacing_tools.isChecked()
        self._settings["remind_session_end"] = self._chk_remind.isChecked()

        order: list[str] = []
        hidden: list[str] = []
        for i in range(self._tier_list.count()):
            item = self._tier_list.item(i)
            key = item.data(Qt.UserRole)
            order.append(key)
            if item.checkState() == Qt.Unchecked:
                hidden.append(key)
        self._settings["tier_order"] = order
        self._settings["hidden_tiers"] = hidden

        sounds.play_toggle()
        self.settingsSaved.emit(self._settings)

    # ── Accounts tab ─────────────────────────────────────────────

    def _build_accounts_tab(self) -> None:
        self._accounts_tab = AccountsTab()
        self._accounts_tab.activeAccountChanged.connect(self._on_active_account_changed)
        # Registry mutations re-emit so the widget can refresh its
        # active-account label.
        self._accounts_tab.accountsChanged.connect(self.accountsChanged.emit)
        self._tabs.addTab(self._accounts_tab, "Accounts")

    def _on_active_account_changed(self, session_key: str, cf_clearance) -> None:
        """Translate AccountsTab signal into the existing widget-facing
        signals. Empty session_key means last account removed → signed-out
        flow; otherwise it's a switch to a different active account."""
        if not session_key:
            self.credentialsCleared.emit()
        else:
            self.credentialsSaved.emit(session_key, cf_clearance)

    # ── History tab ──────────────────────────────────────────────

    def _build_history_tab(self) -> None:
        from sanduhr.history_chart import HistoryChart

        page = QWidget()
        v = QVBoxLayout(page)

        v.addWidget(QLabel(
            "Rolling 30-day usage history. Stored locally only — "
            "never uploaded. Export as CSV to analyze with any agent."
        ))

        # Mode + account selector row
        mode_row = QHBoxLayout()
        mode_row.addWidget(QLabel("Window:"))
        self._hist_week_btn = QPushButton("Week")
        self._hist_week_btn.setCheckable(True)
        self._hist_week_btn.setChecked(True)
        self._hist_month_btn = QPushButton("Month")
        self._hist_month_btn.setCheckable(True)
        mode_row.addWidget(self._hist_week_btn)
        mode_row.addWidget(self._hist_month_btn)
        mode_row.addSpacing(12)
        mode_row.addWidget(QLabel("Account:"))
        self._hist_account = QComboBox()
        self._hist_account.currentIndexChanged.connect(self._on_history_account_changed)
        mode_row.addWidget(self._hist_account)
        mode_row.addStretch()
        v.addLayout(mode_row)

        # Legend strip — only visible when 'All accounts' is selected.
        self._hist_legend = QWidget()
        self._hist_legend_layout = QHBoxLayout(self._hist_legend)
        self._hist_legend_layout.setContentsMargins(0, 0, 0, 0)
        v.addWidget(self._hist_legend)

        # Chart
        self._hist_chart = HistoryChart(theme=self._theme)
        v.addWidget(self._hist_chart, stretch=1)

        def _set_week():
            self._hist_week_btn.setChecked(True)
            self._hist_month_btn.setChecked(False)
            self._hist_chart.set_mode("week")

        def _set_month():
            self._hist_week_btn.setChecked(False)
            self._hist_month_btn.setChecked(True)
            self._hist_chart.set_mode("month")

        self._hist_week_btn.clicked.connect(_set_week)
        self._hist_month_btn.clicked.connect(_set_month)

        # Action row
        action_row = QHBoxLayout()
        export_btn = QPushButton("Export CSV…")
        export_btn.clicked.connect(self._export_history_csv)
        action_row.addWidget(export_btn)

        clear_btn = QPushButton("Clear history")
        clear_btn.clicked.connect(self._clear_history)
        action_row.addWidget(clear_btn)
        action_row.addStretch()
        v.addLayout(action_row)

        self._refresh_history_account_selector()
        # Re-populate the selector whenever the registry changes (Add /
        # Remove / Rename / Set Active in the Accounts tab).
        self.accountsChanged.connect(self._refresh_history_account_selector)

        self._tabs.addTab(page, "History")

    def _refresh_history_account_selector(self) -> None:
        """Rebuild the History tab's account combo from the registry."""
        from sanduhr import accounts as accounts_mod
        # Block signals while we rebuild so the currentIndexChanged
        # handler doesn't fire repeatedly during clear()/addItem().
        self._hist_account.blockSignals(True)
        prev = self._hist_account.currentData()
        self._hist_account.clear()
        self._hist_account.addItem("All accounts", None)
        for label in accounts_mod.list_accounts():
            self._hist_account.addItem(label, label)
        # Restore previous selection if it still exists, else default to
        # All accounts.
        idx = self._hist_account.findData(prev) if prev is not None else 0
        if idx < 0:
            idx = 0
        self._hist_account.setCurrentIndex(idx)
        self._hist_account.blockSignals(False)
        # Trigger the chart + legend update under the (possibly new)
        # selection.
        self._on_history_account_changed()

    def _on_history_account_changed(self) -> None:
        selected = self._hist_account.currentData()  # None = All accounts
        self._hist_chart.set_account(selected)
        self._refresh_history_legend(aggregate=(selected is None))

    def _refresh_history_legend(self, aggregate: bool) -> None:
        """Show one colored swatch per account when in All-accounts mode;
        hide the legend strip entirely in single-account mode."""
        # Clear existing entries
        while self._hist_legend_layout.count():
            item = self._hist_legend_layout.takeAt(0)
            w = item.widget()
            if w is not None:
                w.setParent(None)
        if not aggregate:
            self._hist_legend.setVisible(False)
            return
        from sanduhr import accounts as accounts_mod
        from sanduhr.history_chart import color_for_account
        labels = accounts_mod.list_accounts()
        if not labels:
            self._hist_legend.setVisible(False)
            return
        for label in labels:
            color = color_for_account(label)
            swatch = QLabel(
                f"<span style='color:{color}; font-size:14pt;'>●</span> "
                f"<span style='font-size:9pt;'>{label}</span>"
            )
            swatch.setTextFormat(Qt.RichText)
            self._hist_legend_layout.addWidget(swatch)
        self._hist_legend_layout.addStretch()
        self._hist_legend.setVisible(True)

    def _export_history_csv(self) -> None:
        from PySide6.QtWidgets import QFileDialog
        from sanduhr import csv_export
        from datetime import date

        # Honor the History tab's current account selection — None
        # exports all accounts (with account column), specific exports
        # just that account.
        selected_account = self._hist_account.currentData()
        scope_tag = (
            "all-accounts" if selected_account is None
            else selected_account.lower().replace(" ", "-")
        )
        default_name = f"Sanduhr-usage-{scope_tag}-{date.today().isoformat()}.csv"
        path, _ = QFileDialog.getSaveFileName(
            self, "Export usage history", default_name, "CSV files (*.csv)"
        )
        if not path:
            return
        try:
            count = csv_export.export_to_csv(path, account=selected_account)
        except OSError as e:
            _styled_msgbox(
                self, QMessageBox.Critical, "Export failed",
                f"Could not write {path}:\n{e}",
            ).exec_()
            return
        sounds.play_save_confirmation()
        _styled_msgbox(
            self, QMessageBox.Information, "Exported",
            f"Wrote {count} rows to:\n{path}",
        ).exec_()

    def _clear_history(self) -> None:
        from sanduhr import history
        confirm = _styled_msgbox(
            self, QMessageBox.Warning, "Clear history?",
            "This permanently removes the local 30-day usage history "
            "file. Sparkline and history chart will be empty until new "
            "data accumulates.\n\nCredentials are not affected.",
            buttons=QMessageBox.Yes | QMessageBox.No,
        )
        confirm.setDefaultButton(QMessageBox.No)
        if confirm.exec_() != QMessageBox.Yes:
            return
        try:
            history.clear_all()
        except OSError as e:
            _styled_msgbox(
                self, QMessageBox.Critical, "Clear failed",
                f"Could not clear history file:\n{e}",
            ).exec_()
            return
        self._hist_chart.refresh()
        _styled_msgbox(
            self, QMessageBox.Information, "History cleared",
            "Local usage history file removed.",
        ).exec_()

    # ── Local CC tab ─────────────────────────────────────────────

    def _build_local_cc_tab(self) -> None:
        show_breakdowns = bool(
            self._settings.get("local_cc_show_breakdowns", True)
        )
        self._local_cc_tab = LocalCCTab(
            theme=self._theme,
            show_breakdowns=show_breakdowns,
        )
        self._local_cc_tab.showBreakdownsChanged.connect(
            self._on_local_cc_breakdowns_toggled
        )
        self._tabs.addTab(self._local_cc_tab, "Local CC")

    def _on_local_cc_breakdowns_toggled(self, show: bool) -> None:
        self._settings["local_cc_show_breakdowns"] = show
        self.settingsSaved.emit(self._settings)

    # ── Help tab ─────────────────────────────────────────────────

    def _build_help_tab(self) -> None:
        from PySide6.QtCore import Qt
        page = QWidget()
        v = QVBoxLayout(page)

        help_text = QLabel()
        help_text.setTextFormat(Qt.RichText)
        help_text.setTextInteractionFlags(Qt.TextBrowserInteraction)
        help_text.setOpenExternalLinks(True)
        help_text.setWordWrap(True)
        help_text.setText("""
<h3 style="margin-top:0">Keyboard shortcuts</h3>
<table cellpadding="4">
  <tr><td><b>Ctrl + R</b></td><td>Refresh usage now</td></tr>
  <tr><td><b>Ctrl + ,</b></td><td>Open Settings (this dialog)</td></tr>
  <tr><td><b>Ctrl + D</b></td><td>Toggle compact mode (highest-usage tier only)</td></tr>
  <tr><td><b>Ctrl + H</b></td><td>Open Help (this tab)</td></tr>
  <tr><td><b>Alt + F4</b></td><td>Close Sanduhr</td></tr>
</table>

<h3>Widget interactions</h3>
<ul>
  <li><b>Drag anywhere</b> on the widget to move it. Position persists across sessions.</li>
  <li><b>Double-click the title bar</b> to toggle compact mode (same as Ctrl+D).</li>
  <li><b>Right-click anywhere</b> for a quick menu: Refresh / Compact / Settings / Quit.</li>
  <li><b>Click a theme name</b> in the strip below the title to switch themes instantly.</li>
  <li><b>Pin button</b> toggles always-on-top.</li>
  <li><b>× (Close)</b> quits the widget.</li>
</ul>

<h3>What each tier card shows</h3>
<ul>
  <li><b>Percentage bar</b> — how much of that tier you've used this period.</li>
  <li><b>Bright colored tick</b> on the bar — the "on pace" marker (where you <i>should</i> be right now based on time elapsed).</li>
  <li><b>Inline sparkline</b> — your utilization trend over the last 2 hours.</li>
  <li><b>"Resets in Xd Xh"</b> — time until this tier's quota resets.</li>
  <li><b>"On pace" / "X% ahead" / "X% under"</b> — linear pace analysis.</li>
  <li><b>"At current pace, expires in X"</b> — burn-rate projection (only shown when you'd hit 100% before the reset).</li>
</ul>

<h3>Credentials tab</h3>
<p>On first run, paste your <code>sessionKey</code> cookie from <code>claude.ai</code>
(F12 → Application → Cookies → claude.ai). Stored in Windows Credential Manager,
service <code>com.626labs.sanduhr</code>. Update anytime here. If an account
requires Cloudflare clearance, also paste <code>cf_clearance</code>.</p>

<h3>Themes tab</h3>
<p>Five themes ship built-in. Author your own by dropping a JSON palette into
<code>%APPDATA%\\Sanduhr\\themes\\</code>, or paste one directly in the Themes
tab. The <b>Copy agent prompt</b> button copies a ready-to-use prompt you can
hand any chat agent (Claude, ChatGPT, etc.) along with a reference image or
vibe description to get back a drop-in theme JSON.</p>

<h3>More</h3>
<p>Source + issues: <a href="https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude">github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude</a><br/>
Privacy policy: <a href="https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md">docs/PRIVACY.md</a></p>
""")
        from PySide6.QtWidgets import QScrollArea
        scroll = QScrollArea()
        scroll.setWidget(help_text)
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QScrollArea.NoFrame)
        v.addWidget(scroll)
        self._tabs.addTab(page, "Help")

    def _autofill_filename(self) -> None:
        """Try to pull name from JSON in the paste buffer; fill filename if empty."""
        if self._filename.text().strip():
            return
        try:
            data = json.loads(self._paste.toPlainText())
        except (json.JSONDecodeError, ValueError):
            return
        name = data.get("name")
        if isinstance(name, str) and name:
            self._filename.setText(_slugify(name))

    def _save_and_apply_theme(self) -> None:
        raw = self._paste.toPlainText().strip()
        if not raw:
            _styled_msgbox(
                self, QMessageBox.Warning, "Settings",
                "Paste a theme JSON first.",
            ).exec_()
            return
        try:
            data = json.loads(raw)
        except json.JSONDecodeError as e:
            _styled_msgbox(
                self, QMessageBox.Critical, "Settings",
                f"Invalid JSON: {e}",
            ).exec_()
            return

        filename = self._filename.text().strip()
        if not filename:
            name = data.get("name", "")
            if isinstance(name, str):
                filename = _slugify(name)
        if not filename:
            _styled_msgbox(
                self, QMessageBox.Warning, "Settings",
                "Filename is required.",
            ).exec_()
            return
        if not filename.endswith(".json"):
            filename = f"{filename}.json"

        target = _themes_dir() / filename
        try:
            target.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except OSError as e:
            _styled_msgbox(
                self, QMessageBox.Critical, "Settings",
                f"Could not write theme: {e}",
            ).exec_()
            return

        themes.load_user_themes()
        self._refresh_list()
        self.themesChanged.emit()
        sounds.play_save_confirmation()
        _styled_msgbox(
            self, QMessageBox.Information, "Settings",
            f"Saved and applied: {target.name}",
        ).exec_()
        self._paste.clear()
        self._filename.clear()

    def _copy_agent_prompt(self) -> None:
        path = _agent_prompt_path()
        try:
            text = path.read_text(encoding="utf-8")
        except OSError:
            text = self._fallback_prompt()
        QGuiApplication.clipboard().setText(text)
        _styled_msgbox(
            self, QMessageBox.Information, "Settings",
            "Agent prompt copied to clipboard.\n\n"
            "Paste it into any chat agent with a reference image or vibe "
            "description. Drop the returned JSON into the paste box above.",
        ).exec_()

    def _fallback_prompt(self) -> str:
        return (
            "Design a Sanduhr widget theme JSON. Required hex fields: "
            "name, bg, glass, glass_on_mica, title_bg, border, footer_bg, "
            "bar_bg, text, text_secondary, text_dim, text_muted, accent, "
            "pace_marker, sparkline. Optional: glass_alpha (0.70-0.90), "
            "border_alpha, border_tint, accent_bloom {blur, alpha}, "
            "inner_highlight {color, alpha}. Dark base colors only. "
            "Return valid JSON only, no markdown."
        )

    def _open_themes_folder(self) -> None:
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(_themes_dir())))

    def _apply_selected_theme(self) -> None:
        """Apply the user theme currently selected in the installed-list.
        File names look like 'my-theme.json'; the THEMES dict key is
        the stem lowercased (matches what themes.load_user_themes does)."""
        item = self._list.currentItem()
        if item is None:
            _styled_msgbox(
                self, QMessageBox.Warning, "Settings",
                "Pick a theme from the list first.",
            ).exec_()
            return
        key = Path(item.text()).stem.lower()
        if key not in themes.THEMES:
            _styled_msgbox(
                self, QMessageBox.Critical, "Settings",
                f"Theme '{key}' isn't loaded — try Reload themes first.",
            ).exec_()
            return
        self.themeApplied.emit(key)
        sounds.play_save_confirmation()
        # The widget's apply_theme handler pushes the fresh stylesheet
        # to us as part of its work — no manual refresh needed here.

    def _reload_themes(self) -> None:
        themes.load_user_themes()
        self._refresh_list()
        self.themesChanged.emit()

    def _refresh_list(self) -> None:
        self._list.clear()
        for path in sorted(_themes_dir().glob("*.json")):
            self._list.addItem(path.name)

    def _delete_selected(self) -> None:
        item = self._list.currentItem()
        if not item:
            return
        name = item.text()
        confirm = _styled_msgbox(
            self, QMessageBox.Question, "Settings",
            f"Delete {name}?",
            buttons=QMessageBox.Yes | QMessageBox.No,
        )
        if confirm.exec_() != QMessageBox.Yes:
            return
        try:
            (_themes_dir() / name).unlink()
        except OSError as e:
            _styled_msgbox(
                self, QMessageBox.Critical, "Settings",
                f"Could not delete: {e}",
            ).exec_()
            return
        # Also drop from in-memory THEMES dict so the strip gets rebuilt cleanly
        key = Path(name).stem.lower()
        themes.THEMES.pop(key, None)
        self._refresh_list()
        self.themesChanged.emit()

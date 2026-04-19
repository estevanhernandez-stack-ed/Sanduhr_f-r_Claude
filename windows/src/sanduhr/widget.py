"""SanduhrWidget -- frameless always-on-top main window.

Composes title bar, theme strip, tier cards, footer. Manages drag,
compact mode, pin toggle, right-click context menu, window position
persistence, credentials dialog, and refresh scheduling.
"""

import json
import logging
import sys
import webbrowser
from datetime import datetime
from typing import Dict, Optional

from PySide6.QtCore import QEvent, QPoint, Qt, QThread, QTimer, Slot
from PySide6.QtGui import QColor, QGuiApplication, QKeySequence, QShortcut
from PySide6.QtWidgets import (
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMenu,
    QMessageBox,
    QPushButton,
    QSizePolicy,
    QStackedLayout,
    QVBoxLayout,
    QWidget,
)

from sanduhr import credentials, history, mica, paths, themes
from sanduhr.focus import FocusTimerWidget
from sanduhr.game import SnakeOverlay
from sanduhr.fetcher import UsageFetcher
from sanduhr.settings_dialog import SettingsDialog
from sanduhr.tiers import TierCard, cycle_graph_mode

_log = logging.getLogger(__name__)

_REFRESH_MS = 5 * 60 * 1000
_TICK_MS = 30 * 1000

_TIER_LABELS = {
    "five_hour":            "Session (5hr)",
    "seven_day":            "Weekly - All Models",
    "seven_day_sonnet":     "Weekly - Sonnet",
    "seven_day_opus":       "Weekly - Opus",
    "seven_day_cowork":     "Weekly - Cowork",
    "seven_day_omelette":   "Weekly - Routines",
    "seven_day_oauth_apps": "Weekly - OAuth Apps",
    "iguana_necktie":       "Weekly - Special",
}


class SanduhrWidget(QWidget):
    def __init__(self):
        super().__init__()
        self._settings = self._load_settings()
        self._theme_key = self._settings.get("theme", "obsidian")
        self._theme = themes.THEMES.get(self._theme_key, themes.THEMES["obsidian"])
        self._compact = False
        self._pinned = True
        self._drag_origin: Optional[QPoint] = None
        self._tier_cards: Dict[str, TierCard] = {}
        self._thread: Optional[QThread] = None
        self._fetcher: Optional[UsageFetcher] = None
        self._last: Optional[dict] = None

        self.setWindowTitle("Sanduhr für Claude")
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground, True)
        self.resize(420, 540)
        self._restore_geometry()

        self._build()
        self.apply_theme(self._theme_key)

        migration = credentials.migrate_from_v1()
        if migration.get("theme"):
            self._settings = self._load_settings()
            self.apply_theme(self._settings.get("theme", self._theme_key))

        creds = credentials.load()
        if not creds["session_key"]:
            # Preview mode: show what a real tier card looks like so the
            # empty pre-setup state demonstrates the feature instead of
            # looking like a broken app.
            QTimer.singleShot(0, self._render_preview)
            QTimer.singleShot(100, self._prompt_first_run)
        else:
            self._start_fetcher(creds["session_key"], creds["cf_clearance"])

        self._countdown = QTimer(self)
        self._countdown.timeout.connect(self._tick)
        self._countdown.start(_TICK_MS)

    # -- for tests -------------------------------------------------

    def status_text(self) -> str:
        return self._status_lbl.text()

    def current_theme_key(self) -> str:
        return self._theme_key

    # -- build -----------------------------------------------------

    def _build(self) -> None:
        outer = QVBoxLayout(self)
        outer.setContentsMargins(1, 1, 1, 1)
        outer.setSpacing(0)

        # Accent gradient strip at top
        self._accent_strip = QWidget()
        self._accent_strip.setObjectName("AccentStrip")
        self._accent_strip.setFixedHeight(2)
        outer.addWidget(self._accent_strip)

        # Title bar
        self._title_bar = QWidget()
        self._title_bar.setObjectName("TitleBar")
        self._title_bar.setFixedHeight(34)
        tb = QHBoxLayout(self._title_bar)
        tb.setContentsMargins(10, 0, 0, 0)
        tb.setSpacing(0)
        self._title_lbl = QLabel("Sanduhr für Claude")
        tb.addWidget(self._title_lbl)
        tb.addStretch()
        self._btn_refresh = QPushButton("Refresh")
        # Initial state is pinned (WindowStaysOnTopHint set on __init__),
        # so the button's first label must be the ACTION, i.e. "Unpin".
        self._btn_pin = QPushButton("Unpin")
        # Windows-native-style close button: wider, red hover via stylesheet,
        # uses the Unicode heavy multiplication sign that Windows Explorer
        # itself draws in title bars (rather than a plain lowercase x).
        self._btn_close = QPushButton("\u2715")
        self._btn_close.setObjectName("CloseButton")
        for b in (self._btn_refresh, self._btn_pin):
            b.setFlat(True)
            b.setCursor(Qt.PointingHandCursor)
            b.setFixedHeight(34)
        self._btn_close.setFlat(True)
        self._btn_close.setCursor(Qt.PointingHandCursor)
        self._btn_close.setFixedHeight(34)
        self._btn_close.setFixedWidth(46)  # matches Explorer/Settings close-button width

        # Tooltips — selective, for the controls whose purpose isn't obvious.
        self._btn_refresh.setToolTip("Refresh usage now (Ctrl+R)")
        self._btn_pin.setToolTip("Unpin (currently always on top)")
        self._btn_close.setToolTip("Close (Alt+F4)")
        self._title_lbl.setToolTip("Drag to move")

        # Accessible names — screen readers + MS Store review tooling look at these.
        self._btn_refresh.setAccessibleName("Refresh usage")
        self._btn_pin.setAccessibleName("Toggle always-on-top")
        self._btn_close.setAccessibleName("Close")

        self._btn_refresh.clicked.connect(self._request_refresh)
        self._btn_close.clicked.connect(self.close)
        self._btn_pin.clicked.connect(self._toggle_pin)
        # Order matters — close must be the rightmost button, matching every
        # other Windows title bar the user has ever seen.
        for b in (self._btn_refresh, self._btn_pin, self._btn_close):
            tb.addWidget(b)
        outer.addWidget(self._title_bar)

        # Theme strip moved to popup menu

        # Content
        self._content = QWidget()
        self._content.setObjectName("Content")
        self._main_stack = QStackedLayout(self._content)
        
        self._cards_page = QWidget()
        self._content_layout = QVBoxLayout(self._cards_page)
        self._content_layout.setContentsMargins(8, 8, 8, 8)
        self._content_layout.setSpacing(6)

        # First-run tip banner — reveals the non-obvious affordances the
        # MS Store reviewer (and regular users) would otherwise miss:
        # drag-anywhere, double-click compact, right-click menu. Persists
        # the dismissal in settings.json so returning users don't see it.
        if not self._settings.get("tip_dismissed"):
            self._tip_banner = self._build_tip_banner()
            self._content_layout.addWidget(self._tip_banner)
        else:
            self._tip_banner = None

        self._status_lbl = QLabel("Connecting...")
        self._content_layout.addWidget(self._status_lbl)
        self._cards_container = QWidget()
        self._cards_layout = QVBoxLayout(self._cards_container)
        self._cards_layout.setContentsMargins(0, 0, 0, 0)
        self._cards_layout.setSpacing(8)
        self._content_layout.addWidget(self._cards_container)
        self._content_layout.addStretch()
        
        self._main_stack.addWidget(self._cards_page)
        
        # Focus Page
        theme = themes.THEMES.get(self._settings.get("theme", "sunset-neon"), {})
        self._focus_widget = FocusTimerWidget(theme)
        self._focus_widget.finished.connect(self._exit_focus_mode)
        self._main_stack.addWidget(self._focus_widget)

        outer.addWidget(self._content, stretch=1)
        
        hi = self._settings.get("snake_high_score", 0)
        self._game_overlay = SnakeOverlay(theme, hi, self)
        self._game_overlay.finished.connect(lambda: self.setFocus())
        self._game_overlay.highScoreReached.connect(self._save_snake_highscore)

        # Tool strip — between cards and footer
        self._tool_strip = QWidget()
        self._tool_strip.setObjectName("ToolStrip")
        self._tool_strip.setFixedHeight(28)
        tstrip = QHBoxLayout(self._tool_strip)
        tstrip.setContentsMargins(10, 0, 10, 0)
        tstrip.setSpacing(4)
        tstrip.addStretch()
        self._btn_theme = QPushButton("\ud83c\udfa8")
        self._btn_settings = QPushButton("⚙")
        self._btn_graph = QPushButton("\ud83d\udcca")
        self._btn_compact = QPushButton("↕")
        self._btn_focus = QPushButton("⏳")
        self._btn_snake = QPushButton("\ud83d\udc0d")
        for b in (self._btn_theme, self._btn_settings, self._btn_graph, self._btn_compact, self._btn_focus, self._btn_snake):
            b.setFlat(True)
            b.setCursor(Qt.PointingHandCursor)
            b.setFixedHeight(24)
            tstrip.addWidget(b)
        tstrip.addStretch()
        self._btn_theme.setToolTip("Themes")
        self._btn_settings.setToolTip("Settings")
        self._btn_graph.setToolTip("Cycle graph view: Classic / Horizon")
        self._btn_compact.setToolTip("Compact Mode")
        self._btn_focus.setToolTip("Cooldown Timer")
        self._btn_snake.setToolTip("Play Cooldown Snake")
        # Accessible names — screen readers and MS Store review tooling look at
        # these. The emoji glyphs on their own read as "picture" or literal
        # unicode code points, which is what MS Store graded as "poor
        # navigation" in the v2.0.1 rejection. Same lesson applies here.
        self._btn_theme.setAccessibleName("Themes")
        self._btn_settings.setAccessibleName("Settings")
        self._btn_graph.setAccessibleName("Cycle graph view")
        self._btn_compact.setAccessibleName("Toggle compact mode")
        self._btn_focus.setAccessibleName("Cooldown timer")
        self._btn_snake.setAccessibleName("Play cooldown snake game")
        self._btn_theme.clicked.connect(self._show_theme_menu)
        self._btn_settings.clicked.connect(self._open_settings_dialog)
        self._btn_graph.clicked.connect(self._cycle_graph_view)
        self._btn_compact.clicked.connect(self._toggle_compact)
        self._btn_focus.clicked.connect(self._toggle_focus_mode)
        self._btn_snake.clicked.connect(self._game_overlay.start_game)
        outer.addWidget(self._tool_strip)

        # Footer
        self._footer = QWidget()
        self._footer.setObjectName("Footer")
        self._footer.setFixedHeight(24)
        ft = QHBoxLayout(self._footer)
        ft.setContentsMargins(10, 0, 10, 0)
        
        self._footer_lbl = QLabel("")
        ft.addWidget(self._footer_lbl)
        ft.addStretch()
        
        sonnet = QPushButton("Use Sonnet")
        sonnet.setFlat(True)
        sonnet.setCursor(Qt.PointingHandCursor)
        sonnet.clicked.connect(
            lambda: webbrowser.open("https://claude.ai/new?model=claude-sonnet-4-6")
        )
        ft.addWidget(sonnet)
        outer.addWidget(self._footer)

        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._show_context_menu)

        # Keyboard shortcuts — surfaced in tooltips + the Help tab.
        for seq, slot in (
            ("Ctrl+R", self._request_refresh),
            ("Ctrl+,", lambda: self._open_settings_dialog()),
            ("Ctrl+D", self._toggle_compact),
            ("Ctrl+P", self._toggle_focus_mode),
            ("Ctrl+H", lambda: self._open_settings_dialog(initial_tab=2)),
        ):
            sc = QShortcut(QKeySequence(seq), self)
            sc.activated.connect(slot)

        # Install drag event filter on every non-interactive descendant so the
        # user can drag from anywhere (cards, labels, bars, empty strip space),
        # not just the 1-px gaps between child widgets.
        self._install_drag_filter(self)

    # ── preview (no-credentials onboarding) ────────────────────────

    def _render_preview(self) -> None:
        """Render a demo tier card with mock data so users see what the
        widget will look like once they paste their sessionKey."""
        from datetime import datetime, timedelta, timezone

        self._preview_hint = QLabel(
            "\U0001F441 <b>Preview.</b> Click <b>\u2699 Settings</b> "
            "and paste your <code>sessionKey</code> cookie from claude.ai "
            "to see your real usage."
        )
        self._preview_hint.setTextFormat(Qt.RichText)
        self._preview_hint.setWordWrap(True)
        self._preview_hint.setStyleSheet(
            f"color: {self._theme['accent']}; font-size: 9pt; "
            f"padding: 8px 4px;"
        )
        self._content_layout.insertWidget(
            max(0, self._content_layout.indexOf(self._status_lbl)),
            self._preview_hint,
        )

        demo = TierCard(
            tier_key="five_hour",
            label="Session (5hr) \u00B7 preview",
            theme=self._theme,
        )
        self._preview_card = demo
        # Mock history: ramp up over 2 hours to ~68%, current util 68%.
        mock_history = [28, 32, 35, 40, 43, 47, 51, 54, 58, 61, 64, 68]
        mock_reset = (
            datetime.now(timezone.utc) + timedelta(hours=2, minutes=27)
        ).isoformat().replace("+00:00", "Z")
        demo.update_state(util=68, resets_at=mock_reset, history_values=mock_history)
        self._cards_layout.addWidget(demo)
        self._install_drag_filter(demo)

    def _clear_preview(self) -> None:
        """Tear down preview state — called once real data (or credentials)
        arrives."""
        card = getattr(self, "_preview_card", None)
        if card is not None:
            self._cards_layout.removeWidget(card)
            card.setParent(None)
            card.deleteLater()
            self._preview_card = None
        hint = getattr(self, "_preview_hint", None)
        if hint is not None:
            self._content_layout.removeWidget(hint)
            hint.setParent(None)
            hint.deleteLater()
            self._preview_hint = None

    def _build_tip_banner(self) -> QWidget:
        """One-time tip row surfaced on first launch; dismiss → persisted."""
        banner = QWidget()
        banner.setObjectName("TipBanner")
        lay = QHBoxLayout(banner)
        lay.setContentsMargins(10, 6, 6, 6)
        lay.setSpacing(8)
        msg = QLabel(
            "\U0001F4A1  Drag anywhere to move  \u00B7  double-click title "
            "for compact  \u00B7  right-click for menu"
        )
        msg.setWordWrap(True)
        lay.addWidget(msg, stretch=1)
        dismiss = QPushButton("\u00D7")
        dismiss.setFlat(True)
        dismiss.setCursor(Qt.PointingHandCursor)
        dismiss.setFixedSize(22, 22)
        dismiss.setToolTip("Dismiss")
        dismiss.setAccessibleName("Dismiss tip")
        dismiss.clicked.connect(self._dismiss_tip_banner)
        lay.addWidget(dismiss)
        return banner

    def _dismiss_tip_banner(self) -> None:
        if self._tip_banner is None:
            return
        self._tip_banner.hide()
        self._content_layout.removeWidget(self._tip_banner)
        self._tip_banner.deleteLater()
        self._tip_banner = None
        self._settings["tip_dismissed"] = True
        self._save_settings()

    def _install_drag_filter(self, root: QWidget) -> None:
        """Recursively install drag filter on descendants except QPushButton/QLineEdit."""
        for child in root.findChildren(QWidget):
            if isinstance(child, (QPushButton, QLineEdit)):
                continue
            child.installEventFilter(self)

    def eventFilter(self, obj, event) -> bool:  # noqa: N802 (Qt API)
        et = event.type()
        if et == QEvent.MouseButtonPress:
            if event.button() == Qt.LeftButton:
                self._drag_origin = (
                    event.globalPosition().toPoint() - self.frameGeometry().topLeft()
                )
                return True
        elif et == QEvent.MouseMove:
            if self._drag_origin and (event.buttons() & Qt.LeftButton):
                self.move(event.globalPosition().toPoint() - self._drag_origin)
                return True
        elif et == QEvent.MouseButtonRelease:
            if self._drag_origin is not None:
                self._drag_origin = None
                self._save_window_geometry()
                return True
        elif et == QEvent.MouseButtonDblClick:
            if event.button() == Qt.LeftButton:
                self._toggle_compact()
                return True
        return super().eventFilter(obj, event)

    # -- theme -----------------------------------------------------

    def apply_theme(self, key: str) -> None:
        self._theme_key = key
        self._theme = themes.THEMES[key]

        t = self._theme
        # Themes that opt out of Mica need a solid background on the root widget
        # so the window renders opaque (and captures mouse events across its whole area,
        # so drag works on "empty" regions too).
        opts_out = t.get("opts_out_of_mica", False)
        if opts_out:
            root_bg = f"background-color: {t['bg']};"
        else:
            root_bg = ""

        # Chrome strips (title bar, theme strip, footer) need denser glass than
        # the cards — otherwise their light text disappears against Mica bleeding
        # through from a light desktop wallpaper. Use a slightly denser alpha than
        # cards so the chrome feels more solid than the content area.
        c = QColor(t.get("glass_on_mica", t["glass"]))
        chrome_alpha = 1.0 if opts_out else 0.92
        chrome_bg = f"rgba({c.red()},{c.green()},{c.blue()},{chrome_alpha:.3f})"

        self.setStyleSheet(
            f"""
            SanduhrWidget {{
                {root_bg}
            }}
            QWidget {{
                color: {t['text']};
                font-family: "Segoe UI Variable Display", "Segoe UI", sans-serif;
                font-size: 10pt;
            }}
            QWidget#TitleBar, QWidget#Footer, QWidget#ToolStrip {{
                background-color: {chrome_bg};
            }}
            QWidget#Content {{
                background-color: {chrome_bg};
            }}
            QWidget#TipBanner {{
                background-color: rgba({QColor(t['accent']).red()},{QColor(t['accent']).green()},{QColor(t['accent']).blue()},0.14);
                border: 1px solid rgba({QColor(t['accent']).red()},{QColor(t['accent']).green()},{QColor(t['accent']).blue()},0.35);
                border-radius: 6px;
            }}
            QWidget#TipBanner QLabel {{
                color: {t['text']};
                font-size: 9pt;
            }}
            QPushButton {{
                color: {t['text']};
                background: transparent;
                border: none;
                padding: 0 8px;
            }}
            QPushButton:hover {{
                background-color: rgba({c.red()},{c.green()},{c.blue()},0.45);
            }}
            /* Windows-native close button: larger glyph, red hover, red-darker
               pressed state. Matches Win11 Explorer / Settings title bars. */
            QPushButton#CloseButton {{
                font-size: 12pt;
                font-weight: 400;
            }}
            QPushButton#CloseButton:hover {{
                background-color: #c42b1c;
                color: #ffffff;
            }}
            QPushButton#CloseButton:pressed {{
                background-color: #b2271a;
                color: #ffffff;
            }}
            /* Dialog chrome. MS Store review caught these rendering as light
               theme text on Windows' default light background when the host
               system was set to light mode — because the root stylesheet
               scoped its background to `SanduhrWidget` only, dialogs fell
               through to the system palette. Explicit dialog backgrounds
               here bind the active theme to every QDialog / QMessageBox
               regardless of what the system is doing. Mica doesn't apply
               to dialogs (they're separate top-level windows), so always
               use solid `t['bg']`. */
            QDialog, QMessageBox {{
                background-color: {t['bg']};
                color: {t['text']};
            }}
            QMessageBox QLabel, QDialog QLabel {{
                background-color: transparent;
                color: {t['text']};
            }}
            QTabWidget::pane {{
                background-color: {t['glass']};
                border: 1px solid {t['border']};
                border-radius: 4px;
                top: -1px;
            }}
            QTabWidget {{
                background-color: {t['bg']};
            }}
            QTabBar::tab {{
                background-color: {t['bg']};
                color: {t['text_secondary']};
                border: 1px solid {t['border']};
                padding: 6px 14px;
                margin-right: 2px;
                border-top-left-radius: 4px;
                border-top-right-radius: 4px;
            }}
            QTabBar::tab:selected {{
                background-color: {t['glass']};
                color: {t['text']};
                border-bottom-color: {t['glass']};
            }}
            QTabBar::tab:hover:!selected {{
                color: {t['text']};
            }}
            QLineEdit, QTextEdit, QPlainTextEdit {{
                background-color: {t['bg']};
                color: {t['text']};
                border: 1px solid {t['border']};
                padding: 4px 6px;
                border-radius: 4px;
                selection-background-color: {t['accent']};
                selection-color: {t['bg']};
            }}
            QLineEdit:focus, QTextEdit:focus, QPlainTextEdit:focus {{
                border-color: {t['accent']};
            }}
            QListWidget, QListView {{
                background-color: {t['bg']};
                color: {t['text']};
                border: 1px solid {t['border']};
                border-radius: 4px;
            }}
            QListWidget::item:selected, QListView::item:selected {{
                background-color: {t['accent']};
                color: {t['bg']};
            }}
            QDialogButtonBox QPushButton {{
                color: {t['text']};
                background-color: {t['glass']};
                border: 1px solid {t['border']};
                border-radius: 4px;
                padding: 6px 14px;
                min-width: 72px;
            }}
            QDialogButtonBox QPushButton:hover {{
                border-color: {t['accent']};
                background-color: {chrome_bg};
            }}
            QDialogButtonBox QPushButton:default {{
                border-color: {t['accent']};
            }}
            QScrollArea {{
                background-color: transparent;
                border: none;
            }}
            QScrollBar:vertical {{
                background-color: {t['bg']};
                width: 10px;
                border: none;
            }}
            QScrollBar::handle:vertical {{
                background-color: {t['border']};
                min-height: 20px;
                border-radius: 5px;
            }}
            QScrollBar::handle:vertical:hover {{
                background-color: {t['text_muted']};
            }}
            QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical,
            QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {{
                background: transparent;
                border: none;
                height: 0;
            }}
            """
        )

        self._accent_strip.setStyleSheet(
            f"background: qlineargradient(x1:0, y1:0, x2:1, y2:0, "
            f"stop:0 {self._theme['accent']}, "
            f"stop:1 rgba(0,0,0,0));"
        )

        for card in self._tier_cards.values():
            card.apply_theme(self._theme)
            self._apply_monospace_if_needed(card)
            
        if hasattr(self, '_focus_widget'):
            self._focus_widget.apply_theme(self._theme)
            
        if hasattr(self, '_game_overlay'):
            self._game_overlay.apply_theme(self._theme)

        if self._theme.get("opts_out_of_mica"):
            mica.disable_mica(self)
        else:
            mica.apply_mica(self, enabled=True)

        self._settings["theme"] = key
        self._save_settings()

        # (Theme buttons highlighting logic removed since they are now in a QMenu)

    def _apply_monospace_if_needed(self, card: TierCard) -> None:
        """Matrix-only: swap percentage / countdown fonts to Cascadia Code."""
        from PySide6.QtGui import QFont
        mono = self._theme.get("monospace_font")
        if mono:
            fb = self._theme.get("monospace_fallback", "Consolas")
            family = mono if QFont(mono).exactMatch() else fb
            f = QFont(family, 14)
            f.setStyleHint(QFont.Monospace)
            f.setBold(True)
            card._pct.setFont(f)
            from PySide6.QtGui import QColor as _QColor
            from PySide6.QtWidgets import QGraphicsDropShadowEffect as _Shadow

            bloom = self._theme.get("accent_bloom", {"blur": 4, "alpha": 0.6})
            effect = _Shadow(card._pct)
            effect.setOffset(0, 0)
            effect.setBlurRadius(bloom["blur"])
            c = _QColor(self._theme["accent"])
            c.setAlphaF(bloom["alpha"])
            effect.setColor(c)
            card._pct.setGraphicsEffect(effect)
        else:
            from PySide6.QtGui import QFont
            default = QFont("Segoe UI Variable Display", 14)
            default.setBold(True)
            card._pct.setFont(default)
            card._pct.setGraphicsEffect(None)

    # -- drag ------------------------------------------------------

    def mousePressEvent(self, event) -> None:  # noqa: N802
        if event.button() == Qt.LeftButton:
            self._drag_origin = (
                event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            )

    def mouseMoveEvent(self, event) -> None:  # noqa: N802
        if self._drag_origin and event.buttons() & Qt.LeftButton:
            self.move(event.globalPosition().toPoint() - self._drag_origin)

    def mouseReleaseEvent(self, event) -> None:  # noqa: N802
        self._drag_origin = None
        self._save_window_geometry()

    def mouseDoubleClickEvent(self, event) -> None:  # noqa: N802
        if (
            event.button() == Qt.LeftButton
            and event.position().y() <= self._title_bar.height() + 2
        ):
            self._toggle_compact()

    # -- context menu ----------------------------------------------

    def _save_snake_highscore(self, score: int) -> None:
        self._settings["snake_high_score"] = score
        self._save_settings()

    def _show_context_menu(self, pos: QPoint) -> None:
        menu = QMenu(self)
        for k, a in [
            ("Refresh", self._request_refresh),
            (
                "Expand" if self._compact else "Compact Mode",
                self._toggle_compact,
            ),
            ("Deep Work Mode (Ctrl+P)", self._toggle_focus_mode),
            ("Play Cooldown Snake", self._game_overlay.start_game),
            ("Settings…", self._open_settings_dialog),
            ("Quit Sanduhr", QGuiApplication.quit),
        ]:
            menu.addAction(k, a)
        menu.popup(self.mapToGlobal(pos))

    # -- actions ---------------------------------------------------

    def _toggle_pin(self) -> None:
        """Toggle always-on-top.

        Every ctypes-only approach fails because Qt re-enforces the
        WindowStaysOnTopHint flag on focus/paint events — it remembers
        its own flag state and re-applies WS_EX_TOPMOST. The only
        reliable path is to actually change Qt's flag (setWindowFlag)
        and then call show() to re-show the widget (setWindowFlag hides
        as a side effect of recreating the native handle).

        Cost: HWND gets recreated, which drops Mica + taskbar extended
        style. We re-apply both right after show().
        """
        self._pinned = not self._pinned

        # Preserve geometry — setWindowFlag can reset it on some platforms.
        geom = self.geometry()

        self.setWindowFlag(Qt.WindowStaysOnTopHint, self._pinned)
        self.setGeometry(geom)
        self.show()

        # HWND was recreated — re-apply native-layer attributes.
        if sys.platform == "win32":
            self._taskbar_forced = False
            self._force_taskbar_button()
            self._taskbar_forced = True
        if self._theme.get("opts_out_of_mica"):
            mica.disable_mica(self)
        else:
            mica.apply_mica(self, enabled=True)

        # Button label shows the action a click will take (standard
        # Windows convention), not the current state:
        #   currently pinned  -> "Unpin" (click to unpin)
        #   currently unpinned -> "Pin"  (click to pin)
        self._btn_pin.setText("Unpin" if self._pinned else "Pin")
        self._btn_pin.setToolTip(
            "Unpin (currently always on top)" if self._pinned
            else "Pin always on top"
        )

    def _toggle_compact(self) -> None:
        self._compact = not self._compact
        self._render_cards(self._last or {})

        # Hide/show non-essential strips in compact mode
        self._tool_strip.setVisible(not self._compact)

        if self._compact:
            # Let the layout determine the minimum size needed for the
            # single card at its original dimensions, then lock to that.
            self.setMaximumHeight(16777215)
            self.setMinimumHeight(0)
            QTimer.singleShot(0, lambda: (
                self.adjustSize(),
                self.setFixedHeight(self.sizeHint().height()),
            ))
        else:
            # Unlock height so the layout can expand naturally
            self.setMaximumHeight(16777215)
            self.setMinimumHeight(0)
            QTimer.singleShot(0, lambda: self.adjustSize())

    def _cycle_graph_view(self) -> None:
        new_mode = cycle_graph_mode()
        labels = {'classic': '📊', 'horizon': '📈'}
        self._btn_graph.setText(labels.get(new_mode, '📊'))
        tips = {
            'classic': 'Classic sparkline (click to switch to Horizon chart)',
            'horizon': 'Horizon chart (click to switch to Classic sparkline)',
        }
        self._btn_graph.setToolTip(tips.get(new_mode, ''))
        if self._last:
            self._render_cards(self._last)

    def paintEvent(self, event) -> None:
        super().paintEvent(event)
        if not self._theme.get('bg_grid'):
            return
        from PySide6.QtGui import QPainter, QColor
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, False)
        grid_c = QColor(self._theme.get('border', '#333333'))
        grid_c.setAlphaF(0.12)
        w, h = self.width(), self.height()
        step = 20
        painter.setPen(grid_c)
        for x in range(0, w, step):
            painter.drawLine(x, 0, x, h)
        for y in range(0, h, step):
            painter.drawLine(0, y, w, y)
        major_c = QColor(self._theme.get('border', '#333333'))
        major_c.setAlphaF(0.22)
        painter.setPen(major_c)
        for x in range(0, w, step * 4):
            painter.drawLine(x, 0, x, h)
        for y in range(0, h, step * 4):
            painter.drawLine(0, y, w, y)
        painter.end()

    def _toggle_focus_mode(self) -> None:
        if self._main_stack.currentIndex() == 1:
            self._exit_focus_mode()
        else:
            dur = self._settings.get("focus_mode_duration", 25)
            self._main_stack.setCurrentIndex(1)
            self._focus_widget.start(dur)

    def _exit_focus_mode(self) -> None:
        self._focus_widget.stop()
        self._main_stack.setCurrentIndex(0)

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        if hasattr(self, '_game_overlay'):
            self._game_overlay.setGeometry(self.rect())

    def _open_settings_dialog(self, focus_cf: bool = False, initial_tab: int = 0) -> None:
        creds = credentials.load()
        dlg = SettingsDialog(
            self,
            session_key=creds.get("session_key") or "",
            cf_clearance=creds.get("cf_clearance") or "",
            focus_cf=focus_cf,
            initial_tab=initial_tab,
            settings=self._settings,
        )
        dlg.credentialsSaved.connect(self._on_credentials_saved)
        dlg.credentialsCleared.connect(self._on_credentials_cleared)
        dlg.themesChanged.connect(self._rebuild_theme_strip)
        dlg.settingsSaved.connect(self._on_settings_saved)
        dlg.setStyleSheet(self.styleSheet())
        dlg.exec_()

    def _on_credentials_saved(self, session_key: str, cf_clearance) -> None:
        self._clear_preview()
        self._start_or_update_fetcher(session_key, cf_clearance)
        self._request_refresh()

    def _on_credentials_cleared(self) -> None:
        """User signed out via blank-sessionKey save in the Settings dialog.
        Point the fetcher at empty credentials (it'll 401 on next poll,
        harmless), tear down any tier cards so stale data doesn't linger,
        and tell the user how to resume."""
        if self._fetcher is not None:
            self._fetcher.update_credentials("", None)
        for tier_key in list(self._tier_cards.keys()):
            card = self._tier_cards.pop(tier_key)
            self._cards_layout.removeWidget(card)
            card.setParent(None)
            card.deleteLater()
        self._status_lbl.setText(
            "Signed out — paste sessionKey in Settings to resume."
        )

    def _on_settings_saved(self, new_settings: dict) -> None:
        self._settings.update(new_settings)
        self._save_settings()

    def _prompt_first_run(self) -> None:
        # Build the welcome dialog with our stylesheet pre-applied so it
        # doesn't flash white before Qt's style cascade lands.
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Information)
        box.setWindowTitle("Sanduhr für Claude")
        box.setText(
            "Welcome!\n\n"
            "1. Open claude.ai and log in.\n"
            "2. Press F12 -> Application -> Cookies -> claude.ai.\n"
            "3. Copy the sessionKey cookie value.\n\n"
            "Paste it in the next dialog."
        )
        box.setStyleSheet(self.styleSheet())
        box.exec_()
        self._open_settings_dialog()

    def _rebuild_theme_strip(self) -> None:
        """Themes change handles dynamically via QMenu now, so just re-apply current."""
        self.apply_theme(self._theme_key)

    def _show_theme_menu(self) -> None:
        from PySide6.QtWidgets import QMenu
        menu = QMenu(self)
        menu.setStyleSheet(self.styleSheet())
        for key, theme in themes.THEMES.items():
            action = menu.addAction(theme["name"])
            if key == self._theme_key:
                action.setCheckable(True)
                action.setChecked(True)
            action.triggered.connect(lambda _=False, k=key: self.apply_theme(k))
        menu.exec_(self._btn_theme.mapToGlobal(self._btn_theme.rect().bottomLeft()))

    # -- fetcher ---------------------------------------------------

    def _start_fetcher(self, session_key: str, cf_clearance: Optional[str]) -> None:
        if self._thread is not None:
            return
        self._thread = QThread(self)
        self._fetcher = UsageFetcher(session_key, cf_clearance)
        self._fetcher.moveToThread(self._thread)
        self._fetcher.dataReady.connect(self._on_data_ready)
        self._fetcher.fetchFailed.connect(self._on_fetch_failed)
        self._thread.start()
        QTimer.singleShot(0, self._fetcher.fetch)
        self._schedule = QTimer(self)
        self._schedule.timeout.connect(
            lambda: QTimer.singleShot(0, self._fetcher.fetch)
        )
        self._schedule.start(_REFRESH_MS)

    def _start_or_update_fetcher(self, sk: str, cf: Optional[str]) -> None:
        if self._fetcher is None:
            self._start_fetcher(sk, cf)
        else:
            self._fetcher.update_credentials(sk, cf)

    def _request_refresh(self) -> None:
        if self._fetcher:
            self._status_lbl.setText("Refreshing...")
            QTimer.singleShot(0, self._fetcher.fetch)

    @Slot(dict)
    def _on_data_ready(self, data: dict) -> None:
        self._clear_preview()
        self._status_lbl.setText("")
        self._last = data
        self._render_cards(data)
        ts = datetime.now().strftime("%I:%M %p").lstrip("0")
        mode = "Compact" if self._compact else ("Pinned" if self._pinned else "Float")
        self._footer_lbl.setText(f"Updated {ts} | {mode}")

    @Slot(str, str)
    def _on_fetch_failed(self, kind: str, message: str) -> None:
        if kind == "session_expired":
            self._status_lbl.setText("Session expired -- click Key.")
        elif kind == "cloudflare":
            self._status_lbl.setText("Cloudflare -- add cf_clearance (click Key).")
        elif kind == "network":
            self._status_lbl.setText("No connection -- retrying...")
        else:
            self._status_lbl.setText(f"Error: {message[:60]}")
        self._status_lbl.setStyleSheet("color: #f87171;")

    def _render_cards(self, data: dict) -> None:
        active = []
        for key, label in _TIER_LABELS.items():
            tier = data.get(key)
            if tier and tier.get("utilization") is not None:
                active.append(
                    (key, label, int(tier["utilization"]), tier.get("resets_at"))
                )

        if self._compact and active:
            active = [max(active, key=lambda t: t[2])]

        stale = set(self._tier_cards) - {a[0] for a in active}
        for k in stale:
            card = self._tier_cards.pop(k)
            self._cards_layout.removeWidget(card)
            card.setParent(None)
            card.deleteLater()

        for key, label, util, resets_at in active:
            if key in self._tier_cards:
                card = self._tier_cards[key]
            else:
                card = TierCard(tier_key=key, label=label, theme=self._theme)
                self._tier_cards[key] = card
                self._cards_layout.addWidget(card)
                self._apply_monospace_if_needed(card)
                # New card + its descendants need the drag filter too
                self._install_drag_filter(card)
            card.update_state(
                util=util,
                resets_at=resets_at,
                history_values=history.load(key),
            )

    def _tick(self) -> None:
        """30s countdown tick -- refresh reset labels + pace markers only."""
        data = self._last
        if not data:
            return
        for key, card in self._tier_cards.items():
            tier = data.get(key, {})
            util = tier.get("utilization")
            ra = tier.get("resets_at")
            if util is not None:
                card.update_state(
                    util=int(util), resets_at=ra, history_values=history.load(key)
                )

    # -- geometry persistence --------------------------------------

    def _restore_geometry(self) -> None:
        geom = self._settings.get("window")
        if geom and all(k in geom for k in ("x", "y", "w", "h")):
            self.move(geom["x"], geom["y"])
            self.resize(geom["w"], geom["h"])
        else:
            screen = QGuiApplication.primaryScreen().availableGeometry()
            self.move(
                screen.right() - self.width() - 24,
                screen.bottom() - self.height() - 24,
            )

    def _save_window_geometry(self) -> None:
        self._settings["window"] = {
            "x": self.x(),
            "y": self.y(),
            "w": self.width(),
            "h": self.height(),
        }
        self._save_settings()

    def showEvent(self, event) -> None:  # noqa: N802
        super().showEvent(event)
        # Force a taskbar button once the native HWND exists. Qt's
        # FramelessWindowHint + WindowStaysOnTopHint combo on Win11 can
        # omit WS_EX_APPWINDOW from the extended style, which means no
        # taskbar icon — if the user minimizes there's nowhere to click
        # to restore. Forcing the flag here guarantees a taskbar button.
        if sys.platform == "win32" and not getattr(self, "_taskbar_forced", False):
            self._force_taskbar_button()
            self._taskbar_forced = True

    def _force_taskbar_button(self) -> None:
        """Set WS_EX_APPWINDOW on the native window so Windows shows a
        taskbar button for this frameless widget."""
        import ctypes
        GWL_EXSTYLE = -20
        WS_EX_APPWINDOW = 0x00040000
        WS_EX_TOOLWINDOW = 0x00000080
        SWP_NOMOVE = 0x0002
        SWP_NOSIZE = 0x0001
        SWP_NOZORDER = 0x0004
        SWP_FRAMECHANGED = 0x0020
        hwnd = int(self.winId())
        user32 = ctypes.windll.user32
        current = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        new = (current | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW
        if new != current:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, new)
            user32.SetWindowPos(
                hwnd, 0, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED,
            )

    def closeEvent(self, event) -> None:  # noqa: N802
        self._save_window_geometry()
        if self._thread is not None:
            self._thread.quit()
            self._thread.wait(2000)
        super().closeEvent(event)

    # -- settings --------------------------------------------------

    def _load_settings(self) -> dict:
        path = paths.settings_file()
        if not path.exists():
            return {}
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {}

    def _save_settings(self) -> None:
        try:
            paths.settings_file().write_text(
                json.dumps(self._settings), encoding="utf-8"
            )
        except OSError as e:
            _log.warning("Could not save settings: %s", e)

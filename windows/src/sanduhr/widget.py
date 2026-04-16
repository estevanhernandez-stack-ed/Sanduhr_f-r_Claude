"""SanduhrWidget -- frameless always-on-top main window.

Composes title bar, theme strip, tier cards, footer. Manages drag,
compact mode, pin toggle, right-click context menu, window position
persistence, credentials dialog, and refresh scheduling.
"""

import json
import logging
import webbrowser
from datetime import datetime
from typing import Dict, Optional

from PySide6.QtCore import QPoint, Qt, QThread, QTimer, Slot
from PySide6.QtGui import QGuiApplication
from PySide6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMenu,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from sanduhr import credentials, history, mica, paths, themes
from sanduhr.fetcher import UsageFetcher
from sanduhr.tiers import TierCard

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


class CredentialsDialog(QDialog):
    def __init__(
        self,
        parent=None,
        session_key: str = "",
        cf_clearance: str = "",
        focus_cf: bool = False,
    ):
        super().__init__(parent)
        self.setWindowTitle("Credentials")
        self.setModal(True)

        layout = QVBoxLayout(self)
        layout.addWidget(
            QLabel(
                "Paste your claude.ai cookies.\n"
                "F12 -> Application -> Cookies -> claude.ai"
            )
        )

        layout.addWidget(QLabel("sessionKey"))
        self._sk = QLineEdit(session_key)
        self._sk.setEchoMode(QLineEdit.Password)
        layout.addWidget(self._sk)

        layout.addWidget(QLabel("cf_clearance (optional)"))
        self._cf = QLineEdit(cf_clearance)
        self._cf.setEchoMode(QLineEdit.Password)
        layout.addWidget(self._cf)

        btns = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        layout.addWidget(btns)

        (self._cf if focus_cf else self._sk).setFocus()

    def values(self) -> Dict[str, str]:
        return {
            "session_key": self._sk.text().strip(),
            "cf_clearance": self._cf.text().strip(),
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
        self._accent_strip.setFixedHeight(2)
        outer.addWidget(self._accent_strip)

        # Title bar
        self._title_bar = QWidget()
        self._title_bar.setFixedHeight(34)
        tb = QHBoxLayout(self._title_bar)
        tb.setContentsMargins(10, 0, 0, 0)
        tb.setSpacing(0)
        self._title_lbl = QLabel("Sanduhr für Claude")
        tb.addWidget(self._title_lbl)
        tb.addStretch()
        self._btn_key = QPushButton("Key")
        self._btn_refresh = QPushButton("Refresh")
        self._btn_close = QPushButton("x")
        self._btn_pin = QPushButton("Pin")
        for b in (self._btn_key, self._btn_refresh, self._btn_close, self._btn_pin):
            b.setFlat(True)
            b.setCursor(Qt.PointingHandCursor)
            b.setFixedHeight(34)
        self._btn_key.clicked.connect(lambda: self._open_credentials_dialog())
        self._btn_refresh.clicked.connect(self._request_refresh)
        self._btn_close.clicked.connect(self.close)
        self._btn_pin.clicked.connect(self._toggle_pin)
        for b in (self._btn_key, self._btn_refresh, self._btn_close, self._btn_pin):
            tb.addWidget(b)
        outer.addWidget(self._title_bar)

        # Theme strip
        self._theme_strip = QWidget()
        self._theme_strip.setFixedHeight(26)
        ts = QHBoxLayout(self._theme_strip)
        ts.setContentsMargins(10, 0, 10, 0)
        ts.setSpacing(0)
        self._theme_buttons: Dict[str, QPushButton] = {}
        for key, theme in themes.THEMES.items():
            btn = QPushButton(theme["name"])
            btn.setFlat(True)
            btn.setCursor(Qt.PointingHandCursor)
            btn.clicked.connect(lambda _=False, k=key: self.apply_theme(k))
            self._theme_buttons[key] = btn
            ts.addWidget(btn)
        ts.addStretch()
        outer.addWidget(self._theme_strip)

        # Content
        self._content = QWidget()
        self._content_layout = QVBoxLayout(self._content)
        self._content_layout.setContentsMargins(14, 10, 14, 10)
        self._content_layout.setSpacing(8)
        self._status_lbl = QLabel("Connecting...")
        self._content_layout.addWidget(self._status_lbl)
        self._cards_container = QWidget()
        self._cards_layout = QVBoxLayout(self._cards_container)
        self._cards_layout.setContentsMargins(0, 0, 0, 0)
        self._cards_layout.setSpacing(8)
        self._content_layout.addWidget(self._cards_container)
        self._content_layout.addStretch()
        outer.addWidget(self._content, stretch=1)

        # Footer
        self._footer = QWidget()
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

    # -- theme -----------------------------------------------------

    def apply_theme(self, key: str) -> None:
        self._theme_key = key
        self._theme = themes.THEMES[key]

        # Themes that opt out of Mica need a solid background on the root widget
        # so the window renders opaque (and captures mouse events across its whole area,
        # so drag works on "empty" regions too).
        if self._theme.get("opts_out_of_mica"):
            root_bg = f"background-color: {self._theme['bg']};"
        else:
            root_bg = ""

        self.setStyleSheet(
            f"""
            SanduhrWidget {{
                {root_bg}
            }}
            QWidget {{
                color: {self._theme['text']};
                font-family: "Segoe UI Variable Display", "Segoe UI", sans-serif;
                font-size: 10pt;
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

        if self._theme.get("opts_out_of_mica"):
            mica.disable_mica(self)
        else:
            mica.apply_mica(self, enabled=True)

        self._settings["theme"] = key
        self._save_settings()

        for k, btn in self._theme_buttons.items():
            if k == key:
                btn.setStyleSheet(
                    f"color: {self._theme['accent']}; font-weight: 600;"
                )
            else:
                btn.setStyleSheet(f"color: {self._theme['text_muted']};")

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

    def _show_context_menu(self, pos: QPoint) -> None:
        menu = QMenu(self)
        menu.addAction("Refresh", self._request_refresh)
        menu.addAction(
            "Compact mode" if not self._compact else "Expand",
            self._toggle_compact,
        )
        menu.addAction("Credentials...", lambda: self._open_credentials_dialog())
        menu.addSeparator()
        menu.addAction("Quit", self.close)
        menu.popup(self.mapToGlobal(pos))

    # -- actions ---------------------------------------------------

    def _toggle_pin(self) -> None:
        self._pinned = not self._pinned
        flags = self.windowFlags()
        if self._pinned:
            flags |= Qt.WindowStaysOnTopHint
        else:
            flags &= ~Qt.WindowStaysOnTopHint
        self.setWindowFlags(flags)
        self.show()

    def _toggle_compact(self) -> None:
        self._compact = not self._compact
        self._render_cards(self._last or {})

    def _open_credentials_dialog(self, focus_cf: bool = False) -> None:
        creds = credentials.load()
        dlg = CredentialsDialog(
            self,
            session_key=creds.get("session_key") or "",
            cf_clearance=creds.get("cf_clearance") or "",
            focus_cf=focus_cf,
        )
        if dlg.exec_() == QDialog.Accepted:
            vals = dlg.values()
            if vals["session_key"]:
                credentials.save(
                    session_key=vals["session_key"],
                    cf_clearance=vals["cf_clearance"] or None,
                )
                self._start_or_update_fetcher(
                    vals["session_key"], vals["cf_clearance"] or None
                )
                self._request_refresh()

    def _prompt_first_run(self) -> None:
        QMessageBox.information(
            self,
            "Sanduhr für Claude",
            "Welcome!\n\n"
            "1. Open claude.ai and log in.\n"
            "2. Press F12 -> Application -> Cookies -> claude.ai.\n"
            "3. Copy the sessionKey cookie value.\n\n"
            "Paste it in the next dialog.",
        )
        self._open_credentials_dialog()

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

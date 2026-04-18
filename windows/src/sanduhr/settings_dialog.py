"""SettingsDialog — consolidated user-facing settings.

Two tabs:
  - Credentials: sessionKey + cf_clearance (keyring-backed)
  - Themes: paste JSON, open themes folder, copy agent prompt, reload

Replaces the older standalone CredentialsDialog. The owning widget wires
it up via `open(parent, current_creds)` and reacts to `credentialsSaved`
and `themesChanged` signals.
"""

import json
import logging
import re
from pathlib import Path
from typing import Optional

from PySide6.QtCore import QUrl, Qt, Signal
from PySide6.QtGui import QDesktopServices, QGuiApplication
from PySide6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QTabWidget,
    QVBoxLayout,
    QWidget,
    QCheckBox,
    QSpinBox,
)

from sanduhr import credentials, paths, themes

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
    """Create a QMessageBox with the parent's stylesheet already applied,
    so it doesn't flash white before Qt's style cascade catches up."""
    box = QMessageBox(parent)
    box.setIcon(icon)
    box.setWindowTitle(title)
    box.setText(text)
    if buttons is not None:
        box.setStandardButtons(buttons)
    # Inherit the root widget's stylesheet so the dark theme applies
    # from the first paint, not the second.
    root = parent.window() if parent is not None else None
    if root is not None:
        box.setStyleSheet(root.styleSheet())
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
    credentialsSaved = Signal(str, object)  # (session_key, cf_clearance | None)
    themesChanged = Signal()
    settingsSaved = Signal(dict)

    def __init__(
        self,
        parent: Optional[QWidget] = None,
        session_key: str = "",
        cf_clearance: str = "",
        initial_tab: int = 0,
        focus_cf: bool = False,
        settings: Optional[dict] = None,
    ):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setModal(True)
        self.resize(520, 520)
        
        self._settings = settings or {}

        layout = QVBoxLayout(self)
        self._tabs = QTabWidget()
        layout.addWidget(self._tabs)

        self._build_credentials_tab(session_key, cf_clearance, focus_cf)
        self._build_themes_tab()
        self._build_pacing_tab()
        self._build_help_tab()

        btns = QDialogButtonBox(QDialogButtonBox.Close)
        btns.rejected.connect(self.reject)
        btns.accepted.connect(self.accept)
        layout.addWidget(btns)

        self._tabs.setCurrentIndex(initial_tab)

    # ── Credentials tab ──────────────────────────────────────────

    def _build_credentials_tab(self, sk: str, cf: str, focus_cf: bool) -> None:
        page = QWidget()
        v = QVBoxLayout(page)

        v.addWidget(QLabel(
            "Paste your claude.ai cookies.\n"
            "F12 → Application → Cookies → claude.ai"
        ))

        v.addWidget(QLabel("sessionKey"))
        self._sk = QLineEdit(sk)
        self._sk.setEchoMode(QLineEdit.Password)
        v.addWidget(self._sk)

        v.addWidget(QLabel("cf_clearance (optional)"))
        cf_help = QLabel(
            "Only needed if Sanduhr shows <b>\u201cCloudflare blocked\u201d</b> "
            "after you save. Some accounts sit behind a Cloudflare challenge; "
            "if yours does, copy the <code>cf_clearance</code> cookie from the "
            "same DevTools panel you copied <code>sessionKey</code> from, and "
            "paste it here. Leave blank otherwise."
        )
        cf_help.setTextFormat(Qt.RichText)
        cf_help.setWordWrap(True)
        cf_help.setObjectName("HelpText")
        cf_help.setStyleSheet("font-size: 8pt;")
        v.addWidget(cf_help)
        self._cf = QLineEdit(cf)
        self._cf.setEchoMode(QLineEdit.Password)
        v.addWidget(self._cf)

        row = QHBoxLayout()
        row.addStretch()
        save = QPushButton("Save")
        save.setDefault(True)
        save.clicked.connect(self._save_credentials)
        row.addWidget(save)
        v.addLayout(row)

        v.addStretch()
        (self._cf if focus_cf else self._sk).setFocus()
        self._tabs.addTab(page, "Credentials")

    def _save_credentials(self) -> None:
        sk = self._sk.text().strip()
        cf = self._cf.text().strip() or None
        if not sk:
            _styled_msgbox(
                self, QMessageBox.Warning, "Settings",
                "sessionKey is required.",
            ).exec_()
            return
        credentials.save(session_key=sk, cf_clearance=cf)
        self.credentialsSaved.emit(sk, cf)
        _styled_msgbox(
            self, QMessageBox.Information, "Settings",
            "Credentials saved. Your widget is now fetching your usage — "
            "close this dialog to see it.",
        ).exec_()

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
        reload_btn = QPushButton("Reload themes")
        reload_btn.clicked.connect(self._reload_themes)
        list_row.addWidget(reload_btn)
        del_btn = QPushButton("Delete selected")
        del_btn.clicked.connect(self._delete_selected)
        list_row.addWidget(del_btn)
        list_row.addStretch()
        v.addLayout(list_row)

        self._refresh_list()
        self._tabs.addTab(page, "Themes")

    # ── Pacing tab ───────────────────────────────────────────────

    def _build_pacing_tab(self) -> None:
        page = QWidget()
        v = QVBoxLayout(page)

        v.addWidget(QLabel("<b>Pacing Tools & Deep Work</b>"))
        v.addWidget(QLabel(
            "Configure the advanced pacing and focus tools. These overlays appear "
            "directly on top of the widget UI when activated."
        ))
        
        v.addSpacing(16)
        
        self._chk_pacing_tools = QCheckBox("Enable Pacing Calculators")
        self._chk_pacing_tools.setChecked(self._settings.get("pacing_tools_enabled", True))
        v.addWidget(self._chk_pacing_tools)

        self._chk_auto_game = QCheckBox("Auto-trigger Wait-State Snake Game (>50% ahead)")
        self._chk_auto_game.setChecked(self._settings.get("auto_trigger_game", False))
        v.addWidget(self._chk_auto_game)

        v.addSpacing(12)
        
        row = QHBoxLayout()
        row.addWidget(QLabel("Focus Mode duration (minutes):"))
        self._spin_focus = QSpinBox()
        self._spin_focus.setRange(1, 120)
        self._spin_focus.setValue(self._settings.get("focus_mode_duration", 25))
        row.addWidget(self._spin_focus)
        row.addStretch()
        v.addLayout(row)

        v.addStretch()

        act_row = QHBoxLayout()
        act_row.addStretch()
        save_btn = QPushButton("Save Pacing Config")
        save_btn.clicked.connect(self._save_pacing_settings)
        act_row.addWidget(save_btn)
        v.addLayout(act_row)

        self._tabs.addTab(page, "Pacing")

    def _save_pacing_settings(self) -> None:
        self._settings["pacing_tools_enabled"] = self._chk_pacing_tools.isChecked()
        self._settings["auto_trigger_game"] = self._chk_auto_game.isChecked()
        self._settings["focus_mode_duration"] = self._spin_focus.value()
        self.settingsSaved.emit(self._settings)
        _styled_msgbox(
            self, QMessageBox.Information, "Settings",
            "Pacing configuration saved."
        ).exec_()

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
            QMessageBox.warning(self, "Settings", "Paste a theme JSON first.")
            return
        try:
            data = json.loads(raw)
        except json.JSONDecodeError as e:
            QMessageBox.critical(self, "Settings", f"Invalid JSON: {e}")
            return

        filename = self._filename.text().strip()
        if not filename:
            name = data.get("name", "")
            if isinstance(name, str):
                filename = _slugify(name)
        if not filename:
            QMessageBox.warning(self, "Settings", "Filename is required.")
            return
        if not filename.endswith(".json"):
            filename = f"{filename}.json"

        target = _themes_dir() / filename
        try:
            target.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except OSError as e:
            QMessageBox.critical(self, "Settings", f"Could not write theme: {e}")
            return

        themes.load_user_themes()
        self._refresh_list()
        self.themesChanged.emit()
        QMessageBox.information(
            self, "Settings", f"Saved and applied: {target.name}"
        )
        self._paste.clear()
        self._filename.clear()

    def _copy_agent_prompt(self) -> None:
        path = _agent_prompt_path()
        try:
            text = path.read_text(encoding="utf-8")
        except OSError:
            text = self._fallback_prompt()
        QGuiApplication.clipboard().setText(text)
        QMessageBox.information(
            self,
            "Settings",
            "Agent prompt copied to clipboard.\n\n"
            "Paste it into any chat agent with a reference image or vibe "
            "description. Drop the returned JSON into the paste box above.",
        )

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
        confirm = QMessageBox.question(
            self, "Settings", f"Delete {name}?"
        )
        if confirm != QMessageBox.Yes:
            return
        try:
            (_themes_dir() / name).unlink()
        except OSError as e:
            QMessageBox.critical(self, "Settings", f"Could not delete: {e}")
            return
        # Also drop from in-memory THEMES dict so the strip gets rebuilt cleanly
        key = Path(name).stem.lower()
        themes.THEMES.pop(key, None)
        self._refresh_list()
        self.themesChanged.emit()

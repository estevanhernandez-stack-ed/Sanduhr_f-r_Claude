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

    def __init__(
        self,
        parent: Optional[QWidget] = None,
        session_key: str = "",
        cf_clearance: str = "",
        initial_tab: int = 0,
        focus_cf: bool = False,
    ):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setModal(True)
        self.resize(520, 520)

        layout = QVBoxLayout(self)
        self._tabs = QTabWidget()
        layout.addWidget(self._tabs)

        self._build_credentials_tab(session_key, cf_clearance, focus_cf)
        self._build_themes_tab()

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
            QMessageBox.warning(self, "Settings", "sessionKey is required.")
            return
        credentials.save(session_key=sk, cf_clearance=cf)
        self.credentialsSaved.emit(sk, cf)
        QMessageBox.information(self, "Settings", "Credentials saved.")

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

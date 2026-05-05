"""Accounts settings tab — multi-account registry management UI.

Lets the user add additional Claude accounts (e.g. Personal + Work),
rename them, switch which one is active, and remove accounts they no
longer want tracked. Operations delegate to the sanduhr.accounts
registry; signals notify the parent dialog (and through it, the main
widget) when the active account changes so the fetcher and history
surfaces switch accordingly.
"""

import logging
from typing import Optional

from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from sanduhr import accounts

_log = logging.getLogger(__name__)


def _apply_root_style(child: QWidget, root_owner: QWidget) -> None:
    """Inherit the root widget's stylesheet so themed dialogs don't flash
    white before Qt's style cascade catches up."""
    root = root_owner.window() if root_owner else None
    if root is not None:
        child.setStyleSheet(root.styleSheet())


class _AddAccountDialog(QDialog):
    """Modal for entering a new account: name + sessionKey + cf_clearance."""

    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.setWindowTitle("Add account")
        self.setModal(True)
        self.resize(440, 280)

        v = QVBoxLayout(self)
        v.addWidget(QLabel("Account name (e.g. Personal, Work)"))
        self._name = QLineEdit()
        v.addWidget(self._name)
        name_hint = QLabel(
            "1-32 chars, letters / digits / space / underscore / hyphen."
        )
        name_hint.setObjectName("HelpText")
        name_hint.setStyleSheet("font-size: 8pt;")
        v.addWidget(name_hint)

        v.addWidget(QLabel("sessionKey"))
        self._sk = QLineEdit()
        self._sk.setEchoMode(QLineEdit.Password)
        v.addWidget(self._sk)

        v.addWidget(QLabel("cf_clearance (optional)"))
        self._cf = QLineEdit()
        self._cf.setEchoMode(QLineEdit.Password)
        v.addWidget(self._cf)

        btns = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        v.addWidget(btns)

    def get_values(self) -> tuple[str, str, str]:
        return (
            self._name.text().strip(),
            self._sk.text().strip(),
            self._cf.text().strip(),
        )


class _RenameAccountDialog(QDialog):
    """Modal for renaming an existing account."""

    def __init__(self, current_name: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.setWindowTitle(f"Rename '{current_name}'")
        self.setModal(True)
        self.resize(360, 140)

        v = QVBoxLayout(self)
        v.addWidget(QLabel("New name"))
        self._name = QLineEdit(current_name)
        v.addWidget(self._name)

        btns = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        v.addWidget(btns)

    def get_value(self) -> str:
        return self._name.text().strip()


class AccountsTab(QWidget):
    """Settings tab body for the multi-account registry."""

    # Active account changed (by Set Active, or by Remove of the active
    # account leaving another one as the new active). Args:
    # (session_key, cf_clearance | None). Empty session_key means there
    # are no accounts left.
    activeAccountChanged = Signal(str, object)

    # Any registry mutation (add, remove, rename, set-active). UI
    # surfaces that show the account list (e.g. the main widget's
    # active-account label) listen for this and re-read the registry.
    accountsChanged = Signal()

    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        v = QVBoxLayout(self)

        intro = QLabel(
            "Track multiple Claude accounts in one install. "
            "Switch which one is active to send fetches and history "
            "writes to that account. Each account's history is stored "
            "in its own file."
        )
        intro.setWordWrap(True)
        v.addWidget(intro)

        self._list = QListWidget()
        self._list.itemDoubleClicked.connect(lambda _: self._on_set_active())
        v.addWidget(self._list)

        row = QHBoxLayout()
        self._btn_add = QPushButton("Add…")
        self._btn_add.clicked.connect(self._on_add)
        self._btn_rename = QPushButton("Rename…")
        self._btn_rename.clicked.connect(self._on_rename)
        self._btn_set_active = QPushButton("Set Active")
        self._btn_set_active.clicked.connect(self._on_set_active)
        self._btn_remove = QPushButton("Remove…")
        self._btn_remove.clicked.connect(self._on_remove)
        for b in (self._btn_add, self._btn_rename, self._btn_set_active, self._btn_remove):
            row.addWidget(b)
        row.addStretch()
        v.addLayout(row)

        empty_hint = QLabel(
            "No accounts yet. Click 'Add…' to register your first one — "
            "or sign in via the Credentials tab to create the default "
            "'Personal' account automatically."
        )
        empty_hint.setObjectName("HelpText")
        empty_hint.setStyleSheet("font-size: 8pt;")
        empty_hint.setWordWrap(True)
        self._empty_hint = empty_hint
        v.addWidget(empty_hint)

        self.refresh()

    def refresh(self) -> None:
        """Re-read the registry and rebuild the list. Public so callers
        can poke it after external mutations."""
        self._list.clear()
        labels = accounts.list_accounts()
        active = accounts.get_active()
        for label in labels:
            display = f"●  {label}   (active)" if label == active else f"    {label}"
            item = QListWidgetItem(display)
            item.setData(Qt.UserRole, label)
            self._list.addItem(item)
        # Hint visibility tracks empty state.
        self._empty_hint.setVisible(not labels)
        # Buttons that need a selection are disabled when nothing's selected.
        has_any = bool(labels)
        self._btn_rename.setEnabled(has_any)
        self._btn_set_active.setEnabled(has_any)
        self._btn_remove.setEnabled(has_any)

    def _selected_label(self) -> Optional[str]:
        item = self._list.currentItem()
        if item is None:
            return None
        return item.data(Qt.UserRole)

    def _on_add(self) -> None:
        dlg = _AddAccountDialog(self)
        _apply_root_style(dlg, self)
        if dlg.exec_() != QDialog.Accepted:
            return
        name, sk, cf = dlg.get_values()
        if not name:
            self._error("Account name required.")
            return
        if not sk:
            self._error("sessionKey required.")
            return
        try:
            accounts.add_account(name, session_key=sk, cf_clearance=cf or None)
        except ValueError as e:
            self._error(str(e))
            return
        is_first_account = (len(accounts.list_accounts()) == 1)
        self.refresh()
        self.accountsChanged.emit()
        if is_first_account:
            # First-ever add becomes active automatically (per accounts.add_account
            # semantics). Notify so the fetcher starts polling.
            self.activeAccountChanged.emit(sk, cf or None)

    def _on_rename(self) -> None:
        old = self._selected_label()
        if old is None:
            return
        dlg = _RenameAccountDialog(old, self)
        _apply_root_style(dlg, self)
        if dlg.exec_() != QDialog.Accepted:
            return
        new = dlg.get_value()
        if not new or new == old:
            return
        try:
            accounts.rename_account(old, new)
        except ValueError as e:
            self._error(str(e))
            return
        self.refresh()
        self.accountsChanged.emit()

    def _on_set_active(self) -> None:
        label = self._selected_label()
        if label is None or label == accounts.get_active():
            return
        accounts.set_active(label)
        creds = accounts.load_credentials(label)
        self.refresh()
        self.accountsChanged.emit()
        self.activeAccountChanged.emit(
            creds.get("session_key") or "", creds.get("cf_clearance")
        )

    def _on_remove(self) -> None:
        label = self._selected_label()
        if label is None:
            return
        was_active = label == accounts.get_active()
        confirm = QMessageBox(self)
        confirm.setIcon(QMessageBox.Warning)
        confirm.setWindowTitle(f"Remove '{label}'?")
        confirm.setText(
            f"Remove the '{label}' account from Sanduhr?\n\n"
            f"This deletes the stored credentials and the local "
            f"history.{label}.json file. Cannot be undone."
        )
        confirm.setStandardButtons(QMessageBox.Yes | QMessageBox.No)
        confirm.setDefaultButton(QMessageBox.No)
        _apply_root_style(confirm, self)
        if confirm.exec_() != QMessageBox.Yes:
            return

        # Wipe the history file BEFORE removing the account so we still
        # know which account file to target.
        from sanduhr import history
        history.clear_all(account=label)
        accounts.remove_account(label)
        self.refresh()
        self.accountsChanged.emit()
        if was_active:
            new_active = accounts.get_active()
            if new_active is not None:
                creds = accounts.load_credentials(new_active)
                self.activeAccountChanged.emit(
                    creds.get("session_key") or "", creds.get("cf_clearance")
                )
            else:
                # Last account removed — caller should drop into signed-out state.
                self.activeAccountChanged.emit("", None)

    def _error(self, msg: str) -> None:
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Warning)
        box.setWindowTitle("Sanduhr")
        box.setText(msg)
        _apply_root_style(box, self)
        box.exec_()

"""Tests for the AccountsTab UI in accounts_dialog.py.

Covers the four button flows (Add / Rename / Set Active / Remove) plus
the registry state assertions and signal emissions. Modal sub-dialogs
(_AddAccountDialog, _RenameAccountDialog) are stubbed so the tests
don't need to drive real Qt modal loops.
"""

import tempfile
from unittest.mock import MagicMock

import pytest
from PySide6.QtWidgets import QDialog, QMessageBox


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    """Each test gets its own APPDATA dir so history files don't leak
    between tests in this module."""
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


@pytest.fixture
def _stub_message_box(monkeypatch):
    """Auto-confirm any QMessageBox.exec_() call (for Remove confirms,
    error popups, etc.) so the tests don't block on modal dialogs."""
    monkeypatch.setattr(QMessageBox, "exec_", lambda self: QMessageBox.Yes)


@pytest.fixture
def _stub_add_dialog(monkeypatch):
    """Stub the _AddAccountDialog modal — pass `set_values` to choose
    what the next .exec_() returns."""
    state = {"values": ("New", "placeholder-new", ""), "result": QDialog.Accepted}

    class _Stub:
        def __init__(self, *args, **kwargs):
            pass
        def setStyleSheet(self, _):
            pass
        def exec_(self):
            return state["result"]
        def get_values(self):
            return state["values"]

    import sanduhr.accounts_dialog as ad
    monkeypatch.setattr(ad, "_AddAccountDialog", _Stub)
    return state


@pytest.fixture
def _stub_rename_dialog(monkeypatch):
    state = {"value": "Renamed", "result": QDialog.Accepted}

    class _Stub:
        def __init__(self, current_name, *args, **kwargs):
            self._current = current_name
        def setStyleSheet(self, _):
            pass
        def exec_(self):
            return state["result"]
        def get_value(self):
            return state["value"]

    import sanduhr.accounts_dialog as ad
    monkeypatch.setattr(ad, "_RenameAccountDialog", _Stub)
    return state


def test_empty_registry_shows_hint_and_disables_buttons(qtbot, _fake_keyring):
    from sanduhr.accounts_dialog import AccountsTab
    tab = AccountsTab()
    qtbot.addWidget(tab)
    assert tab._list.count() == 0
    assert tab._empty_hint.isVisible() is False  # not yet shown until parented
    # After explicit refresh once parented, hint logic still holds
    tab.refresh()
    assert tab._btn_rename.isEnabled() is False
    assert tab._btn_set_active.isEnabled() is False
    assert tab._btn_remove.isEnabled() is False


def test_add_first_account_emits_active_changed(
    qtbot, _fake_keyring, _stub_add_dialog
):
    from sanduhr import accounts
    from sanduhr.accounts_dialog import AccountsTab

    tab = AccountsTab()
    qtbot.addWidget(tab)
    _stub_add_dialog["values"] = ("Personal", "placeholder-1", "")

    active_changed = MagicMock()
    accounts_changed = MagicMock()
    tab.activeAccountChanged.connect(active_changed)
    tab.accountsChanged.connect(accounts_changed)

    tab._on_add()

    assert accounts.list_accounts() == ["Personal"]
    assert accounts.get_active() == "Personal"
    accounts_changed.assert_called_once()
    active_changed.assert_called_once_with("placeholder-1", None)


def test_add_second_account_does_not_change_active(
    qtbot, _fake_keyring, _stub_add_dialog
):
    from sanduhr import accounts
    from sanduhr.accounts_dialog import AccountsTab

    accounts.add_account("Personal", session_key="placeholder-personal")

    tab = AccountsTab()
    qtbot.addWidget(tab)
    _stub_add_dialog["values"] = ("Work", "placeholder-work", "")

    active_changed = MagicMock()
    tab.activeAccountChanged.connect(active_changed)

    tab._on_add()

    assert accounts.list_accounts() == ["Personal", "Work"]
    assert accounts.get_active() == "Personal"
    # Adding a second account does NOT trigger activeAccountChanged.
    active_changed.assert_not_called()


def test_add_with_invalid_name_shows_error(
    qtbot, _fake_keyring, _stub_add_dialog, _stub_message_box
):
    from sanduhr import accounts
    from sanduhr.accounts_dialog import AccountsTab

    tab = AccountsTab()
    qtbot.addWidget(tab)
    _stub_add_dialog["values"] = ("bad/name", "x", "")

    tab._on_add()

    assert accounts.list_accounts() == []


def test_set_active_emits_signal_with_credentials(qtbot, _fake_keyring):
    from PySide6.QtCore import Qt
    from sanduhr import accounts
    from sanduhr.accounts_dialog import AccountsTab

    accounts.add_account("Personal", session_key="placeholder-personal")
    accounts.add_account("Work", session_key="placeholder-work", cf_clearance="cf-placeholder-work")

    tab = AccountsTab()
    qtbot.addWidget(tab)

    # Select 'Work'
    for i in range(tab._list.count()):
        if tab._list.item(i).data(Qt.UserRole) == "Work":
            tab._list.setCurrentRow(i)
            break

    active_changed = MagicMock()
    tab.activeAccountChanged.connect(active_changed)

    tab._on_set_active()

    assert accounts.get_active() == "Work"
    active_changed.assert_called_once_with("placeholder-work", "cf-placeholder-work")


def test_rename_updates_registry(qtbot, _fake_keyring, _stub_rename_dialog):
    from sanduhr import accounts
    from sanduhr.accounts_dialog import AccountsTab

    accounts.add_account("Personal", session_key="placeholder-personal")
    tab = AccountsTab()
    qtbot.addWidget(tab)
    tab._list.setCurrentRow(0)

    _stub_rename_dialog["value"] = "Home"
    tab._on_rename()

    assert accounts.list_accounts() == ["Home"]
    assert accounts.get_active() == "Home"


def test_remove_active_account_advances_active_pointer(
    qtbot, _fake_keyring, _stub_message_box
):
    from sanduhr import accounts, history, paths
    from sanduhr.accounts_dialog import AccountsTab

    accounts.add_account("Personal", session_key="placeholder-personal")
    accounts.add_account("Work", session_key="placeholder-work")
    history.append("five_hour", 30, account="Personal")
    history.append("five_hour", 70, account="Work")

    tab = AccountsTab()
    qtbot.addWidget(tab)
    tab._list.setCurrentRow(0)  # Personal

    active_changed = MagicMock()
    tab.activeAccountChanged.connect(active_changed)

    tab._on_remove()

    assert "Personal" not in accounts.list_accounts()
    assert accounts.get_active() == "Work"
    # Personal's history file was wiped before account removal.
    assert not paths.history_file_for("Personal").exists() or \
        paths.history_file_for("Personal").read_text() == "{}"
    # Work's history is intact.
    assert history.load("five_hour", account="Work") == [70]
    # New-active credentials emitted so the widget re-points the fetcher.
    active_changed.assert_called_once_with("placeholder-work", None)


def test_remove_last_account_emits_empty_credentials(
    qtbot, _fake_keyring, _stub_message_box
):
    from sanduhr import accounts
    from sanduhr.accounts_dialog import AccountsTab

    accounts.add_account("Solo", session_key="placeholder-solo")
    tab = AccountsTab()
    qtbot.addWidget(tab)
    tab._list.setCurrentRow(0)

    active_changed = MagicMock()
    tab.activeAccountChanged.connect(active_changed)

    tab._on_remove()

    assert accounts.list_accounts() == []
    assert accounts.get_active() is None
    # Empty session_key → SettingsDialog routes to credentialsCleared.
    active_changed.assert_called_once_with("", None)

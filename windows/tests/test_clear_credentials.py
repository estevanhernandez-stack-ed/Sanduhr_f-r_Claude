"""Regression tests for v2.0.3 — blank-sessionKey save clears credentials.

Previously a blank sessionKey on save showed "sessionKey is required" and
rejected the write. That left users with no UI path to revoke credentials —
they'd have to open Windows Credential Manager by hand. Now a blank save
is treated as an explicit sign-out, gated behind a confirmation dialog.
"""

import tempfile
import pytest
from unittest.mock import MagicMock

from PySide6.QtWidgets import QMessageBox


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def test_blank_save_confirmed_calls_clear(qtbot, monkeypatch):
    """When the user saves with an empty sessionKey and confirms the
    sign-out dialog, credentials.clear() must be called and the
    credentialsCleared signal must fire."""
    from sanduhr import credentials
    from sanduhr.settings_dialog import SettingsDialog

    mock_clear = MagicMock()
    monkeypatch.setattr(credentials, "clear", mock_clear)
    monkeypatch.setattr(credentials, "save", MagicMock())

    # Stub _styled_msgbox so the exec_() calls return preset answers
    # without actually showing modal dialogs.
    call_log = []

    class FakeBox:
        def __init__(self, response):
            self._response = response

        def setDefaultButton(self, _):
            pass

        def exec_(self):
            call_log.append(self._response)
            return self._response

    def fake_styled_msgbox(parent, icon, title, text, buttons=None):
        # First call is confirmation (Yes/No), second is success info.
        if buttons is not None:
            return FakeBox(QMessageBox.Yes)
        return FakeBox(QMessageBox.Ok)

    import sanduhr.settings_dialog as sd_mod
    monkeypatch.setattr(sd_mod, "_styled_msgbox", fake_styled_msgbox)

    dlg = SettingsDialog(session_key="stored-key", cf_clearance="")
    qtbot.addWidget(dlg)

    # User empties the sessionKey field.
    dlg._sk.setText("")

    with qtbot.waitSignal(dlg.credentialsCleared, timeout=1000):
        dlg._save_credentials()

    assert mock_clear.called, "credentials.clear() should have been called"


def test_blank_save_cancelled_does_not_clear(qtbot, monkeypatch):
    """When the user saves with an empty sessionKey but cancels the
    sign-out confirmation, credentials.clear() must NOT be called and
    no signal must fire."""
    from sanduhr import credentials
    from sanduhr.settings_dialog import SettingsDialog

    mock_clear = MagicMock()
    monkeypatch.setattr(credentials, "clear", mock_clear)
    monkeypatch.setattr(credentials, "save", MagicMock())

    class FakeBox:
        def __init__(self, response):
            self._response = response

        def setDefaultButton(self, _):
            pass

        def exec_(self):
            return self._response

    def fake_styled_msgbox(parent, icon, title, text, buttons=None):
        if buttons is not None:
            return FakeBox(QMessageBox.No)  # user cancels
        pytest.fail("No info dialog should appear when user cancels")

    import sanduhr.settings_dialog as sd_mod
    monkeypatch.setattr(sd_mod, "_styled_msgbox", fake_styled_msgbox)

    dlg = SettingsDialog(session_key="stored-key", cf_clearance="")
    qtbot.addWidget(dlg)
    dlg._sk.setText("")

    # Should NOT fire credentialsCleared.
    signal_spy = MagicMock()
    dlg.credentialsCleared.connect(signal_spy)

    dlg._save_credentials()

    assert not mock_clear.called, "credentials.clear() must not be called on cancel"
    assert not signal_spy.called, "credentialsCleared signal must not fire on cancel"


def test_nonblank_save_still_works(qtbot, monkeypatch):
    """A normal save with a non-empty sessionKey must still call
    credentials.save() and fire credentialsSaved — regression guard
    ensuring the clear-flow didn't break the happy path."""
    from sanduhr import credentials
    from sanduhr.settings_dialog import SettingsDialog

    mock_save = MagicMock()
    mock_clear = MagicMock()
    monkeypatch.setattr(credentials, "save", mock_save)
    monkeypatch.setattr(credentials, "clear", mock_clear)

    class FakeBox:
        def setDefaultButton(self, _):
            pass

        def exec_(self):
            return QMessageBox.Ok

    def fake_styled_msgbox(parent, icon, title, text, buttons=None):
        return FakeBox()

    import sanduhr.settings_dialog as sd_mod
    monkeypatch.setattr(sd_mod, "_styled_msgbox", fake_styled_msgbox)

    dlg = SettingsDialog(session_key="", cf_clearance="")
    qtbot.addWidget(dlg)
    dlg._sk.setText("new-session-key")

    with qtbot.waitSignal(dlg.credentialsSaved, timeout=1000) as blocker:
        dlg._save_credentials()

    assert mock_save.called
    assert not mock_clear.called
    assert blocker.args[0] == "new-session-key"


def test_sessionkey_hint_label_exists(qtbot):
    """The hint under the sessionKey field must mention the sign-out
    behaviour, so users discover it."""
    from sanduhr.settings_dialog import SettingsDialog

    dlg = SettingsDialog(session_key="", cf_clearance="")
    qtbot.addWidget(dlg)

    # Walk the dialog for any QLabel mentioning clear/sign-out.
    from PySide6.QtWidgets import QLabel
    labels = dlg.findChildren(QLabel)
    hint_text = " ".join(lbl.text() for lbl in labels).lower()

    assert "empty" in hint_text, "Hint must mention the empty-save behaviour"
    assert ("sign out" in hint_text) or ("clear" in hint_text), (
        "Hint must mention the sign-out/clear semantics"
    )

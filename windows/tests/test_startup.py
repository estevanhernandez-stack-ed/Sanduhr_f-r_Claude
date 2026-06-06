"""Tests for startup.py -- run-on-login management.

Pure helpers plus a real winreg round-trip against a throwaway HKCU test
key (cleaned in teardown), so no test ever touches the real Run entry.
"""

import pytest

from sanduhr import startup

_TEST_RUN_KEY = r"Software\Sanduhr\test-autostart"


def test_is_packaged_detects_windowsapps():
    packaged = r"C:\Program Files\WindowsApps\626LabsLLC.SanduhrfrClaude_2.4.0.0_x64__abc\Sanduhr.exe"
    unpackaged = r"C:\Users\este\AppData\Local\Sanduhr\Sanduhr.exe"
    assert startup.is_packaged(packaged) is True
    assert startup.is_packaged(unpackaged) is False
    assert startup.is_packaged("") is False


def test_run_command_quotes_path():
    assert (
        startup.run_command(r"C:\Program Files\Sanduhr\Sanduhr.exe")
        == r'"C:\Program Files\Sanduhr\Sanduhr.exe"'
    )


def test_value_name_matches_installer():
    # Guards against drift from windows/installer/Sanduhr.iss [Registry] ValueName.
    assert startup._VALUE_NAME == "Sanduhr"


@pytest.fixture
def clean_test_key():
    yield
    import winreg

    for key in (_TEST_RUN_KEY, r"Software\Sanduhr"):
        try:
            winreg.DeleteKey(winreg.HKEY_CURRENT_USER, key)
        except (FileNotFoundError, OSError):
            pass


def test_enable_disable_roundtrip_unpackaged(clean_test_key):
    exe = r"C:\Apps\Sanduhr\Sanduhr.exe"
    assert startup.is_enabled_unpackaged(run_key=_TEST_RUN_KEY) is False
    startup.set_enabled_unpackaged(True, executable=exe, run_key=_TEST_RUN_KEY)
    assert startup.is_enabled_unpackaged(run_key=_TEST_RUN_KEY) is True
    # Disabling again is idempotent.
    startup.set_enabled_unpackaged(False, run_key=_TEST_RUN_KEY)
    startup.set_enabled_unpackaged(False, run_key=_TEST_RUN_KEY)
    assert startup.is_enabled_unpackaged(run_key=_TEST_RUN_KEY) is False


def test_set_enabled_packaged_opens_settings(monkeypatch):
    opened = {}
    monkeypatch.setattr(startup, "is_packaged", lambda *a, **k: True)
    monkeypatch.setattr(
        startup, "open_startup_settings", lambda: opened.setdefault("hit", True)
    )
    outcome = startup.set_enabled(True)
    assert outcome.opened_settings is True
    assert outcome.applied is False
    assert opened.get("hit") is True


def test_set_enabled_unpackaged_writes_run_key(monkeypatch, clean_test_key):
    monkeypatch.setattr(startup, "is_packaged", lambda *a, **k: False)
    # set_enabled_unpackaged reads the module global at call time, so
    # redirecting it keeps the write on the throwaway test key.
    monkeypatch.setattr(startup, "_RUN_KEY", _TEST_RUN_KEY)
    outcome = startup.set_enabled(True, executable=r"C:\X\Sanduhr.exe")
    assert outcome.applied is True
    assert outcome.opened_settings is False
    assert startup.is_enabled_unpackaged(run_key=_TEST_RUN_KEY) is True

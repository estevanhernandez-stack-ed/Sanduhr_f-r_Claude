"""Tests for sanduhr.credentials."""

import json
from unittest.mock import MagicMock

import pytest

from sanduhr import credentials, paths


@pytest.fixture
def mock_keyring(monkeypatch):
    storage = {}

    def _set(service, account, value):
        storage[(service, account)] = value

    def _get(service, account):
        return storage.get((service, account))

    def _delete(service, account):
        storage.pop((service, account), None)

    mock = MagicMock()
    mock.set_password = MagicMock(side_effect=_set)
    mock.get_password = MagicMock(side_effect=_get)
    mock.delete_password = MagicMock(side_effect=_delete)
    mock._storage = storage
    monkeypatch.setattr(credentials, "keyring", mock)
    return mock


def test_load_empty_returns_empty_dict(tmp_appdata, mock_keyring):
    assert credentials.load() == {"session_key": None, "cf_clearance": None}


def test_save_and_load_roundtrip(tmp_appdata, mock_keyring):
    credentials.save(session_key="abc", cf_clearance="def")
    assert credentials.load() == {"session_key": "abc", "cf_clearance": "def"}


def test_save_session_key_only(tmp_appdata, mock_keyring):
    credentials.save(session_key="abc")
    assert credentials.load()["session_key"] == "abc"
    assert credentials.load()["cf_clearance"] is None


def test_clear_removes_entries(tmp_appdata, mock_keyring):
    credentials.save(session_key="abc", cf_clearance="def")
    credentials.clear()
    assert credentials.load() == {"session_key": None, "cf_clearance": None}


def test_service_name_matches_mac_keychain(mock_keyring):
    credentials.save(session_key="abc")
    assert mock_keyring.set_password.call_args_list[0].args[0] == "com.626labs.sanduhr"


def test_migrate_v1_moves_session_key_to_keyring(
    tmp_appdata, mock_keyring, tmp_path, monkeypatch
):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    (v1_dir / "config.json").write_text(
        json.dumps({"session_key": "old-key", "theme": "mint"})
    )
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_dir / "config.json")

    result = credentials.migrate_from_v1()

    assert result["migrated"] is True
    assert credentials.load()["session_key"] == "old-key"


def test_migrate_v1_deletes_old_file(tmp_appdata, mock_keyring, tmp_path, monkeypatch):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    v1_config = v1_dir / "config.json"
    v1_config.write_text(json.dumps({"session_key": "old-key"}))
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_config)

    credentials.migrate_from_v1()
    assert not v1_config.exists()


def test_migrate_v1_preserves_theme(tmp_appdata, mock_keyring, tmp_path, monkeypatch):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    (v1_dir / "config.json").write_text(
        json.dumps({"session_key": "k", "theme": "aurora"})
    )
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_dir / "config.json")

    credentials.migrate_from_v1()
    settings = json.loads(paths.settings_file().read_text())
    assert settings.get("theme") == "aurora"


def test_migrate_v1_missing_file_is_noop(
    tmp_appdata, mock_keyring, tmp_path, monkeypatch
):
    monkeypatch.setattr(
        paths, "legacy_config_file", lambda: tmp_path / "does-not-exist.json"
    )
    result = credentials.migrate_from_v1()
    assert result["migrated"] is False


def test_migrate_v1_copies_history(tmp_appdata, mock_keyring, tmp_path, monkeypatch):
    v1_dir = tmp_path / ".claude-usage-widget"
    v1_dir.mkdir()
    (v1_dir / "config.json").write_text(json.dumps({"session_key": "k"}))
    (v1_dir / "history.json").write_text(
        json.dumps({"five_hour": [{"t": "t", "v": 42}]})
    )
    monkeypatch.setattr(paths, "legacy_config_file", lambda: v1_dir / "config.json")
    monkeypatch.setattr(paths, "legacy_history_file", lambda: v1_dir / "history.json")

    credentials.migrate_from_v1()
    copied = json.loads(paths.history_file().read_text())
    assert copied == {"five_hour": [{"t": "t", "v": 42}]}

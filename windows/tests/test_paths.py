"""Tests for sanduhr.paths."""

from pathlib import Path

from sanduhr import paths


def test_app_data_dir_uses_appdata_env(tmp_appdata):
    result = paths.app_data_dir()
    assert result == tmp_appdata / "Sanduhr"


def test_app_data_dir_creates_directory(tmp_appdata):
    assert not (tmp_appdata / "Sanduhr").exists()
    result = paths.app_data_dir()
    assert result.exists()
    assert result.is_dir()


def test_history_file_path(tmp_appdata):
    assert paths.history_file() == tmp_appdata / "Sanduhr" / "history.json"


def test_settings_file_path(tmp_appdata):
    assert paths.settings_file() == tmp_appdata / "Sanduhr" / "settings.json"


def test_log_file_path(tmp_appdata):
    assert paths.log_file() == tmp_appdata / "Sanduhr" / "sanduhr.log"


def test_last_error_file_path(tmp_appdata):
    assert paths.last_error_file() == tmp_appdata / "Sanduhr" / "last_error.json"


def test_legacy_config_file_points_to_home_dot_dir():
    result = paths.legacy_config_file()
    assert result.name == "config.json"
    assert result.parent.name == ".claude-usage-widget"
    assert result.parent.parent == Path.home()

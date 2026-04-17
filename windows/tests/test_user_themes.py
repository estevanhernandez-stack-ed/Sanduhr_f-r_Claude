"""Tests for sanduhr.themes.load_user_themes()."""

import json

import pytest

from sanduhr import paths, themes


@pytest.fixture(autouse=True)
def _restore_themes():
    """Snapshot built-in THEMES before each test, restore after so user-theme
    pollution doesn't leak between tests."""
    snapshot = dict(themes.THEMES)
    yield
    themes.THEMES.clear()
    themes.THEMES.update(snapshot)


def _minimal_theme(name: str, accent: str = "#ff00ff") -> dict:
    return {
        "name": name,
        "bg": "#1a0a1f",
        "glass": "#2a1835",
        "glass_on_mica": "#1d0f28",
        "title_bg": "#1a0a1f",
        "border": "#4a2a5e",
        "text": "#f5e0f0",
        "text_secondary": "#d4a5c8",
        "text_dim": "#8a6a82",
        "text_muted": "#5a4a58",
        "accent": accent,
        "bar_bg": "#2d1a38",
        "footer_bg": "#140a18",
        "pace_marker": "#fbbf24",
        "sparkline": accent,
    }


def test_load_user_themes_no_dir_creates_it(tmp_appdata):
    # Dir doesn't exist yet
    assert not (tmp_appdata / "Sanduhr" / "themes").exists()
    result = themes.load_user_themes()
    assert result == {}
    assert (tmp_appdata / "Sanduhr" / "themes").exists()


def test_load_user_themes_valid_json(tmp_appdata):
    themes_dir = paths.app_data_dir() / "themes"
    themes_dir.mkdir(parents=True, exist_ok=True)
    (themes_dir / "sunset.json").write_text(
        json.dumps(_minimal_theme("Sunset")), encoding="utf-8"
    )

    result = themes.load_user_themes()
    assert "sunset" in result
    assert "sunset" in themes.THEMES
    assert themes.THEMES["sunset"]["name"] == "Sunset"
    # Default glass tuning applied
    assert themes.THEMES["sunset"]["glass_alpha"] == 0.80


def test_load_user_themes_preserves_explicit_glass_tuning(tmp_appdata):
    themes_dir = paths.app_data_dir() / "themes"
    themes_dir.mkdir(parents=True, exist_ok=True)
    payload = _minimal_theme("Custom")
    payload["glass_alpha"] = 0.55
    payload["inner_highlight"] = {"color": "#ff00ff", "alpha": 0.3}
    (themes_dir / "custom.json").write_text(json.dumps(payload), encoding="utf-8")

    themes.load_user_themes()
    assert themes.THEMES["custom"]["glass_alpha"] == 0.55
    assert themes.THEMES["custom"]["inner_highlight"]["color"] == "#ff00ff"


def test_load_user_themes_missing_fields_skipped(tmp_appdata, caplog):
    themes_dir = paths.app_data_dir() / "themes"
    themes_dir.mkdir(parents=True, exist_ok=True)
    (themes_dir / "broken.json").write_text(
        json.dumps({"name": "Broken"}), encoding="utf-8"
    )

    result = themes.load_user_themes()
    assert "broken" not in result
    assert "broken" not in themes.THEMES


def test_load_user_themes_invalid_json_skipped(tmp_appdata):
    themes_dir = paths.app_data_dir() / "themes"
    themes_dir.mkdir(parents=True, exist_ok=True)
    (themes_dir / "garbage.json").write_text("{ not valid }", encoding="utf-8")

    result = themes.load_user_themes()
    assert "garbage" not in result
    assert "garbage" not in themes.THEMES


def test_load_user_themes_filename_becomes_key(tmp_appdata):
    themes_dir = paths.app_data_dir() / "themes"
    themes_dir.mkdir(parents=True, exist_ok=True)
    # File name "Neon Dream.json" -> key "neon dream" (lowercased, spaces preserved)
    (themes_dir / "neon-dream.json").write_text(
        json.dumps(_minimal_theme("Neon Dream")), encoding="utf-8"
    )

    themes.load_user_themes()
    assert "neon-dream" in themes.THEMES
    assert themes.THEMES["neon-dream"]["name"] == "Neon Dream"

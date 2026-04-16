"""Tests for sanduhr.themes."""

import pytest

from sanduhr import themes


def test_all_themes_defined():
    expected = {"obsidian", "aurora", "ember", "mint", "matrix"}
    assert set(themes.THEMES) == expected


@pytest.mark.parametrize("key", ["obsidian", "aurora", "ember", "mint", "matrix"])
def test_theme_has_required_color_fields(key):
    t = themes.THEMES[key]
    required = {"name", "bg", "glass", "glass_on_mica", "border", "text", "accent"}
    assert required.issubset(t.keys()), f"{key} missing: {required - t.keys()}"


@pytest.mark.parametrize("key", ["obsidian", "aurora", "ember", "mint"])
def test_non_matrix_themes_have_glass_dials(key):
    t = themes.THEMES[key]
    assert "glass_alpha" in t
    assert "border_alpha" in t
    assert "accent_bloom" in t
    assert isinstance(t["glass_alpha"], float)
    assert 0.0 < t["glass_alpha"] < 1.0


def test_matrix_theme_opts_out_of_glass():
    t = themes.THEMES["matrix"]
    assert t["glass_alpha"] == 1.0  # opaque
    assert t.get("opts_out_of_mica") is True


def test_usage_color_below_50_is_green():
    assert themes.usage_color(25) == "#4ade80"


def test_usage_color_exceeds_90_is_red():
    assert themes.usage_color(95) == "#f87171"


def test_usage_color_boundary_50():
    assert themes.usage_color(50) == "#facc15"


def test_usage_color_boundary_75():
    assert themes.usage_color(75) == "#fb923c"

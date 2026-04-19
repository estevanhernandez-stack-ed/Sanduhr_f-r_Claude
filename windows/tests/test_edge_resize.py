"""Tests for edge-drag resize.

The frameless widget becomes draggable from any edge or corner.
Minimum size is computed from QFontMetrics measurements of the
longest known strings so text never gets clipped below it.
"""

import json
import tempfile
import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


@pytest.fixture(autouse=True)
def _stub_modal_dialogs(monkeypatch):
    from sanduhr import widget as widget_mod
    monkeypatch.setattr(
        widget_mod.SanduhrWidget, "_prompt_first_run", lambda self: None
    )


def _build_widget(qtbot, monkeypatch):
    from sanduhr import credentials, widget
    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})
    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    return w


def test_minimum_size_is_dynamic(qtbot, monkeypatch):
    """Minimum size computed via QFontMetrics, not hardcoded."""
    w = _build_widget(qtbot, monkeypatch)
    min_w = w.minimumSize().width()
    min_h = w.minimumSize().height()
    # Should be big enough to hold the longest status string
    # ("Signed out — paste sessionKey in Settings to resume.") at 10pt
    assert min_w >= 320, f"min_w {min_w} too narrow for status strings"
    assert min_h >= 180, f"min_h {min_h} too short for compact mode"


def test_resize_zone_detection_left_edge(qtbot, monkeypatch):
    """Cursor within 6px of the left edge is a resize zone."""
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 500)
    from PySide6.QtCore import QPoint
    zone = w._resize_zone(QPoint(3, 200))
    assert zone == "left"


def test_resize_zone_detection_bottom_right_corner(qtbot, monkeypatch):
    """Corner zone takes priority over edge."""
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 500)
    from PySide6.QtCore import QPoint
    zone = w._resize_zone(QPoint(498, 498))
    assert zone == "bottom-right"


def test_resize_zone_detection_interior(qtbot, monkeypatch):
    """Interior returns None (not a resize zone)."""
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 500)
    from PySide6.QtCore import QPoint
    zone = w._resize_zone(QPoint(250, 250))
    assert zone is None


def test_resize_respects_minimum(qtbot, monkeypatch):
    """A resize that would go below minimumSize gets clamped."""
    w = _build_widget(qtbot, monkeypatch)
    min_w = w.minimumSize().width()
    min_h = w.minimumSize().height()
    w.resize(min_w - 50, min_h - 50)
    assert w.width() >= min_w
    assert w.height() >= min_h


def test_resize_persists_to_settings(qtbot, monkeypatch):
    """After a resize + save, the geometry is in settings.json."""
    from sanduhr import paths
    w = _build_widget(qtbot, monkeypatch)
    w.resize(500, 450)
    w._save_settings()
    data = json.loads(paths.settings_file().read_text(encoding="utf-8"))
    assert data.get("geom", {}).get("w") == 500
    assert data.get("geom", {}).get("h") == 450

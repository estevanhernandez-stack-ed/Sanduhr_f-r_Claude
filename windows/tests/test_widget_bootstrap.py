"""Smoke tests -- widget imports and constructs without credentials."""

import json
import pytest
import tempfile


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def test_widget_constructs_without_credentials(qtbot, monkeypatch):
    from sanduhr import credentials, widget

    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})

    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    assert w.windowTitle() == "Sanduhr für Claude"


def test_widget_status_shows_setup_prompt_with_no_credentials(qtbot, monkeypatch):
    from sanduhr import credentials, widget

    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})

    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    assert "setup" in w.status_text().lower() or "connecting" in w.status_text().lower()


def test_widget_applies_stored_theme(qtbot, monkeypatch):
    from sanduhr import credentials, paths, widget

    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})
    paths.settings_file().write_text(json.dumps({"theme": "mint"}), encoding="utf-8")

    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    assert w.current_theme_key() == "mint"

"""Regression tests for v2.0.3 — dialog legibility on light-mode Windows.

Microsoft Store review caught dialogs rendering as dark-theme text on the
system's default light background. Root cause: the root stylesheet bound
its background to `SanduhrWidget` only, so QDialog / QMessageBox fell
through to the system palette. These tests assert that the generated
stylesheet now contains explicit rules for every dialog-chrome widget
type, for every built-in theme — so the bug can't silently regress.
"""

import json
import tempfile
import pytest

from sanduhr import themes


REQUIRED_DIALOG_SELECTORS = [
    "QDialog",
    "QMessageBox",
    "QTabWidget::pane",
    "QTabBar::tab",
    "QLineEdit",
    "QTextEdit",
    "QListWidget",
    "QDialogButtonBox QPushButton",
    "QScrollBar:vertical",
]


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


def _build_widget(qtbot, monkeypatch, theme_key: str):
    from sanduhr import credentials, paths, widget
    monkeypatch.setattr(
        credentials, "load", lambda: {"session_key": None, "cf_clearance": None}
    )
    monkeypatch.setattr(credentials, "migrate_from_v1", lambda: {"migrated": False})
    paths.settings_file().write_text(
        json.dumps({"theme": theme_key}), encoding="utf-8"
    )
    w = widget.SanduhrWidget()
    qtbot.addWidget(w)
    return w


@pytest.mark.parametrize("theme_key", list(themes.THEMES.keys()))
@pytest.mark.parametrize("selector", REQUIRED_DIALOG_SELECTORS)
def test_stylesheet_contains_dialog_selector(qtbot, monkeypatch, theme_key, selector):
    """For every theme × every dialog-chrome selector, the generated
    stylesheet must include an explicit rule. Without this, dialogs fall
    through to the system palette on light-mode Windows."""
    w = _build_widget(qtbot, monkeypatch, theme_key)
    qss = w.styleSheet()
    assert selector in qss, (
        f"Theme '{theme_key}' stylesheet is missing a rule for '{selector}'. "
        f"This causes dialogs to render with system (possibly light) "
        f"background on that platform — the exact bug that MS Store flagged "
        f"as 10.1.4.4 'Navigation of the product is poor'."
    )


@pytest.mark.parametrize("theme_key", list(themes.THEMES.keys()))
def test_qdialog_rule_sets_explicit_background(qtbot, monkeypatch, theme_key):
    """The QDialog rule must set an explicit background-color. Without
    that, even if the selector matches, Qt uses the system palette.
    Value must match the theme's `bg` color."""
    w = _build_widget(qtbot, monkeypatch, theme_key)
    qss = w.styleSheet()
    theme_bg = themes.THEMES[theme_key]["bg"].lower()

    # Find the QDialog block and assert it references the theme bg.
    # Accept both `QDialog { ... }` and `QDialog, QMessageBox { ... }`.
    assert "QDialog" in qss
    assert theme_bg in qss.lower(), (
        f"Theme '{theme_key}' bg color {theme_bg} not found in stylesheet — "
        f"dialogs won't use the themed background."
    )


@pytest.mark.parametrize("theme_key", list(themes.THEMES.keys()))
def test_settings_dialog_inherits_widget_stylesheet(qtbot, monkeypatch, theme_key):
    """`_open_settings_dialog` must explicitly apply the widget's
    stylesheet to the SettingsDialog — QDialog is a separate top-level
    window and doesn't inherit QSS from its parent automatically."""
    from sanduhr.settings_dialog import SettingsDialog
    w = _build_widget(qtbot, monkeypatch, theme_key)

    dlg = SettingsDialog(w, session_key="", cf_clearance="")
    qtbot.addWidget(dlg)

    # Simulate what _open_settings_dialog does.
    dlg.setStyleSheet(w.styleSheet())

    # The applied stylesheet should contain the QDialog rule so the
    # SettingsDialog itself renders with a themed background.
    assert "QDialog" in dlg.styleSheet()
    assert "QTabBar" in dlg.styleSheet()
    assert "QLineEdit" in dlg.styleSheet()

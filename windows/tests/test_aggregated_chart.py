"""Tests for HistoryChart's multi-account aggregated render mode.

Covers data-shape assertions only — actual QPainter output is verified
manually. These lock in the contract between history.aggregate_window
and the chart's internal _data dict so a future refactor can't silently
break the overlay view."""

import tempfile
import pytest


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


@pytest.fixture
def _two_accounts_with_data(_fake_keyring):
    from sanduhr import accounts, history
    accounts.add_account("Personal", session_key="sk-p")
    accounts.add_account("Work", session_key="sk-w")
    history.append("five_hour", 30, account="Personal")
    history.append("five_hour", 70, account="Work")
    history.append("seven_day", 50, account="Personal")
    yield


def test_color_for_account_is_stable_per_label(_fake_keyring):
    """color_for_account returns the same color for the same label
    regardless of how many times it's called."""
    from sanduhr import accounts
    from sanduhr.history_chart import color_for_account, ACCOUNT_COLORS

    accounts.add_account("Personal", session_key="sk-p")
    accounts.add_account("Work", session_key="sk-w")

    c_p1 = color_for_account("Personal")
    c_p2 = color_for_account("Personal")
    c_w = color_for_account("Work")
    assert c_p1 == c_p2
    assert c_p1 != c_w
    # First-registered account gets palette index 0.
    assert c_p1 == ACCOUNT_COLORS[0]
    assert c_w == ACCOUNT_COLORS[1]


def test_color_for_account_unknown_label_returns_first_color(_fake_keyring):
    from sanduhr.history_chart import color_for_account, ACCOUNT_COLORS
    assert color_for_account("Nobody") == ACCOUNT_COLORS[0]


def test_aggregate_mode_renders_all_accounts(qtbot, _two_accounts_with_data):
    """set_account(None) → _data has each tier mapped to dict of all
    populated accounts."""
    from sanduhr.history_chart import HistoryChart

    chart = HistoryChart(theme={"text_dim": "#888", "text_secondary": "#aaa", "accent": "#fff"})
    qtbot.addWidget(chart)
    chart.set_account(None)

    # five_hour has both accounts populated
    assert "five_hour" in chart._data
    assert set(chart._data["five_hour"].keys()) == {"Personal", "Work"}
    # seven_day only has Personal — Work was never recorded for that tier
    assert "seven_day" in chart._data
    assert set(chart._data["seven_day"].keys()) == {"Personal"}


def test_single_account_mode_filters_to_one(qtbot, _two_accounts_with_data):
    """set_account(label) → _data only contains points for that account."""
    from sanduhr.history_chart import HistoryChart

    chart = HistoryChart(theme={"text_dim": "#888", "text_secondary": "#aaa", "accent": "#fff"})
    qtbot.addWidget(chart)
    chart.set_account("Work")

    assert "five_hour" in chart._data
    assert set(chart._data["five_hour"].keys()) == {"Work"}
    # seven_day was only recorded for Personal — Work has nothing, so
    # the tier shouldn't appear at all when filtered to Work.
    assert "seven_day" not in chart._data


def test_account_attribute_round_trips(qtbot, _fake_keyring):
    from sanduhr.history_chart import HistoryChart
    chart = HistoryChart(theme={"text_dim": "#888", "text_secondary": "#aaa", "accent": "#fff"})
    qtbot.addWidget(chart)
    assert chart.account() is None
    chart.set_account("Personal")
    assert chart.account() == "Personal"
    chart.set_account(None)
    assert chart.account() is None

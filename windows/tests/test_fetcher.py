"""Tests for sanduhr.fetcher -- QObject on a worker thread."""

import os
import tempfile

import pytest

from unittest.mock import MagicMock

from sanduhr import api, fetcher


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def test_fetcher_emits_data_ready_on_success(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.return_value = {"five_hour": {"utilization": 10}}
    # Routines endpoint isn't part of this test's contract — return None
    # so the synthesis branch in fetcher is a no-op (matches the
    # account-without-routines path).
    fake_client.get_routine_budget.return_value = None
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.dataReady, timeout=2000) as blocker:
        f.fetch()
    assert blocker.args == [{"five_hour": {"utilization": 10}}]


def test_fetcher_synthesizes_routines_tier_when_budget_returned(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.return_value = {"five_hour": {"utilization": 10}}
    fake_client.get_routine_budget.return_value = {"used": 3, "limit": 15}
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.dataReady, timeout=2000) as blocker:
        f.fetch()
    data = blocker.args[0]
    assert "routines" in data
    assert data["routines"]["used"] == 3
    assert data["routines"]["limit"] == 15
    # 3/15 = 20% utilization
    assert abs(data["routines"]["utilization"] - 20.0) < 0.01


def test_fetcher_skips_routines_when_budget_unavailable(qtbot, monkeypatch):
    """Account without Routines (older plan) — endpoint 404s, the
    helper returns None, and the data dict has no 'routines' key."""
    fake_client = MagicMock()
    fake_client.get_usage.return_value = {"five_hour": {"utilization": 10}}
    fake_client.get_routine_budget.return_value = None
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.dataReady, timeout=2000) as blocker:
        f.fetch()
    assert "routines" not in blocker.args[0]


def test_fetcher_emits_fetch_failed_on_session_expired(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.side_effect = api.SessionExpired("401")
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.fetchFailed, timeout=2000) as blocker:
        f.fetch()
    kind, _message = blocker.args
    assert kind == "session_expired"


def test_fetcher_emits_fetch_failed_on_cloudflare(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.side_effect = api.CloudflareBlocked("cf")
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.fetchFailed, timeout=2000) as blocker:
        f.fetch()
    assert blocker.args[0] == "cloudflare"


def test_fetcher_emits_fetch_failed_on_network(qtbot, monkeypatch):
    fake_client = MagicMock()
    fake_client.get_usage.side_effect = api.NetworkError("boom")
    monkeypatch.setattr(fetcher, "ClaudeAPI", lambda *a, **kw: fake_client)

    f = fetcher.UsageFetcher("sk", None)
    with qtbot.waitSignal(f.fetchFailed, timeout=2000) as blocker:
        f.fetch()
    assert blocker.args[0] == "network"


def test_update_credentials_rebuilds_client(qtbot, monkeypatch):
    builds = []

    def _builder(session_key, cf_clearance=None):
        builds.append((session_key, cf_clearance))
        m = MagicMock()
        m.get_usage.return_value = {}
        m.get_routine_budget.return_value = None
        return m

    monkeypatch.setattr(fetcher, "ClaudeAPI", _builder)

    f = fetcher.UsageFetcher("sk1", None)
    f.update_credentials("sk2", "cf")
    f.fetch()
    assert ("sk2", "cf") in builds

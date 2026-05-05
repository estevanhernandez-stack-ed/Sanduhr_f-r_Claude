"""Tests for per-account history routing + legacy migration."""

import json
import pytest


@pytest.fixture
def _registered_accounts(_fake_keyring):
    """Register two accounts: Personal (active) and Work."""
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="sk-p")
    accounts.add_account("Work", session_key="sk-w")
    yield


def test_history_writes_to_active_account_file(_registered_accounts, tmp_appdata):
    from sanduhr import accounts, history, paths
    accounts.set_active("Personal")
    history.append("five_hour", 50)
    p = paths.history_file_for("Personal")
    assert p.exists()
    data = json.loads(p.read_text())
    assert data["five_hour"][0]["v"] == 50


def test_history_per_account_isolated(_registered_accounts, tmp_appdata):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30)
    accounts.set_active("Work")
    history.append("five_hour", 70)
    accounts.set_active("Personal")
    assert history.load("five_hour") == [30]
    accounts.set_active("Work")
    assert history.load("five_hour") == [70]


def test_explicit_account_override(_registered_accounts, tmp_appdata):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30, account="Work")
    accounts.set_active("Work")
    assert history.load("five_hour") == [30]
    accounts.set_active("Personal")
    assert history.load("five_hour") == []


def test_legacy_migration_renames_file_to_active_account(
    _registered_accounts, tmp_appdata
):
    from sanduhr import accounts, history, paths
    accounts.set_active("Personal")
    legacy = paths.history_file()
    legacy.write_text(
        json.dumps({"five_hour": [{"t": "2026-05-01T00:00:00+00:00", "v": 42}]}),
        encoding="utf-8",
    )
    # Triggers migration on first load.
    history.load_history()
    assert not legacy.exists()
    new = paths.history_file_for("Personal")
    assert new.exists()
    assert json.loads(new.read_text())["five_hour"][0]["v"] == 42


def test_legacy_migration_skipped_when_per_account_already_exists(
    _registered_accounts, tmp_appdata
):
    """If history.{Personal}.json already exists, don't clobber it via migration."""
    from sanduhr import accounts, history, paths
    accounts.set_active("Personal")
    paths.history_file_for("Personal").write_text(
        json.dumps({"five_hour": [{"t": "2026-05-04T00:00:00+00:00", "v": 100}]}),
        encoding="utf-8",
    )
    paths.history_file().write_text(
        json.dumps({"five_hour": [{"t": "2026-05-01T00:00:00+00:00", "v": 42}]}),
        encoding="utf-8",
    )
    history.load_history()
    # Per-account file is the source of truth; legacy is left alone (no clobber).
    data = json.loads(paths.history_file_for("Personal").read_text())
    assert data["five_hour"][0]["v"] == 100


def test_aggregate_window_returns_all_accounts(_registered_accounts, tmp_appdata):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30)
    accounts.set_active("Work")
    history.append("five_hour", 70)
    agg = history.aggregate_window("five_hour", days=7)
    assert set(agg.keys()) == {"Personal", "Work"}
    assert len(agg["Personal"]) == 1
    assert len(agg["Work"]) == 1
    assert agg["Personal"][0]["v"] == 30
    assert agg["Work"][0]["v"] == 70


def test_aggregate_window_empty_for_unrecorded_tier(
    _registered_accounts, tmp_appdata
):
    from sanduhr import history
    agg = history.aggregate_window("five_hour", days=7)
    # Both accounts exist but have no history yet; each gets an empty list.
    assert agg == {"Personal": [], "Work": []}


def test_clear_all_only_clears_active_account(_registered_accounts, tmp_appdata):
    from sanduhr import accounts, history
    accounts.set_active("Personal")
    history.append("five_hour", 30)
    accounts.set_active("Work")
    history.append("five_hour", 70)
    accounts.set_active("Personal")
    history.clear_all()
    assert history.load_history() == {}
    accounts.set_active("Work")
    assert history.load("five_hour") == [70]


def test_history_when_no_active_account_is_noop(_fake_keyring, tmp_appdata):
    from sanduhr import history
    # No accounts registered.
    assert history.load_history() == {}
    assert history.load("five_hour") == []
    # Append silently no-ops when there's nowhere to write.
    history.append("five_hour", 50)
    assert history.load("five_hour") == []

"""Tests for the multi-account registry layered on top of keyring."""

import pytest


def test_empty_registry_returns_no_accounts(_fake_keyring):
    from sanduhr import accounts
    assert accounts.list_accounts() == []
    assert accounts.get_active() is None


def test_add_account_persists_and_sets_active_when_first(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1", cf_clearance=None)
    assert accounts.list_accounts() == ["Personal"]
    assert accounts.get_active() == "Personal"


def test_add_second_account_does_not_change_active(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    assert accounts.list_accounts() == ["Personal", "Work"]
    assert accounts.get_active() == "Personal"


def test_set_active_changes_pointer(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    accounts.set_active("Work")
    assert accounts.get_active() == "Work"


def test_set_active_unknown_account_raises(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    with pytest.raises(ValueError):
        accounts.set_active("Nonexistent")


def test_remove_account_clears_creds_and_advances_active(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    accounts.remove_account("Personal")
    assert accounts.list_accounts() == ["Work"]
    assert accounts.get_active() == "Work"
    assert accounts.load_credentials("Personal") == {
        "session_key": None,
        "cf_clearance": None,
    }


def test_remove_last_account_leaves_no_active(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Solo", session_key="placeholder-1")
    accounts.remove_account("Solo")
    assert accounts.list_accounts() == []
    assert accounts.get_active() is None


def test_remove_unknown_account_is_noop(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.remove_account("Nonexistent")
    assert accounts.list_accounts() == ["Personal"]
    assert accounts.get_active() == "Personal"


def test_rename_account_updates_list_active_and_keys(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1", cf_clearance="cf-placeholder-1")
    accounts.rename_account("Personal", "Home")
    assert accounts.list_accounts() == ["Home"]
    assert accounts.get_active() == "Home"
    assert accounts.load_credentials("Home") == {
        "session_key": "placeholder-1",
        "cf_clearance": "cf-placeholder-1",
    }
    assert accounts.load_credentials("Personal") == {
        "session_key": None,
        "cf_clearance": None,
    }


def test_rename_to_existing_label_rejected(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.add_account("Work", session_key="placeholder-2")
    with pytest.raises(ValueError):
        accounts.rename_account("Personal", "Work")


def test_invalid_label_rejected(_fake_keyring):
    from sanduhr import accounts
    with pytest.raises(ValueError):
        accounts.add_account("", session_key="x")
    with pytest.raises(ValueError):
        accounts.add_account("a" * 33, session_key="x")
    with pytest.raises(ValueError):
        accounts.add_account("bad/name", session_key="x")


def test_duplicate_label_rejected(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    with pytest.raises(ValueError):
        accounts.add_account("Personal", session_key="placeholder-2")


def test_load_credentials_for_unknown_account_returns_nones(_fake_keyring):
    from sanduhr import accounts
    assert accounts.load_credentials("DoesNotExist") == {
        "session_key": None,
        "cf_clearance": None,
    }


def test_save_credentials_updates_existing_account(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    accounts.save_credentials("Personal", session_key="placeholder-2", cf_clearance="cf-placeholder-2")
    assert accounts.load_credentials("Personal") == {
        "session_key": "placeholder-2",
        "cf_clearance": "cf-placeholder-2",
    }


def test_save_credentials_for_unknown_account_raises(_fake_keyring):
    from sanduhr import accounts
    with pytest.raises(ValueError):
        accounts.save_credentials("Nonexistent", session_key="x")


def test_migrate_legacy_promotes_existing_creds_to_personal(_fake_keyring):
    """Legacy sessionKey slot becomes the 'Personal' account; legacy slots cleaned up."""
    import keyring
    from sanduhr import accounts
    keyring.set_password("com.626labs.sanduhr", "sessionKey", "placeholder-legacy")
    keyring.set_password("com.626labs.sanduhr", "cf_clearance", "cf-placeholder-legacy")
    migrated = accounts.migrate_legacy()
    assert migrated is True
    assert accounts.list_accounts() == ["Personal"]
    assert accounts.get_active() == "Personal"
    assert accounts.load_credentials("Personal") == {
        "session_key": "placeholder-legacy",
        "cf_clearance": "cf-placeholder-legacy",
    }
    assert keyring.get_password("com.626labs.sanduhr", "sessionKey") is None
    assert keyring.get_password("com.626labs.sanduhr", "cf_clearance") is None


def test_migrate_legacy_noop_when_already_migrated(_fake_keyring):
    from sanduhr import accounts
    accounts.add_account("Personal", session_key="placeholder-1")
    assert accounts.migrate_legacy() is False


def test_migrate_legacy_noop_when_no_creds(_fake_keyring):
    from sanduhr import accounts
    assert accounts.migrate_legacy() is False

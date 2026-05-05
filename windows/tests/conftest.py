"""Shared pytest fixtures."""

import tempfile
from pathlib import Path

import pytest


@pytest.fixture
def tmp_appdata(monkeypatch):
    """Redirect %APPDATA% to a temp directory for the duration of a test."""
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield Path(tmp)


@pytest.fixture
def _fake_keyring(monkeypatch):
    """In-memory keyring backend. Opt-in fixture for tests that touch the
    credential layer — avoids writing to the real OS keychain.

    Returns the underlying dict so tests can inspect or seed slots directly.
    """
    import keyring
    import keyring.errors

    store: dict[tuple[str, str], str] = {}

    def _get(service: str, username: str):
        return store.get((service, username))

    def _set(service: str, username: str, password: str):
        store[(service, username)] = password

    def _del(service: str, username: str):
        if (service, username) not in store:
            raise keyring.errors.PasswordDeleteError("not found")
        del store[(service, username)]

    monkeypatch.setattr(keyring, "get_password", _get)
    monkeypatch.setattr(keyring, "set_password", _set)
    monkeypatch.setattr(keyring, "delete_password", _del)
    return store

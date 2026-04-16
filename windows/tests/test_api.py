"""Tests for sanduhr.api -- HTTP stubbed via requests-mock."""

import pytest

from sanduhr import api


@pytest.fixture
def mock_scraper(monkeypatch):
    """Replace cloudscraper.create_scraper with a plain requests.Session."""
    import requests

    def _create_scraper():
        return requests.Session()

    monkeypatch.setattr(api.cloudscraper, "create_scraper", _create_scraper)


def test_get_usage_happy_path(mock_scraper, requests_mock):
    requests_mock.get(
        "https://claude.ai/api/organizations",
        json=[{"uuid": "org-123"}],
    )
    requests_mock.get(
        "https://claude.ai/api/organizations/org-123/usage",
        json={"five_hour": {"utilization": 42, "resets_at": "2030-01-01T00:00:00Z"}},
    )

    client = api.ClaudeAPI("abc")
    result = client.get_usage()
    assert result["five_hour"]["utilization"] == 42


def test_get_usage_caches_org_id(mock_scraper, requests_mock):
    orgs = requests_mock.get(
        "https://claude.ai/api/organizations", json=[{"uuid": "org-x"}]
    )
    requests_mock.get("https://claude.ai/api/organizations/org-x/usage", json={})

    client = api.ClaudeAPI("abc")
    client.get_usage()
    client.get_usage()
    client.get_usage()

    assert orgs.call_count == 1


def test_401_raises_session_expired(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", status_code=401)
    with pytest.raises(api.SessionExpired):
        api.ClaudeAPI("abc").get_usage()


def test_403_with_cloudflare_body_raises_cloudflare_blocked(mock_scraper, requests_mock):
    requests_mock.get(
        "https://claude.ai/api/organizations",
        status_code=403,
        text="<html><title>Just a moment...</title><body>cf-challenge</body></html>",
    )
    with pytest.raises(api.CloudflareBlocked):
        api.ClaudeAPI("abc").get_usage()


def test_403_without_cloudflare_signature_is_session_expired(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", status_code=403, text="{}")
    with pytest.raises(api.SessionExpired):
        api.ClaudeAPI("abc").get_usage()


def test_5xx_raises_network_error(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", status_code=502)
    with pytest.raises(api.NetworkError):
        api.ClaudeAPI("abc").get_usage()


def test_cookie_header_includes_session_key(mock_scraper, requests_mock):
    m = requests_mock.get(
        "https://claude.ai/api/organizations", json=[{"uuid": "org-123"}]
    )
    requests_mock.get("https://claude.ai/api/organizations/org-123/usage", json={})
    api.ClaudeAPI("the-key").get_usage()
    assert "sessionKey=the-key" in m.last_request.headers["Cookie"]


def test_cookie_header_includes_cf_clearance_when_provided(
    mock_scraper, requests_mock
):
    m = requests_mock.get(
        "https://claude.ai/api/organizations", json=[{"uuid": "org-123"}]
    )
    requests_mock.get("https://claude.ai/api/organizations/org-123/usage", json={})
    api.ClaudeAPI("sk", cf_clearance="cf-cookie").get_usage()
    cookies = m.last_request.headers["Cookie"]
    assert "sessionKey=sk" in cookies
    assert "cf_clearance=cf-cookie" in cookies


def test_no_orgs_raises_network_error(mock_scraper, requests_mock):
    requests_mock.get("https://claude.ai/api/organizations", json=[])
    with pytest.raises(api.NetworkError):
        api.ClaudeAPI("abc").get_usage()

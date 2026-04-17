"""Claude.ai API client.

Wraps cloudscraper for Cloudflare-aware requests, raises typed
exceptions so the UI layer can give targeted error feedback.
"""

from typing import Optional

import cloudscraper
import requests


class APIError(Exception):
    """Base class for API errors."""


class SessionExpired(APIError):
    """401 or 403 -- key rotation needed."""


class CloudflareBlocked(APIError):
    """403 with Cloudflare challenge markup -- cf_clearance cookie needed."""


class NetworkError(APIError):
    """5xx, DNS, timeout, or unexpected API response."""


_API_BASE = "https://claude.ai/api"


def _looks_like_cloudflare(text: str) -> bool:
    """Detect Cloudflare challenge / block page by page content."""
    if not text:
        return False
    t = text.lower()
    return "cf-challenge" in t or "just a moment" in t or "cloudflare" in t


class ClaudeAPI:
    def __init__(self, session_key: str, cf_clearance: Optional[str] = None):
        self.session_key = session_key
        self.cf_clearance = cf_clearance
        self._scraper = cloudscraper.create_scraper()
        self._scraper.headers["Accept"] = "application/json"
        self._org_id: Optional[str] = None

    def _cookie_header(self) -> str:
        parts = [f"sessionKey={self.session_key}"]
        if self.cf_clearance:
            parts.append(f"cf_clearance={self.cf_clearance}")
        return "; ".join(parts)

    def _get(self, url: str) -> requests.Response:
        return self._scraper.get(
            url, headers={"Cookie": self._cookie_header()}, timeout=15
        )

    def _check(self, resp: requests.Response) -> None:
        if resp.status_code == 401:
            raise SessionExpired("HTTP 401 -- session key rejected")
        if resp.status_code == 403:
            if _looks_like_cloudflare(resp.text):
                raise CloudflareBlocked("Cloudflare challenge -- cf_clearance needed")
            raise SessionExpired("HTTP 403 -- session key rejected")
        if resp.status_code >= 500:
            raise NetworkError(f"HTTP {resp.status_code}")
        resp.raise_for_status()

    def _get_org_id(self) -> str:
        if self._org_id is not None:
            return self._org_id
        resp = self._get(f"{_API_BASE}/organizations")
        self._check(resp)
        try:
            orgs = resp.json()
        except ValueError as e:
            raise NetworkError("Org discovery returned non-JSON") from e
        if not orgs:
            raise NetworkError("No organizations returned for this account")
        self._org_id = orgs[0]["uuid"]
        return self._org_id

    def get_usage(self) -> dict:
        org_id = self._get_org_id()
        resp = self._get(f"{_API_BASE}/organizations/{org_id}/usage")
        self._check(resp)
        try:
            return resp.json()
        except ValueError as e:
            raise NetworkError("Usage endpoint returned non-JSON") from e

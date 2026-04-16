"""Threaded usage fetcher.

Owns a `ClaudeAPI` instance, exposes `fetch()` slot and
`dataReady` / `fetchFailed` signals. Meant to be moved to a
`QThread` so network calls never block the GUI.
"""

import logging
from typing import Optional

from PySide6.QtCore import QObject, Signal, Slot

from sanduhr import api, history
from sanduhr.api import ClaudeAPI

_log = logging.getLogger(__name__)

_HISTORY_TIERS = (
    "five_hour",
    "seven_day",
    "seven_day_sonnet",
    "seven_day_opus",
    "seven_day_cowork",
    "seven_day_omelette",
    "seven_day_oauth_apps",
    "iguana_necktie",
)


class UsageFetcher(QObject):
    """Wraps ClaudeAPI, emits Qt signals on success/failure."""

    dataReady = Signal(dict)
    fetchFailed = Signal(str, str)  # (kind, message)

    def __init__(
        self, session_key: str, cf_clearance: Optional[str] = None, parent=None
    ):
        super().__init__(parent)
        self._client = ClaudeAPI(session_key, cf_clearance)

    def update_credentials(
        self, session_key: str, cf_clearance: Optional[str] = None
    ) -> None:
        """Swap the underlying client (called when user updates credentials)."""
        self._client = ClaudeAPI(session_key, cf_clearance)

    @Slot()
    def fetch(self) -> None:
        """Fetch usage on the current thread. Emits exactly one signal."""
        try:
            data = self._client.get_usage()
        except api.SessionExpired as e:
            self.fetchFailed.emit("session_expired", str(e))
            return
        except api.CloudflareBlocked as e:
            self.fetchFailed.emit("cloudflare", str(e))
            return
        except api.NetworkError as e:
            self.fetchFailed.emit("network", str(e))
            return
        except Exception as e:
            _log.exception("Unexpected fetch failure")
            self.fetchFailed.emit("unknown", str(e))
            return

        for tier_key in _HISTORY_TIERS:
            tier = data.get(tier_key)
            if tier and tier.get("utilization") is not None:
                try:
                    history.append(tier_key, int(tier["utilization"]))
                except Exception:
                    _log.exception("Failed to append history for %s", tier_key)

        self.dataReady.emit(data)

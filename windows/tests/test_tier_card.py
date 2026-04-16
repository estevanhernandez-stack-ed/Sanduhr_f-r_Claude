"""Tests for TierCard -- logic-level assertions, not pixel-level."""

import pytest
import tempfile
from datetime import datetime, timedelta, timezone

from sanduhr import themes
from sanduhr.tiers import TierCard


@pytest.fixture(autouse=True)
def _isolate_appdata(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setenv("APPDATA", tmp)
        yield


def test_tier_card_initial_state(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    assert card.label_text() == "Session (5hr)"
    assert card.percentage_text() == "0%"


def test_tier_card_update_sets_percentage(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    card.update_state(util=42, resets_at=None, history_values=[])
    assert card.percentage_text() == "42%"


def test_tier_card_update_sets_reset_text(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    future = (datetime.now(timezone.utc) + timedelta(hours=2)).isoformat().replace(
        "+00:00", "Z"
    )
    card.update_state(util=50, resets_at=future, history_values=[])
    assert "Resets in" in card.reset_text()


def test_tier_card_apply_theme_switches_palette(qtbot):
    card = TierCard(
        tier_key="five_hour",
        label="Session (5hr)",
        theme=themes.THEMES["obsidian"],
    )
    qtbot.addWidget(card)
    card.apply_theme(themes.THEMES["mint"])
    assert card.current_theme_name() == "Mint"

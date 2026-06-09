"""Tests for plan.py -- subscription-tier label mapping.

Pure-function coverage plus a redacted real-payload fixture so the parse
is pinned to an actual /api/organizations response shape.
"""

import json
from pathlib import Path

from sanduhr.plan import PlanBadge, plan_label

_MAX_20X = "Max ×20"


def test_max_20x_subscription():
    b = plan_label("default_claude_max_20x", "stripe_subscription", ["claude_max", "chat"])
    assert b is not None
    assert b.name == _MAX_20X
    assert "Galaxy Brain Max" in b.riffs
    assert len(b.riffs) >= 2


def test_max_5x_subscription():
    b = plan_label("default_claude_max_5x", "stripe_subscription", ["claude_max"])
    assert b is not None
    assert b.name == "Max"
    assert "Maximum Effort" in b.riffs


def test_pro_subscription():
    assert plan_label("default_claude_pro", "stripe_subscription", ["claude_pro", "chat"]) == PlanBadge("Pro", [])


def test_team_subscription():
    b = plan_label("default_claude_team", "stripe_subscription", ["claude_team"])
    assert b is not None
    assert b.name == "Team"
    assert b.riffs == []


def test_prepaid_api_org_returns_none():
    assert plan_label("auto_prepaid_tier_0", "prepaid", ["api", "api_individual"]) is None


def test_unknown_subscription_tier_returns_none():
    assert plan_label("some_future_tier", "stripe_subscription", ["chat"]) is None


def test_missing_or_empty_tier_returns_none():
    assert plan_label(None) is None
    assert plan_label("") is None


def test_non_subscription_billing_blocks_badge():
    # Even with 'max' in the string, a non-subscription billing type
    # must never render a subscription badge.
    assert plan_label("weird_max_thing", "prepaid", ["api"]) is None


def test_lenient_when_billing_absent():
    # Older callers may pass only the tier string -- best-effort parse.
    b = plan_label("default_claude_max_20x")
    assert b is not None and b.name == _MAX_20X


def test_riffs_are_copies_not_shared_state():
    a = plan_label("default_claude_max_20x", "stripe_subscription", ["claude_max"])
    a.riffs.append("MUTATED")
    b = plan_label("default_claude_max_20x", "stripe_subscription", ["claude_max"])
    assert "MUTATED" not in b.riffs


def test_real_payload_fixture():
    fx = json.loads(
        (Path(__file__).parent / "fixtures" / "organizations_sample.json").read_text(
            encoding="utf-8"
        )
    )
    sub = next(o for o in fx if o["billing_type"] == "stripe_subscription")
    b = plan_label(sub["rate_limit_tier"], sub["billing_type"], sub["capabilities"])
    assert b is not None and b.name == _MAX_20X

    api_org = next(o for o in fx if o["billing_type"] == "prepaid")
    assert (
        plan_label(api_org["rate_limit_tier"], api_org["billing_type"], api_org["capabilities"])
        is None
    )

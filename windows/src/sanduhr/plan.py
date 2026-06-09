"""Subscription-tier label mapping for the footer tier badge.

Pure, Qt-free, network-free. Maps the ``rate_limit_tier`` field from the
claude.ai ``/api/organizations`` response to a clean, store-safe display
name plus a collection of tongue-in-cheek "easter egg" riffs.

The footer badge shows the clean plan name; the riffs rotate through the
tooltip (a different one each time the user hovers). Parsing is
defensive -- a substring match gated on a Claude subscription -- because
only ``default_claude_max_20x`` (Max x20) and the prepaid/API tiers have
been observed directly; the other plan strings are best-guess and will
firm up as more accounts surface.
"""

from typing import List, NamedTuple, Optional


class PlanBadge(NamedTuple):
    """A resolved subscription tier: a clean display name + rotating riffs."""

    name: str
    riffs: List[str]


# Easter-egg riffs per tier. The visible badge always shows the clean
# name; these ride in the rotating tooltip. Only the Max tiers carry
# riffs today -- Pro/Team show the plain name (empty list, easily
# extended later).
_RIFFS = {
    "max_20x": [
        "Plaid Max",
        "Maximum Overdrive",
        "Ridiculous Speed",
        "Galaxy Brain Max",
    ],
    "max_5x": ["Maximum Effort", "Max Headroom", "Big Max"],
}


def _is_subscription(billing_type: Optional[str], capabilities: List[str]) -> bool:
    """Distinguish a claude.ai subscription from a prepaid/API console org.

    When billing or capability info is present we require positive
    evidence of a subscription. When BOTH are absent (older callers that
    pass only the tier string) we don't block -- the caller gets
    best-effort parsing off the tier string alone.
    """
    if billing_type is None and not capabilities:
        return True  # no signal to gate on -- be lenient
    if billing_type == "stripe_subscription":
        return True
    return any(c in capabilities for c in ("claude_max", "claude_pro", "claude_team"))


def plan_label(
    rate_limit_tier: Optional[str],
    billing_type: Optional[str] = None,
    capabilities: Optional[List[str]] = None,
) -> Optional[PlanBadge]:
    """Resolve an org's ``rate_limit_tier`` to a :class:`PlanBadge`.

    Returns ``None`` when the account is not a recognized claude.ai
    subscription (the footer badge is then hidden). Prepaid / API orgs --
    e.g. ``auto_prepaid_tier_0`` with ``billing_type == "prepaid"`` --
    always resolve to ``None``.
    """
    if not rate_limit_tier:
        return None
    if not _is_subscription(billing_type, capabilities or []):
        return None

    tier = rate_limit_tier.lower()
    if "max_20x" in tier:
        return PlanBadge("Max ×20", list(_RIFFS["max_20x"]))
    if "max" in tier:
        return PlanBadge("Max", list(_RIFFS["max_5x"]))
    if "team" in tier:
        return PlanBadge("Team", [])
    if "pro" in tier:
        return PlanBadge("Pro", [])
    return None

"""Local Claude Code session-log reader.

Surface a real-time delta of token usage on top of Sanduhr's API
canonical numbers. Anthropic's /usage endpoint lags actual
consumption by minutes; the local CC session JSONL files update as
events happen, so reading them gives sharper "what just burned in
the last 30 seconds" feedback.

Conventions:
    Discovery — search ~/.claude/projects/ AND ~/.claude-personal/projects/
                (some users run a custom Claude Code config dir).
                Files are {session-uuid}.jsonl, one event per line.
    Parsing  — only `type=assistant` events carry token counts. The
               relevant payload lives at `message.usage` and the model
               at `message.model`. Other event types (user, attachment,
               system, etc.) are skipped.
    Counting — input_tokens + output_tokens. Cache-creation /
               cache-read tokens are billed differently and don't
               always count against subscription tier limits the same
               way; lump them in only when we know the upstream
               policy. For now: just the two human-burn fields.
"""

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterator, Optional


_LOG_ROOT_NAMES = (".claude", ".claude-personal")


def search_roots() -> list[Path]:
    """Candidate Claude Code config roots in the current user's home.
    Returns only roots that actually exist."""
    home = Path.home()
    out = []
    for name in _LOG_ROOT_NAMES:
        p = home / name
        if p.is_dir():
            out.append(p)
    return out


def discover_log_files() -> list[Path]:
    """Return all session JSONL files across known CC roots.

    Files live at `<root>/projects/<encoded-cwd>/<session-uuid>.jsonl`.
    Roots that don't exist are silently skipped; missing
    `projects/` subdir is also fine (fresh CC install, no sessions yet)."""
    files: list[Path] = []
    for root in search_roots():
        projects = root / "projects"
        if not projects.is_dir():
            continue
        for project_dir in projects.iterdir():
            if not project_dir.is_dir():
                continue
            files.extend(project_dir.glob("*.jsonl"))
    return files


def _parse_iso(s: Optional[str]) -> Optional[datetime]:
    """Permissive ISO-8601 parser. Returns None on bad input rather
    than raising so a single malformed timestamp doesn't kill the
    whole aggregation."""
    if not s:
        return None
    try:
        return datetime.fromisoformat(s.replace("Z", "+00:00"))
    except (ValueError, AttributeError, TypeError):
        return None


def iter_usage_events(
    path: Path,
) -> Iterator[tuple[Optional[datetime], Optional[str], dict]]:
    """Stream `(timestamp, model, usage_dict)` tuples for every
    `type=assistant` event in a single session JSONL.

    Streaming (line by line) instead of slurping the whole file —
    active sessions can hit tens of MB and we only want the latest
    delta. Malformed lines are skipped silently; one bad write at
    the end of a live file shouldn't disqualify everything before."""
    try:
        f = open(path, encoding="utf-8", errors="replace")
    except OSError:
        return
    try:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                d = json.loads(line)
            except (json.JSONDecodeError, UnicodeDecodeError):
                continue
            if d.get("type") != "assistant":
                continue
            msg = d.get("message")
            if not isinstance(msg, dict):
                continue
            usage = msg.get("usage")
            if not isinstance(usage, dict):
                continue
            yield (_parse_iso(d.get("timestamp")), msg.get("model"), usage)
    finally:
        f.close()


def _human_tokens(usage: dict) -> int:
    """Sum the token fields that count as 'live burn' against the
    user's subscription. input + output. Cache fields excluded —
    cache-read is essentially free and cache-creation is billed but
    rolled into the upstream limit accounting in a way we can't
    untangle from the local log alone."""
    return int(usage.get("input_tokens", 0) or 0) + int(
        usage.get("output_tokens", 0) or 0
    )


def tokens_since(cutoff: datetime) -> dict[str, int]:
    """Return `{model_name: total_tokens}` summed across ALL session
    files in ALL discovered roots, considering only events with
    timestamp >= cutoff. Models with zero new tokens since cutoff
    are omitted."""
    totals: dict[str, int] = {}
    if cutoff.tzinfo is None:
        cutoff = cutoff.replace(tzinfo=timezone.utc)
    for path in discover_log_files():
        for ts, model, usage in iter_usage_events(path):
            if ts is None or model is None:
                continue
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            if ts < cutoff:
                continue
            tokens = _human_tokens(usage)
            if tokens <= 0:
                continue
            totals[model] = totals.get(model, 0) + tokens
    return totals


# Mapping from model-name prefix → Sanduhr tier_key. Used by callers
# that want to overlay the local-log delta on a specific tier card.
# Order matters: more specific prefixes first so 'claude-opus-4-7'
# matches the opus rule before any future generic catch-all.
_MODEL_TIER_PREFIXES = (
    ("claude-opus", "seven_day_opus"),
    ("claude-sonnet", "seven_day_sonnet"),
    ("claude-haiku", "seven_day"),  # No haiku-specific tier — fold to weekly.
)


def model_to_tier_key(model: Optional[str]) -> Optional[str]:
    """Map a CC `message.model` string to one of Sanduhr's tier keys.
    Returns None for unrecognized models so callers can decide
    whether to ignore them or fold into a generic bucket."""
    if not model:
        return None
    for prefix, tier in _MODEL_TIER_PREFIXES:
        if model.startswith(prefix):
            return tier
    return None


def tokens_since_by_tier(cutoff: datetime) -> dict[str, int]:
    """Convenience wrapper: same as tokens_since() but keyed by
    Sanduhr tier rather than CC model name. Models that don't map
    to a known tier are dropped."""
    by_model = tokens_since(cutoff)
    by_tier: dict[str, int] = {}
    for model, tokens in by_model.items():
        tier = model_to_tier_key(model)
        if tier is None:
            continue
        by_tier[tier] = by_tier.get(tier, 0) + tokens
    return by_tier

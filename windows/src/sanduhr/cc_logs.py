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
import time
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Iterator, NamedTuple, Optional


class UsageEvent(NamedTuple):
    """One assistant-message event extracted from a CC session log.
    Carries the fields aggregations need: when, which model, the raw
    usage dict, plus context (project cwd + active skill if any)."""
    timestamp: Optional[datetime]
    model: Optional[str]
    usage: dict
    cwd: Optional[str]
    attribution_skill: Optional[str]


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


def iter_usage_events(path: Path) -> Iterator[UsageEvent]:
    """Stream UsageEvent records for every `type=assistant` event in
    a single session JSONL.

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
            yield UsageEvent(
                timestamp=_parse_iso(d.get("timestamp")),
                model=msg.get("model"),
                usage=usage,
                cwd=d.get("cwd"),
                attribution_skill=d.get("attributionSkill"),
            )
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


def _file_mtime_after(path: Path, cutoff_epoch: float) -> bool:
    """Cheap stat-check: does this file's mtime fall after the cutoff?
    Used to skip parsing entirely on session files that haven't been
    touched since the time window we care about. Heavy users with
    months of CC history accumulate hundreds of session files —
    skipping them at the stat() level instead of parsing every line
    is the difference between an instant refresh and a multi-second
    freeze."""
    try:
        return path.stat().st_mtime >= cutoff_epoch
    except OSError:
        return False


def tokens_since(cutoff: datetime) -> dict[str, int]:
    """Return `{model_name: total_tokens}` summed across ALL session
    files in ALL discovered roots, considering only events with
    timestamp >= cutoff. Models with zero new tokens since cutoff
    are omitted."""
    totals: dict[str, int] = {}
    if cutoff.tzinfo is None:
        cutoff = cutoff.replace(tzinfo=timezone.utc)
    cutoff_epoch = cutoff.timestamp()
    for path in discover_log_files():
        if not _file_mtime_after(path, cutoff_epoch):
            continue
        for event in iter_usage_events(path):
            if event.timestamp is None or event.model is None:
                continue
            ts = event.timestamp
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            if ts < cutoff:
                continue
            tokens = _human_tokens(event.usage)
            if tokens <= 0:
                continue
            totals[event.model] = totals.get(event.model, 0) + tokens
    return totals


def tokens_by_day(days_back: int = 30) -> dict[date, int]:
    """Sum tokens grouped by LOCAL calendar date for the trailing
    `days_back` days. Returns `{date: total_tokens}`. The date is
    each event's timestamp converted to the user's local timezone,
    which matches what they'd recognize as 'today's usage' /
    'yesterday's usage'."""
    cutoff = datetime.now(timezone.utc) - timedelta(days=days_back)
    cutoff_epoch = cutoff.timestamp()
    by_day: dict[date, int] = {}
    for path in discover_log_files():
        if not _file_mtime_after(path, cutoff_epoch):
            continue
        for event in iter_usage_events(path):
            if event.timestamp is None:
                continue
            ts = event.timestamp
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            if ts < cutoff:
                continue
            tokens = _human_tokens(event.usage)
            if tokens <= 0:
                continue
            local_date = ts.astimezone().date()
            by_day[local_date] = by_day.get(local_date, 0) + tokens
    return by_day


def tokens_by_project(since: datetime) -> dict[str, int]:
    """Sum tokens grouped by working directory (`cwd`) since the
    cutoff. Returns `{cwd: total_tokens}`. Events without a cwd are
    skipped so we don't have a None bucket."""
    if since.tzinfo is None:
        since = since.replace(tzinfo=timezone.utc)
    cutoff_epoch = since.timestamp()
    by_project: dict[str, int] = {}
    for path in discover_log_files():
        if not _file_mtime_after(path, cutoff_epoch):
            continue
        for event in iter_usage_events(path):
            if event.timestamp is None or event.cwd is None:
                continue
            ts = event.timestamp
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            if ts < since:
                continue
            tokens = _human_tokens(event.usage)
            if tokens <= 0:
                continue
            by_project[event.cwd] = by_project.get(event.cwd, 0) + tokens
    return by_project


def tokens_by_skill(since: datetime) -> dict[str, int]:
    """Sum tokens grouped by `attributionSkill` since the cutoff.
    Returns `{skill_name: total_tokens}`. Events without an
    attributionSkill are skipped — the caller is asking specifically
    for skill-attributed work."""
    if since.tzinfo is None:
        since = since.replace(tzinfo=timezone.utc)
    cutoff_epoch = since.timestamp()
    by_skill: dict[str, int] = {}
    for path in discover_log_files():
        if not _file_mtime_after(path, cutoff_epoch):
            continue
        for event in iter_usage_events(path):
            if event.timestamp is None or not event.attribution_skill:
                continue
            ts = event.timestamp
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            if ts < since:
                continue
            tokens = _human_tokens(event.usage)
            if tokens <= 0:
                continue
            by_skill[event.attribution_skill] = (
                by_skill.get(event.attribution_skill, 0) + tokens
            )
    return by_skill


# In-memory cache for the LocalCCTab combined aggregation. Tab
# refreshes hit the cache after the first compute; the data is
# acceptable to be ~30 s stale (the underlying sessions log faster
# than that, but not by much).
_AGG_CACHE: dict = {
    "computed_at": 0.0,
    "days_back": None,
    "result": None,
}
_AGG_CACHE_TTL_SEC = 30


def aggregate_for_local_cc_tab(days_back: int = 30) -> dict:
    """Single-pass aggregation for the Local CC tab — replaces three
    separate iterations (tokens_by_day + tokens_by_project +
    tokens_by_skill) with one walk through the session files.

    Combined with mtime filtering and a 30 s cache, this turns a
    multi-second freeze into a sub-100ms refresh on heavy CC users
    with hundreds of session files.

    Returns `{'by_day': ..., 'by_project': ..., 'by_skill': ...}`."""
    now = time.time()
    if (
        _AGG_CACHE["result"] is not None
        and _AGG_CACHE["days_back"] == days_back
        and (now - _AGG_CACHE["computed_at"]) < _AGG_CACHE_TTL_SEC
    ):
        return _AGG_CACHE["result"]

    cutoff = datetime.now(timezone.utc) - timedelta(days=days_back)
    cutoff_epoch = cutoff.timestamp()
    by_day: dict[date, int] = {}
    by_project: dict[str, int] = {}
    by_skill: dict[str, int] = {}

    for path in discover_log_files():
        if not _file_mtime_after(path, cutoff_epoch):
            continue
        for event in iter_usage_events(path):
            if event.timestamp is None:
                continue
            ts = event.timestamp
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            if ts < cutoff:
                continue
            tokens = _human_tokens(event.usage)
            if tokens <= 0:
                continue

            by_day[ts.astimezone().date()] = (
                by_day.get(ts.astimezone().date(), 0) + tokens
            )
            if event.cwd:
                by_project[event.cwd] = by_project.get(event.cwd, 0) + tokens
            if event.attribution_skill:
                by_skill[event.attribution_skill] = (
                    by_skill.get(event.attribution_skill, 0) + tokens
                )

    result = {"by_day": by_day, "by_project": by_project, "by_skill": by_skill}
    _AGG_CACHE.update(computed_at=now, days_back=days_back, result=result)
    return result


def invalidate_cache() -> None:
    """Force the next aggregate_for_local_cc_tab call to recompute.
    Tests use this to verify cache behavior without time travel."""
    _AGG_CACHE["computed_at"] = 0.0
    _AGG_CACHE["result"] = None


def project_display_name(cwd: str) -> str:
    """Pull a friendly basename out of a cwd path for display in the
    Local CC tab. `C:\\Users\\estev\\Projects\\Sanduhr` → `Sanduhr`.
    Handles both forward and backslash separators."""
    if not cwd:
        return ""
    parts = cwd.replace("\\", "/").rstrip("/").split("/")
    last = parts[-1] if parts else cwd
    return last or cwd


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

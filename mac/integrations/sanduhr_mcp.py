#!/usr/bin/env python3
"""sanduhr-mcp for macOS: Claude subscription headroom for agents.

Mac port of windows-dotnet/src/Sanduhr.Mcp (stdio JSON-RPC, one message per line).
Same server name, tool names and response shapes as Windows, so an agent can call
get_usage the same way on either machine. This first cut serves the two
snapshot-backed tools:

  get_usage   tiers with utilization, headroom, reset countdown, pace and projection
  ping        health: snapshot found, age, writer version, why get_usage is stale

Read-only. Reads ~/Library/Application Support/Sanduhr/snapshot.json, written by the
Sanduhr widget after every fetch. Never reads the session key, never calls claude.ai,
never posts anything. Failures are typed results (status/reason/remedy), never
protocol errors. Python 3.9 standard library only.

Not yet on Mac (see docs/mac-parity-plan.md): get_local_burn_by_project,
get_model_usage, get_usage_history, publish_usage, propose_theme.
"""
import json
import os
import sys
from datetime import datetime, timezone

SERVER_VERSION = "0.1.0-mac"
PROTOCOL_VERSION = "2025-06-18"
SCHEMA_VERSION = 1
FRESH_SECONDS = 450
DEAD_SECONDS = 900
SNAPSHOT = os.environ.get("SANDUHR_SNAPSHOT") or os.path.expanduser(
    "~/Library/Application Support/Sanduhr/snapshot.json")
DATA_LAG_NOTE = "claude.ai's own numbers lag consumption by several minutes"
LABELS = {
    "five_hour": "Session (5hr)",
    "seven_day": "Weekly - All Models",
    "seven_day_sonnet": "Weekly - Sonnet",
    "seven_day_opus": "Weekly - Opus",
    "seven_day_cowork": "Weekly - Cowork",
    "seven_day_omelette": "Weekly - Design",
    "seven_day_oauth_apps": "Weekly - OAuth Apps",
    "iguana_necktie": "Weekly - Special",
}
REMEDY_START = "Start Sanduhr (it writes the snapshot after every fetch, every 5 minutes)."
REMEDY_ERROR = {
    "session_expired": "Sanduhr's claude.ai session expired - sign in again in Sanduhr (Settings > Credentials). Tiers below are last-good, not current.",
    "cloudflare": "Sanduhr is blocked by a Cloudflare challenge - paste a fresh cf_clearance in Sanduhr. Tiers below are last-good, not current.",
}
REMEDY_NETWORK = "Sanduhr's last fetch failed (network). Tiers below are last-good, not current."

TOOLS = [
    {
        "name": "get_usage",
        "description": (
            "Check Claude subscription quota headroom (the Sanduhr widget's live snapshot of "
            "claude.ai usage). Call BEFORE spawning subagents, launching long autonomous runs, "
            "or choosing a bigger model for a large job. Reflects the Sanduhr widget's signed-in "
            "account, which may not be the account this session bills to - confirm with the "
            "user if they run multiple accounts. A stale or no_data status means unknown "
            "headroom - never assume budget."),
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "annotations": {"readOnlyHint": True},
    },
    {
        "name": "ping",
        "description": (
            "Health check for the Sanduhr usage server: is the widget writing its snapshot, "
            "how old it is, and why get_usage would be stale. Use when get_usage returns "
            "stale or no_data."),
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "annotations": {"readOnlyHint": True},
    },
]


def parse(ts):
    if not ts or not isinstance(ts, str):
        return None
    try:
        s = ts.strip().replace("Z", "+00:00")
        if "." in s:
            head, rest = s.split(".", 1)
            i = 0
            while i < len(rest) and rest[i].isdigit():
                i += 1
            s = head + "." + (rest[:i] + "000000")[:6] + rest[i:]
        d = datetime.fromisoformat(s)
        return d if d.tzinfo else d.replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def to_int(v):
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, (int, float)):
        return int(v)
    return None


def read_snapshot():
    """('missing'|'malformed'|'ok', dict or None), one read, any parse failure = malformed."""
    try:
        with open(SNAPSHOT, encoding="utf-8") as f:
            raw = f.read()
    except FileNotFoundError:
        return "missing", None
    except OSError:
        return "malformed", None
    try:
        snap = json.loads(raw)
    except ValueError:
        return "malformed", None
    return ("ok", snap) if isinstance(snap, dict) else ("malformed", None)


def band(age):
    if age < FRESH_SECONDS:
        return "fresh"
    return "stale" if age <= DEAD_SECONDS else "dead"


def no_data(reason, remedy):
    return {"status": "no_data", "reason": reason, "remedy": remedy}


def total_secs(key):
    return 5 * 3600 if key == "five_hour" else 7 * 86400


def build_tier(key, t, now):
    util = to_int(t.get("utilization"))
    resets_at = t.get("resets_at")
    reset = parse(resets_at)
    crossed = reset is not None and reset <= now
    tier = {
        "key": key,
        "label": LABELS.get(key, key),
        "utilization_pct": None if crossed else util,
        "headroom_pct": None if crossed or util is None else max(0, 100 - util),
        "resets_at": resets_at,
        "resets_in_seconds": round(max(0.0, (reset - now).total_seconds())) if reset else None,
        "reset_crossed": crossed,
        "used": to_int(t.get("used")),
        "limit": to_int(t.get("limit")),
        "pace": None,
        "projection": None,
    }
    if crossed or reset is None or util is None:
        return tier
    total = total_secs(key)
    rem = max(0.0, (reset - now).total_seconds())
    f = min(1.0, max(0.0, (total - rem) / total))
    if f <= 0:
        return tier
    delta = util - f * 100
    wait = util / 100.0 - f
    tier["pace"] = {
        "verdict": "on_pace" if abs(delta) < 5 else ("ahead" if delta > 0 else "under"),
        "delta_pct": round(abs(delta)),
        "cooldown_seconds": round(wait * total) if wait > 0 else None,
        "surplus_pct": round(f * 100 - util) if f * 100 - util > 0 else None,
        "projected_final_pct": round(min(200.0, util / f)) if util > 0 and f > 0.01 else None,
    }
    rate = util / f
    if util > 0 and rate > 100:
        until100 = max(0.0, (100 / rate - f) * total)
        before = until100 < rem
        tier["projection"] = {"expires_before_reset": before,
                              "expires_in_seconds": round(until100) if before else None}
    else:
        tier["projection"] = {"expires_before_reset": False, "expires_in_seconds": None}
    return tier


def build_usage(now=None):
    now = now or datetime.now(timezone.utc)
    outcome, snap = read_snapshot()
    if outcome == "missing":
        return no_data("missing", "No usage snapshot. " + REMEDY_START)
    if outcome == "malformed":
        return no_data("malformed", "Usage snapshot unreadable. Restart Sanduhr; if it persists, delete " + SNAPSHOT + ".")
    try:
        schema = int(snap.get("schema_version"))
    except (TypeError, ValueError):
        return no_data("malformed", "snapshot carries no schema_version")
    if schema > SCHEMA_VERSION:
        return no_data("schema_unsupported",
                       "Snapshot schema v%d is newer than this server supports (v%d). Update the Sanduhr integrations." % (schema, SCHEMA_VERSION))
    captured = parse(snap.get("captured_at"))
    if captured is None:
        return no_data("malformed", "snapshot captured_at is unparseable")

    age = round(max(0.0, (now - captured).total_seconds()))
    b = band(age)
    file_status = snap.get("status")
    error_kind = snap.get("error_kind")
    status = "ok" if b == "fresh" and file_status != "error" else "stale"
    reason = remedy = None
    if b == "dead":
        reason, remedy = "widget_not_polling", "Snapshot is %d minutes old. %s" % (age // 60, REMEDY_START)
    elif file_status == "error":
        remedy = REMEDY_ERROR.get(error_kind, REMEDY_NETWORK)

    tiers = [build_tier(t["key"], t, now) for t in snap.get("tiers") or []
             if isinstance(t, dict) and isinstance(t.get("key"), str) and t["key"]]
    return {
        "status": status,
        "reason": reason,
        "remedy": remedy,
        "fetch_error": error_kind if file_status == "error" else None,
        "as_of": snap.get("captured_at"),
        "age_seconds": age,
        "writer_version": snap.get("writer_version"),
        "data_source": "snapshot",
        "scope": "active_account_only",
        "account": {"ref": snap.get("account_ref"), "plan": snap.get("plan")},
        "data_lag_note": DATA_LAG_NOTE,
        "schema_version": SCHEMA_VERSION,
        "tiers": tiers,
        "local_burn_since_snapshot": None,
    }


def build_ping(now=None):
    now = now or datetime.now(timezone.utc)
    outcome, snap = read_snapshot()
    age = b = None
    if outcome == "ok":
        captured = parse(snap.get("captured_at"))
        if captured is not None:
            age = round(max(0.0, (now - captured).total_seconds()))
            b = band(age)
    reason = remedy = None
    if outcome == "missing":
        status, reason, remedy = "no_data", "missing", REMEDY_START
    elif b == "dead":
        status, reason, remedy = "stale", "widget_not_polling", REMEDY_START
    elif outcome != "ok" or b is None:
        status, reason = "no_data", "malformed"
    else:
        status = "ok" if b == "fresh" and snap.get("status") != "error" else "stale"
        if snap.get("status") == "error":
            remedy = REMEDY_ERROR.get(snap.get("error_kind"), REMEDY_NETWORK)
    return {
        "server_version": SERVER_VERSION,
        "platform": "macos",
        "snapshot_schema_supported": SCHEMA_VERSION,
        "snapshot_path": SNAPSHOT,
        "snapshot_found": outcome != "missing",
        "snapshot_age_seconds": age,
        "snapshot_writer_version": snap.get("writer_version") if outcome == "ok" else None,
        "history_found": False,
        "usage_status": status,
        "usage_reason": reason,
        "remedy": remedy,
        "cc_roots_found": [],
        "cc_roots_consented": [],
        "tools_not_yet_on_mac": ["get_local_burn_by_project", "get_model_usage",
                                 "get_usage_history", "publish_usage", "propose_theme"],
    }


def write(frame):
    sys.stdout.write(json.dumps(frame, separators=(",", ":")) + "\n")  # ASCII-only by default
    sys.stdout.flush()


def result(mid, res):
    if mid is not None:
        write({"jsonrpc": "2.0", "id": mid, "result": res})


def error(mid, code, message):
    write({"jsonrpc": "2.0", "id": mid, "error": {"code": code, "message": message}})


def dispatch(msg):
    method, mid = msg.get("method"), msg.get("id")
    if method == "initialize":
        p = msg.get("params") or {}
        result(mid, {
            "protocolVersion": p.get("protocolVersion") or PROTOCOL_VERSION,
            "capabilities": {"tools": {"listChanged": False}},
            "serverInfo": {"name": "sanduhr", "title": "Sanduhr - Claude subscription usage",
                           "version": SERVER_VERSION},
        })
    elif method in ("notifications/initialized", "notifications/cancelled"):
        pass
    elif method == "ping":
        result(mid, {})
    elif method == "tools/list":
        result(mid, {"tools": TOOLS})
    elif method == "tools/call":
        name = (msg.get("params") or {}).get("name")
        builders = {"get_usage": build_usage, "ping": build_ping}
        if name not in builders:
            error(mid, -32602, "unknown tool: %s" % name)
            return
        payload = builders[name]()
        result(mid, {"content": [{"type": "text", "text": json.dumps(payload, separators=(",", ":"))}],
                     "isError": False})
    elif mid is not None:
        error(mid, -32601, "method not found: %s" % method)


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except ValueError:
            error(None, -32700, "parse error")
            continue
        if isinstance(msg, dict):
            dispatch(msg)


if __name__ == "__main__":
    main()

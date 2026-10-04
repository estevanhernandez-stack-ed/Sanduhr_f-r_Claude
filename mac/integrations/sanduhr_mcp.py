#!/usr/bin/env python3
"""sanduhr-mcp for macOS: Claude subscription headroom and Claude Code usage for agents.

Mac port of windows-dotnet/src/Sanduhr.Mcp (stdio JSON-RPC, one message per line).
Same server name, tool names and response shapes as Windows, so an agent calls the
tools the same way on either machine:

  get_usage                  tiers with utilization, headroom, reset countdown, pace and
                             projection, plus local Claude Code burn since the snapshot
  get_local_burn_by_project  local Claude Code tokens by project over 1, 7 or 30 days
  get_model_usage            local Claude Code tokens by model, joined with the weekly meters
  get_usage_history          daily tokens from Sanduhr's record (the vault), and the
                             meters' daily peaks from their history
  ping                       health: snapshot, sharing summary, the tools on this Mac

What each tool may read is decided per account in Sanduhr (Settings, Accounts, Data, Share
with Claude) and handed over in ~/Library/Application Support/Sanduhr/mcp-access.json. No
file, an unreadable one or an unknown schema means nothing is shared. Off: the account does
not exist here. Meters: get_usage and the meter history. Meters and activity: also the
burn, model and record tools for the account's linked Claude Code folder, with project
names as the account chose (Hidden returns the record's short code, never a name).

Read-only: opens snapshot.json, mcp-access.json, the history files and vault folders it
names, and the session logs under a folder it names. Never reads Sanduhr's settings, the
Keychain or account names; never calls claude.ai or anything else on the network; never
writes a file; never logs. No tool takes a path. Failures are typed results
(status/reason/remedy), never protocol errors. Python 3.9 standard library only.

publish_usage is dropped on the Mac (nothing leaves this Mac); propose_theme is not on Mac.
"""
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timedelta, timezone

SERVER_VERSION = "0.2.0-mac"
PROTOCOL_VERSION = "2025-06-18"
SCHEMA_VERSION = 1
ACCESS_SCHEMA_VERSION = 1
FRESH_SECONDS = 450
DEAD_SECONDS = 900
DATA_LAG_NOTE = "claude.ai's own numbers lag consumption by several minutes"
BURN_CAVEAT = (
    "token-count proxy from local Claude Code logs (input+output only, cache excluded); "
    "NOT convertible to utilization %. CC deletes session logs after ~30 days; totals are lower bounds.")
HISTORY_CAVEAT = (
    "Vault rollups cover days the widget has ingested; today may be partial or absent - "
    "use get_usage for live burn. Absent days are no-record, never zero. " + BURN_CAVEAT)
METER_HISTORY_NOTE = (
    "meter_history is each shared account's highest utilization per local day from Sanduhr's "
    "meter history (kept 30 days); days without a reading are omitted.")
LABELS = {
    "five_hour": "Session (5hr)",
    "seven_day": "Weekly - All Models",
    "seven_day_sonnet": "Weekly - Sonnet",
    "seven_day_opus": "Weekly - Opus",
    "seven_day_fable": "Weekly - Fable",
    "seven_day_cowork": "Weekly - Cowork",
    "seven_day_omelette": "Weekly - Design",
    "seven_day_oauth_apps": "Weekly - OAuth Apps",
    "iguana_necktie": "Weekly - Special",
}
TIER_ORDER = list(LABELS)
# More specific prefixes first (CcLogReader.TierForModel). Haiku folds into the weekly tier.
MODEL_TIER_PREFIXES = [
    ("claude-opus", "seven_day_opus"),
    ("claude-sonnet", "seven_day_sonnet"),
    ("claude-fable", "seven_day_fable"),
    ("claude-haiku", "seven_day"),
]
WORKTREE_MARKERS = [[".claude", "worktrees"], [".worktrees"]]

REMEDY_START = "Start Sanduhr (it writes the snapshot after every fetch, every 5 minutes)."
REMEDY_ERROR = {
    "session_expired": "Sanduhr's claude.ai session expired - sign in again in Sanduhr (Settings > Credentials). Tiers below are last-good, not current.",
    "cloudflare": "Sanduhr is blocked by a Cloudflare challenge - paste a fresh cf_clearance in Sanduhr. Tiers below are last-good, not current.",
}
REMEDY_NETWORK = "Sanduhr's last fetch failed (network). Tiers below are last-good, not current."
SETTINGS_PLACE = "Sanduhr's Settings > Accounts > Data"
REMEDY_NOT_SHARED = {
    "missing": "Nothing is shared with Claude. Choose Share with Claude for an account in " + SETTINGS_PLACE + " (Sanduhr writes the choices for this server to mcp-access.json).",
    "unreadable": "Sanduhr's sharing file (mcp-access.json) could not be read, so nothing is shared. Change any Share with Claude choice in " + SETTINGS_PLACE + " to rewrite it.",
    "schema_unsupported": "Sanduhr's sharing file is newer than this server understands, so nothing is shared. Update the Sanduhr integrations (bash mac/integrations/install.sh).",
    "none": "No account is shared with Claude. Choose Share with Claude for an account in " + SETTINGS_PLACE + ".",
}
REMEDY_ACTIVE_NOT_SHARED = (
    "The account Sanduhr shows is not shared with Claude. Set its Share with Claude to Meters "
    "(or Meters and activity) in " + SETTINGS_PLACE + ".")
REMEDY_NO_ACTIVITY = (
    "No account shares Claude Code activity from a folder Sanduhr reads. In " + SETTINGS_PLACE +
    ": link the account's Claude Code folder, set Claude Code activity to Live only or Keep a "
    "record, and Share with Claude to Meters and activity.")

READ_ONLY = {"readOnlyHint": True, "destructiveHint": False, "openWorldHint": False}
NO_ARGS = {"type": "object", "properties": {}, "additionalProperties": False}
TOOLS = [
    {
        "name": "get_usage",
        "description": (
            "Check Claude subscription quota headroom (the Sanduhr widget's live snapshot of "
            "claude.ai usage, plus local Claude Code burn since that snapshot when the account "
            "shares its activity). Call BEFORE spawning subagents, launching long autonomous runs, "
            "or choosing a bigger model for a large job. Reflects the Sanduhr widget's ACTIVE "
            "account, which may not be the account this session bills to - confirm with the user "
            "if they run multiple accounts. Answers only when the user shares that account with "
            "Claude in Sanduhr. A stale, no_data or not_shared result means unknown headroom - "
            "never assume budget."),
        "inputSchema": NO_ARGS,
        "annotations": READ_ONLY,
    },
    {
        "name": "get_local_burn_by_project",
        "description": (
            "Attribute recent local Claude Code token burn to projects: which project ate the "
            "tokens, keyed per shared account (its account_ref). Attribution only - token counts "
            "are a proxy (input+output, cache excluded) and are NOT convertible to quota "
            "percentages; use get_usage for headroom. Scoped to the accounts the user shares "
            "activity for in Sanduhr; every response names the accounts it covered. Project "
            "names follow each account's choice: Hidden returns a short code per project."),
        "inputSchema": {
            "type": "object",
            "properties": {
                "window_days": {"type": "integer", "enum": [1, 7, 30],
                                "description": "Lookback window. Default 7."},
                "full_paths": {"type": "boolean",
                               "description": "Return full project paths instead of basenames, for accounts whose project names are set to Full paths. Default false."},
            },
            "additionalProperties": False,
        },
        "annotations": READ_ONLY,
    },
    {
        "name": "get_model_usage",
        "description": (
            "Which models are burning the budget: per-model local Claude Code token totals over "
            "a window, joined with each model's weekly meter utilization from the live snapshot. "
            "Use when choosing between models for a big job - a model whose per-model weekly "
            "meter is hot is the one to avoid. Token counts are a proxy (input+output, cache "
            "excluded), never convertible to quota %. Scoped to the accounts the user shares "
            "activity for in Sanduhr."),
        "inputSchema": {
            "type": "object",
            "properties": {
                "window_days": {"type": "integer", "enum": [1, 7, 30],
                                "description": "Lookback window. Default 7."},
            },
            "additionalProperties": False,
        },
        "annotations": READ_ONLY,
    },
    {
        "name": "get_usage_history",
        "description": (
            "Daily local Claude Code usage history from Sanduhr's durable record (survives Claude "
            "Code's ~30-day log cleanup): tokens per day with sent/received split, window totals, "
            "top projects; plus each shared account's daily meter peaks from Sanduhr's meter "
            "history. Trend and attribution data, not quota - use get_usage for headroom. Days "
            "without a record are omitted, never fabricated as zero. Scoped to the accounts the "
            "user shares in Sanduhr (the record needs Meters and activity and Keep a record)."),
        "inputSchema": {
            "type": "object",
            "properties": {
                "window_days": {"type": "integer", "enum": [7, 30, 90],
                                "description": "Lookback window in days. Default 30."},
            },
            "additionalProperties": False,
        },
        "annotations": READ_ONLY,
    },
    {
        "name": "ping",
        "description": (
            "Health check and verify anchor: server version, snapshot presence and age, what the "
            "user shares with Claude in Sanduhr (counts only), and the tools on this Mac. Call "
            "this first when get_usage returns no_data or not_shared, to distinguish "
            "server-broken from data-absent from not-shared."),
        "inputSchema": NO_ARGS,
        "annotations": READ_ONLY,
    },
]
TOOL_NAMES = [t["name"] for t in TOOLS]
TOOLS_NOT_ON_MAC = {
    "publish_usage": "dropped on Mac: nothing leaves this Mac",
    "propose_theme": "not on Mac",
}


class Paths:
    """Where the server reads. SANDUHR_SUPPORT_DIR (tests, a test folder) moves everything;
    SANDUHR_SNAPSHOT moves the snapshot alone, as before."""

    def __init__(self, support_dir=None, snapshot=None):
        self.support_dir = support_dir or os.environ.get("SANDUHR_SUPPORT_DIR") or os.path.expanduser(
            "~/Library/Application Support/Sanduhr")
        self.snapshot = snapshot or os.environ.get("SANDUHR_SNAPSHOT") or os.path.join(
            self.support_dir, "snapshot.json")
        self.access = os.path.join(self.support_dir, "mcp-access.json")
        self.vault = os.path.join(self.support_dir, "vault")

    def history_file(self, name):
        return os.path.join(self.support_dir, name)


# -- time and numbers ------------------------------------------------------------------------

def parse(ts):
    if not ts or not isinstance(ts, str):
        return None
    try:
        s = ts.strip().replace("Z", "+00:00").replace("z", "+00:00")
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


def iso_o(t):
    """.NET's round-trip "o" format for a UTC instant: seven fraction digits, +00:00."""
    t = t.astimezone(timezone.utc)
    return t.strftime("%Y-%m-%dT%H:%M:%S.") + "%06d0+00:00" % t.microsecond


def to_int(v):
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, (int, float)):
        return int(v)
    return None


def number(v):
    """A token count as Windows reads it: integers as is, fractions truncated, else 0."""
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        return 0
    if isinstance(v, float) and (v != v or abs(v) >= 9.2e18):
        return 0
    return int(v)


def local_date(t):
    return t.astimezone().date()


def no_data(reason, remedy):
    return {"status": "no_data", "reason": reason, "remedy": remedy}


# -- the access file ------------------------------------------------------------------------

REF_RE = re.compile(r"^[0-9a-f]{8}$")
VAULT_ID_RE = re.compile(r"^[0-9a-f]{16}$")
HISTORY_RE = re.compile(r"^history\.[^/\\\x00]+\.json$")


class Account:
    __slots__ = ("ref", "active", "share", "history_file", "names", "vault_id", "live_folder")

    def __init__(self, ref, active, share, history_file, names, vault_id, live_folder):
        self.ref = ref
        self.active = active
        self.share = share
        self.history_file = history_file
        self.names = names
        self.vault_id = vault_id
        self.live_folder = live_folder


def _clean_folder(v):
    if not isinstance(v, str) or not v or "\x00" in v or not v.startswith("/"):
        return None
    if os.path.normpath(v) != v.rstrip("/") or "/../" in v + "/":
        return None
    return v.rstrip("/") or None


def read_access(paths):
    """('ok'|'missing'|'unreadable'|'schema_unsupported', [Account]). Anything but 'ok' shares
    nothing. Entries that don't validate are dropped one by one; the first entry for an
    account_ref wins."""
    try:
        with open(paths.access, encoding="utf-8") as f:
            raw = f.read()
    except FileNotFoundError:
        return "missing", []
    except (OSError, UnicodeDecodeError):
        return "unreadable", []
    try:
        doc = json.loads(raw)
    except ValueError:
        return "unreadable", []
    if not isinstance(doc, dict):
        return "unreadable", []
    schema = doc.get("schema_version")
    if isinstance(schema, bool) or not isinstance(schema, int):
        return "unreadable", []
    if schema != ACCESS_SCHEMA_VERSION:
        return "schema_unsupported", []
    rows = doc.get("accounts")
    if not isinstance(rows, list):
        return "unreadable", []
    out, seen = [], set()
    for row in rows:
        if not isinstance(row, dict):
            continue
        ref, share = row.get("account_ref"), row.get("share")
        if not isinstance(ref, str) or not REF_RE.match(ref) or ref in seen:
            continue
        if share not in ("meters", "activity"):
            continue
        hist = row.get("history_file")
        hist = hist if isinstance(hist, str) and HISTORY_RE.match(hist) else None
        names = vault_id = folder = None
        if share == "activity":
            names = row.get("names")
            if names not in ("names", "hidden", "full"):
                names = "hidden"     # unknown: the safest reading
            v = row.get("vault_id")
            vault_id = v if isinstance(v, str) and VAULT_ID_RE.match(v) else None
            folder = _clean_folder(row.get("live_folder"))
        seen.add(ref)
        out.append(Account(ref, row.get("active") is True, share, hist, names, vault_id, folder))
    return "ok", out


def not_shared(status):
    return no_data("not_shared", REMEDY_NOT_SHARED.get(status, REMEDY_NOT_SHARED["none"]))


# -- snapshot ------------------------------------------------------------------------------

def read_snapshot(paths):
    """('missing'|'malformed'|'ok', dict or None), one read, any parse failure = malformed."""
    try:
        with open(paths.snapshot, encoding="utf-8") as f:
            raw = f.read()
    except FileNotFoundError:
        return "missing", None
    except (OSError, UnicodeDecodeError):
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


def snapshot_account(snap, accounts):
    """The shared account whose meters the snapshot holds, or None."""
    ref = snap.get("account_ref") if isinstance(snap, dict) else None
    if not isinstance(ref, str):
        return None
    return next((a for a in accounts if a.ref == ref), None)


# -- Claude Code session logs (CcLogReader, per folder) --------------------------------------

def tier_for_model(model):
    if not isinstance(model, str) or not model:
        return None
    for prefix, tier in MODEL_TIER_PREFIXES:
        if model.startswith(prefix):
            return tier
    return None


def log_files(folder):
    """Session JSONLs under one folder's projects/, nested transcripts included; files lying
    directly in projects/ are not sessions."""
    projects = os.path.join(folder, "projects")
    try:
        names = sorted(os.listdir(projects))
    except OSError:
        return []
    out = []
    for name in names:
        d = os.path.join(projects, name)
        if not os.path.isdir(d):
            continue
        for base, dirs, files in os.walk(d):
            dirs.sort()
            for f in sorted(files):
                if f.endswith(".jsonl"):
                    out.append(os.path.join(base, f))
    return out


def iter_events(path):
    """(timestamp, model, tokens, cwd) per assistant line with usage. Malformed lines skip."""
    try:
        f = open(path, "rb")
    except OSError:
        return
    with f:
        for line in f:
            if b'"assistant"' not in line or b'"usage"' not in line:
                continue
            try:
                d = json.loads(line)
            except ValueError:
                continue
            if not isinstance(d, dict) or d.get("type") != "assistant":
                continue
            msg = d.get("message")
            usage = msg.get("usage") if isinstance(msg, dict) else None
            if not isinstance(usage, dict):
                continue
            model = msg.get("model")
            cwd = d.get("cwd")
            yield (parse(d.get("timestamp")),
                   model if isinstance(model, str) else None,
                   number(usage.get("input_tokens")) + number(usage.get("output_tokens")),
                   cwd if isinstance(cwd, str) else None)


def events_since(folder, since, counter=None):
    cutoff = since.timestamp()
    for path in log_files(folder):
        try:
            if os.stat(path).st_mtime < cutoff:
                continue
        except OSError:
            pass   # unreadable mtime: scan the file rather than skip data
        if counter is not None:
            counter[0] += 1
        for ts, model, tokens, cwd in iter_events(path):
            if ts is not None and ts >= since:
                yield ts, model, tokens, cwd


def holds_git(d):
    """Whether an absolute folder holds a .git entry (a folder, or a file in a linked worktree)."""
    return d.startswith("/") and os.path.exists(d + "/.git")


def project_display_name(cwd, dir_holds_git=holds_git):
    """ProjectDisplayName: the worktree's repo, else the nearest enclosing repo, else the
    basename."""
    if not cwd:
        return ""
    parts = cwd.replace("\\", "/").rstrip("/").split("/")
    for i in range(1, len(parts)):
        hit = False
        for marker in WORKTREE_MARKERS:
            if i + len(marker) >= len(parts):
                continue
            if all(parts[i + k].lower() == m for k, m in enumerate(marker)):
                hit = True
                break
        if hit:
            if parts[i - 1]:
                return parts[i - 1]
            break
    depth = len(parts)
    while depth > 1:
        name = parts[depth - 1]
        if name and dir_holds_git("/".join(parts[:depth])):
            return name
        depth -= 1
    last = parts[-1] if parts else cwd
    return last or cwd


def hidden_name(name):
    """The record's code for a project under Hidden (VaultHiddenName): p- and 10 hex digits of
    the SHA-256 of the folded name. Only the code leaves this process."""
    return "p-" + hashlib.sha256(name.encode("utf-8")).hexdigest()[:10]


def project_label(cwd, names, full_paths, dir_holds_git=holds_git):
    if not cwd:
        return "(unknown)"
    if names == "full" and full_paths:
        return cwd
    name = project_display_name(cwd, dir_holds_git)
    return hidden_name(name) if names == "hidden" else name


def activity_accounts(accounts):
    return [a for a in accounts if a.share == "activity" and a.live_folder]


def burn_since(account, since):
    """local_burn_since_snapshot for one account's folder."""
    total, by_tier = 0, {}
    for _, model, tokens, _ in events_since(account.live_folder, since):
        if tokens <= 0:
            continue
        total += tokens
        tier = tier_for_model(model)
        if tier:
            by_tier[tier] = by_tier.get(tier, 0) + tokens
    ordered = dict(sorted(by_tier.items(), key=lambda p: -p[1]))
    return {"total_tokens": total, "by_tier": ordered, "caveat": BURN_CAVEAT}


# -- the vault and the meter history -------------------------------------------------------

def months_between(first, last):
    y, m = first.year, first.month
    out = []
    while (y, m) <= (last.year, last.month):
        out.append("%04d-%02d" % (y, m))
        y, m = (y + 1, 1) if m == 12 else (y, m + 1)
    return out


def read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError, UnicodeDecodeError):
        return None


def project_name_of(key):
    """The display name in a project_key (api~3f2a91cc is api); Hidden keys have no ~."""
    i = key.rfind("~")
    return key[:i] if i > 0 else key


def read_vault_window(paths, vault_ids, first, last):
    by_day, by_in, by_out, by_project = {}, {}, {}, {}
    for vid in vault_ids:
        for month in months_between(first, last):
            doc = read_json(os.path.join(paths.vault, vid, "rollups-%s.json" % month))
            days = doc.get("days") if isinstance(doc, dict) else None
            if not isinstance(days, dict):
                continue
            for key, day in days.items():
                try:
                    date = datetime.strptime(key, "%Y-%m-%d").date()
                except (TypeError, ValueError):
                    continue
                if not (first <= date <= last) or not isinstance(day, dict):
                    continue
                by_day[date] = by_day.get(date, 0) + number(day.get("total"))
                by_in[date] = by_in.get(date, 0) + number(day.get("input"))
                by_out[date] = by_out.get(date, 0) + number(day.get("output"))
                projects = day.get("by_project")
                if isinstance(projects, dict):
                    for k, v in projects.items():
                        name = project_name_of(k)
                        by_project[name] = by_project.get(name, 0) + number(v)
    return by_day, by_in, by_out, by_project


def read_meter_history(paths, account, first, last):
    """Per tier, the highest reading per local day in [first, last]."""
    if not account.history_file:
        return []
    doc = read_json(paths.history_file(account.history_file))
    if not isinstance(doc, dict):
        return []
    tiers = []
    keys = sorted(doc, key=lambda k: (TIER_ORDER.index(k) if k in TIER_ORDER else len(TIER_ORDER), k))
    for key in keys:
        series = doc[key]
        if not isinstance(key, str) or not key or not isinstance(series, list):
            continue
        peaks = {}
        for p in series:
            if not isinstance(p, dict):
                continue
            t, v = parse(p.get("t")), p.get("v")
            if t is None or isinstance(v, bool) or not isinstance(v, (int, float)):
                continue
            day = local_date(t)
            if first <= day <= last:
                peaks[day] = max(peaks.get(day, v), v)
        if peaks:
            tiers.append({"key": key, "label": LABELS.get(key, key),
                          "days": [{"date": d.isoformat(), "peak_pct": round(peaks[d])} for d in sorted(peaks)]})
    return tiers


# -- tools -------------------------------------------------------------------------------

def build_usage(now=None, paths=None):
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    access, accounts = read_access(paths)
    if access != "ok" or not accounts:
        return not_shared(access if access != "ok" else "none")
    outcome, snap = read_snapshot(paths)
    if outcome == "missing":
        return no_data("missing", "No usage snapshot. " + REMEDY_START)
    if outcome == "malformed":
        return no_data("malformed", "Usage snapshot unreadable. Restart Sanduhr; if it persists, delete " + paths.snapshot + ".")
    account = snapshot_account(snap, accounts)
    if account is None:
        return no_data("not_shared", REMEDY_ACTIVE_NOT_SHARED)
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
    burn = None
    if account.share == "activity" and account.live_folder:
        burn = burn_since(account, captured)
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
        "account": {"ref": account.ref, "plan": snap.get("plan")},
        "data_lag_note": DATA_LAG_NOTE,
        "schema_version": SCHEMA_VERSION,
        "tiers": tiers,
        "local_burn_since_snapshot": burn,
    }


def window_arg(args, default):
    v = (args or {}).get("window_days", default)
    if isinstance(v, bool) or not isinstance(v, int):
        return -1   # non-integer -> typed invalid_params
    return v


def build_burn(window_days=7, full_paths=False, now=None, paths=None, dir_holds_git=holds_git):
    if window_days not in (1, 7, 30):
        return no_data("invalid_params", "window_days must be 1, 7, or 30")
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    access, accounts = read_access(paths)
    if access != "ok" or not accounts:
        return not_shared(access if access != "ok" else "none")
    scanned = activity_accounts(accounts)
    if not scanned:
        return no_data("disabled", REMEDY_NO_ACTIVITY)
    since = now - timedelta(days=window_days)
    files = [0]
    roots = []
    for a in scanned:
        total, by_project = 0, {}
        for _, _, tokens, cwd in events_since(a.live_folder, since, files):
            if tokens <= 0:
                continue
            project = project_label(cwd, a.names, full_paths, dir_holds_git)
            total += tokens
            by_project[project] = by_project.get(project, 0) + tokens
        roots.append({
            "root": a.ref,
            "active": a.active,
            "names": "full" if a.names == "full" and full_paths else a.names,
            "total_tokens": total,
            "projects": [{"name": n, "tokens": t} for n, t in sorted(by_project.items(), key=lambda p: -p[1])],
        })
    return {
        "status": "ok",
        "reason": None,
        "remedy": None,
        "window_days": window_days,
        "since": iso_o(since),
        "full_paths": bool(full_paths),
        "roots_scanned": [a.ref for a in scanned],
        "roots": roots,
        "files_scanned": files[0],
        "caveat": BURN_CAVEAT,
    }


def build_model_usage(window_days=7, now=None, paths=None):
    if window_days not in (1, 7, 30):
        return no_data("invalid_params", "window_days must be 1, 7, or 30")
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    access, accounts = read_access(paths)
    if access != "ok" or not accounts:
        return not_shared(access if access != "ok" else "none")
    scanned = activity_accounts(accounts)
    if not scanned:
        return no_data("disabled", REMEDY_NO_ACTIVITY)
    since = now - timedelta(days=window_days)
    total, by_model = 0, {}
    for a in scanned:
        for _, model, tokens, _ in events_since(a.live_folder, since):
            if tokens <= 0:
                continue
            m = model or "(unknown)"
            total += tokens
            by_model[m] = by_model.get(m, 0) + tokens

    # The meter join: only the snapshot of an account whose activity is in the totals, so one
    # account's tokens are never set beside another account's meter.
    meter_by_tier = meter_source = None
    outcome, snap = read_snapshot(paths)
    owner = snapshot_account(snap, scanned) if outcome == "ok" else None
    captured = parse(snap.get("captured_at")) if owner else None
    if owner and captured:
        meter_by_tier = {}
        for t in snap.get("tiers") or []:
            if not isinstance(t, dict) or not isinstance(t.get("key"), str) or not t["key"]:
                continue
            u = to_int(t.get("utilization"))
            r = parse(t.get("resets_at"))
            if u is not None and not (r is not None and r <= now):
                meter_by_tier[t["key"]] = u
        meter_source = {"status": snap.get("status"), "as_of": snap.get("captured_at"),
                        "age_seconds": round(max(0.0, (now - captured).total_seconds())),
                        "account_ref": owner.ref}

    models = []
    for model, tokens in sorted(by_model.items(), key=lambda p: -p[1]):
        tier = tier_for_model(model)
        models.append({
            "model": model,
            "tokens": tokens,
            "share_pct": round(tokens * 100.0 / total) if total > 0 else 0,
            "tier_key": tier,
            "tier_label": LABELS.get(tier) if tier else None,
            "meter_utilization_pct": meter_by_tier.get(tier) if tier and meter_by_tier is not None else None,
        })
    return {
        "status": "ok",
        "reason": None,
        "remedy": None,
        "window_days": window_days,
        "since": iso_o(since),
        "roots_scanned": [a.ref for a in scanned],
        "total_tokens": total,
        "models": models,
        "meter_source": meter_source,
        "caveat": BURN_CAVEAT,
    }


def build_history(window_days=30, now=None, paths=None):
    if window_days not in (7, 30, 90):
        return no_data("invalid_params", "window_days must be 7, 30, or 90")
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    access, accounts = read_access(paths)
    if access != "ok" or not accounts:
        return not_shared(access if access != "ok" else "none")
    today = local_date(now)
    first = today - timedelta(days=window_days - 1)
    recorded = [a for a in accounts if a.share == "activity" and a.vault_id]
    by_day, by_in, by_out, by_project = read_vault_window(paths, [a.vault_id for a in recorded], first, today)
    meters = []
    for a in accounts:
        tiers = read_meter_history(paths, a, first, today)
        if tiers:
            meters.append({"account_ref": a.ref, "active": a.active, "tiers": tiers})
    if not by_day and not meters:
        return no_data("missing",
                       "No record or meter history for the shared accounts. Keep a record of Claude Code "
                       "activity (the vault) and keep Meter history in " + SETTINGS_PLACE + ", and let Sanduhr run.")
    total = 0
    days = []
    for date in sorted(by_day):
        total += by_day[date]
        days.append({"date": date.isoformat(), "tokens": by_day[date],
                     # 0/0 alongside a nonzero total is WS-C-era legacy (unsplit), not zero traffic.
                     "sent": by_in.get(date, 0), "received": by_out.get(date, 0)})
    top = sorted(by_project.items(), key=lambda p: -p[1])[:5]
    return {
        "status": "ok",
        "reason": None,
        "remedy": None,
        "window_days": window_days,
        "from": first.isoformat(),
        "to": today.isoformat(),
        "roots_scanned": [a.ref for a in recorded],
        "total_tokens": total,
        "days_recorded": len(days),
        "days": days,
        "top_projects": [{"name": n, "tokens": t} for n, t in top],
        "meter_history": meters,
        "caveat": HISTORY_CAVEAT + " " + METER_HISTORY_NOTE,
    }


def build_ping(now=None, paths=None):
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    access, accounts = read_access(paths)
    outcome, snap = read_snapshot(paths)
    age = b = None
    if outcome == "ok":
        captured = parse(snap.get("captured_at"))
        if captured is not None:
            age = round(max(0.0, (now - captured).total_seconds()))
            b = band(age)
    owner = snapshot_account(snap, accounts) if outcome == "ok" else None
    reason = remedy = None
    if access != "ok" or not accounts:
        status, reason = "no_data", "not_shared"
        remedy = REMEDY_NOT_SHARED.get(access if access != "ok" else "none")
    elif outcome == "missing":
        status, reason, remedy = "no_data", "missing", REMEDY_START
    elif outcome == "ok" and owner is None:
        status, reason, remedy = "no_data", "not_shared", REMEDY_ACTIVE_NOT_SHARED
    elif b == "dead":
        status, reason, remedy = "stale", "widget_not_polling", REMEDY_START
    elif outcome != "ok" or b is None:
        status, reason = "no_data", "malformed"
    else:
        status = "ok" if b == "fresh" and snap.get("status") != "error" else "stale"
        if snap.get("status") == "error":
            remedy = REMEDY_ERROR.get(snap.get("error_kind"), REMEDY_NETWORK)
    active = next((a for a in accounts if a.active), None)
    shown = owner or active
    history_found = bool(shown and shown.history_file
                         and os.path.isfile(paths.history_file(shown.history_file)))
    return {
        "server_version": SERVER_VERSION,
        "platform": "macos",
        "snapshot_schema_supported": SCHEMA_VERSION,
        "snapshot_path": paths.snapshot,
        "snapshot_found": outcome != "missing",
        "snapshot_age_seconds": age,
        "snapshot_writer_version": snap.get("writer_version") if outcome == "ok" else None,
        "history_found": history_found,
        "usage_status": status,
        "usage_reason": reason,
        "remedy": remedy,
        "cc_roots_consented": [a.ref for a in activity_accounts(accounts)],
        "sharing": {
            "access_file": access,
            "access_schema_supported": ACCESS_SCHEMA_VERSION,
            "accounts_shared": len(accounts),
            "meters_only": sum(1 for a in accounts if a.share == "meters"),
            "meters_and_activity": sum(1 for a in accounts if a.share == "activity"),
            "activity_read": len(activity_accounts(accounts)),
            "records_shared": sum(1 for a in accounts if a.share == "activity" and a.vault_id),
            "active_account": active.share if active else "off",
        },
        "tools_available": TOOL_NAMES,
        "tools_not_on_mac": TOOLS_NOT_ON_MAC,
    }


# -- protocol ----------------------------------------------------------------------------

def write(frame):
    sys.stdout.write(json.dumps(frame, separators=(",", ":")) + "\n")  # ASCII-only by default
    sys.stdout.flush()


def result(mid, res):
    if mid is not None:
        write({"jsonrpc": "2.0", "id": mid, "result": res})


def error(mid, code, message):
    write({"jsonrpc": "2.0", "id": mid, "error": {"code": code, "message": message}})


def call_tool(name, args):
    """The payload for a tools/call, or None for an unknown tool."""
    args = args if isinstance(args, dict) else {}
    if name == "get_usage":
        return build_usage()
    if name == "get_local_burn_by_project":
        full = args.get("full_paths", False)
        return build_burn(window_arg(args, 7), full if isinstance(full, bool) else False)
    if name == "get_model_usage":
        return build_model_usage(window_arg(args, 7))
    if name == "get_usage_history":
        return build_history(window_arg(args, 30))
    if name == "ping":
        return build_ping()
    return None


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
        params = msg.get("params") or {}
        name = params.get("name")
        payload = call_tool(name, params.get("arguments"))
        if payload is None:
            error(mid, -32602, "unknown tool")
            return
        result(mid, {"content": [{"type": "text", "text": json.dumps(payload, separators=(",", ":"))}],
                     "isError": False})
    elif mid is not None:
        error(mid, -32601, "method not found")


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

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
  get_desk_messages          the Desk's messages.txt lines, the pin and rotation, today's line
  propose_desk_messages      suggests Desk message lines; Sanduhr asks the user (or applies them
                             when the user lets Claude change the messages directly)
  propose_theme              suggests a widget color theme; Sanduhr asks the user (or saves and
                             applies it when the user lets Claude change themes directly)
  watch_start, watch_update, watch_end
                             a live watcher card on the notch or the Desk for work in flight (a CI
                             run, a deploy), when the user lets agents show watchers

What each tool may read is decided per account in Sanduhr (Settings, Accounts, Data, Share
with Claude) and handed over in ~/Library/Application Support/Sanduhr/mcp-access.json. No
file, an unreadable one or an unknown schema means nothing is shared. Off: the account does
not exist here. Meters: get_usage and the meter history. Meters and activity: also the
burn, model and record tools for the account's linked Claude Code folder, with project
names as the account chose (Hidden returns the record's short code, never a name).

Reads snapshot.json, mcp-access.json, the history files and vault folders it names, the
session logs under a folder it names, Desk's messages.txt and desk-messages-state.json (the pin
and rotation, written by Sanduhr). Desk messages are not gated by Share with your agents: they are on
the desktop already, and a proposal only asks (Sanduhr checks it again and the user approves,
unless they chose to let Claude change the messages directly). Never reads Sanduhr's settings,
the Keychain or account names; never calls claude.ai or anything else on the network; never
logs. The files it writes are desk-messages-request.json (propose_desk_messages),
theme-request.json (propose_theme) and watch-request-<ms>-<seq>-<id>.json (the watch tools, only
while watchers.json, written by Sanduhr, says agents may show watchers), atomically and
owner-only; it never writes messages.txt or a theme. No tool takes a path. Failures are typed results (status/reason/remedy), never protocol
errors. Python 3.9 standard library only.

publish_usage is dropped on the Mac (nothing leaves this Mac).
"""
import hashlib
import json
import math
import os
import re
import sys
import time
import uuid
from datetime import datetime, timedelta, timezone

SERVER_VERSION = "0.5.0-mac"
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
    "missing": "Nothing is shared with your agents. Choose Share with your agents for an account in " + SETTINGS_PLACE + " (Sanduhr writes the choices for this server to mcp-access.json).",
    "unreadable": "Sanduhr's sharing file (mcp-access.json) could not be read, so nothing is shared. Change any Share with your agents choice in " + SETTINGS_PLACE + " to rewrite it.",
    "schema_unsupported": "Sanduhr's sharing file is newer than this server understands, so nothing is shared. Update the Sanduhr integrations (bash mac/integrations/install.sh).",
    "none": "No account is shared with your agents. Choose Share with your agents for an account in " + SETTINGS_PLACE + ".",
}
REMEDY_ACTIVE_NOT_SHARED = (
    "The account Sanduhr shows is not shared with your agents. Set its Share with your agents to Meters "
    "(or Meters and activity) in " + SETTINGS_PLACE + ".")
REMEDY_NO_ACTIVITY = (
    "No account shares Claude Code activity from a folder Sanduhr reads. In " + SETTINGS_PLACE +
    ": link the account's Claude Code folder, set Claude Code activity to Live only or Keep a "
    "record, and Share with your agents to Meters and activity.")

DESK_SYNTAX = (
    "messages.txt syntax, one line each: a plain line shows on any day; 'Mon: text' only on that "
    "weekday (Mon Tue Wed Thu Fri Sat Sun, exactly so); '10-31: text' only on that date (MM-DD); "
    "'# text' is a comment and blank lines are ignored. Each day the Desk shows one usual line: "
    "today's weekday lines if there are any (they replace the plain lines that day, unless the "
    "user's mix_daily switch is on, which lets the plain lines take turns with them), else the "
    "plain lines; it rotates through them once a day (a weekday's lines once a week) or once an "
    "hour (rotate, the user's choice), steady in between. Date lines are special days: they do not "
    "replace the usual line, they add to it, drawn above it, and every line for that date shows "
    "(two birthdays on one date both show; more than 3 take turns hourly, 3 at a time). So "
    "birthdays, anniversaries and holidays are date lines, one per person or occasion, short and "
    "friendly, like '03-14: {ink:#ff7e5f,#feb47b} happy birthday, Sam.'; the user's everyday line "
    "still shows under them. Pinning one line is a user setting (pinned): the pinned line replaces "
    "the usual line every day, so weekday and plain lines don't show while it is pinned (say so "
    "before proposing them), but date lines still stack above the pinned line, so birthdays and "
    "holidays show either way. ")
DESK_EFFECTS = (
    "Effects: tags at the start of the text, after any prefix, in any order, each in braces: "
    "{ink:#ff2a6d,#05d9e8} this line's ink, 1 to 4 hex colors (2 or more make a left-to-right "
    "gradient); {glow} / {noglow} turn the soft glow on or off for this line; {size:1.3} scales "
    "the line from 0.5 to 2 times the Desk's message size; {write} draws the line in, left to right "
    "like handwriting, once when it first appears; {shimmer} sends a slow light sweep across it "
    "every few seconds; {sweep} runs a three-character light across the line that brightens it "
    "toward white, once when the line appears, and {sweep:20} every 20 seconds (2 to 3600); "
    "{font:small-caps} draws the line in a letter style: bold, italic, bold-italic and small-caps "
    "in the Desk's own handwriting (its Bold face, a slant, capitals at x-height size), sans, mono, "
    "double-struck, script and fraktur as Unicode math letters (A to Z, a to z and, where the "
    "style has them, digits; accents and punctuation stay as written) in the system font. "
    "Examples: '{ink:#ffd08a} showtime.', 'Fri: {ink:#ff2a6d,#05d9e8} {glow} "
    "ship it.', '10-31: {ink:#ff7518,#6b2fa0} {write} happy halloween.', '{size:0.8} {noglow} "
    "breathe.', '{font:small-caps} {sweep} showtime.', '{font:script} {sweep:30} good morning.'. "
    "Good taste: short lines (handwriting reads best under about 40 characters, two "
    "lines at most on screen), lowercase and a period suit the hand, {write}, {shimmer} and {sweep} "
    "sparingly (a few lines, not every line, and one of them per line), {font:...} on short "
    "lines, gradients with 2 or 3 colors that sit near each other, sizes near 1. Reduce Motion "
    "shows {write} at once and skips {shimmer} and {sweep}. ")
DESK_GUIDE_READ = (
    "Read the user's Desk messages: the handwritten line Sanduhr draws on the macOS desktop, picked "
    "from ~/Library/Application Support/Desk/messages.txt. Returns every raw line of the file "
    "(comments and effects included), whether one line is pinned, the rotation (daily or hourly), "
    "mix_daily (whether plain lines take turns with a weekday's own lines), "
    "today: the usual raw line the Desk shows now, and today_special: the date lines it shows above "
    "it today (empty on most days). Call this before propose_desk_messages to match "
    "the user's voice and to avoid repeats. " + DESK_SYNTAX + DESK_EFFECTS)
DESK_GUIDE_PROPOSE = (
    "Suggest lines for the user's Desk messages. Sanduhr checks them and, unless the user lets "
    "Claude change the messages directly, shows them as a suggestion to Add, Review or Dismiss: "
    "pending_approval means the user decides, applied means they are in the list now, rejected "
    "comes with reasons to fix. This server never writes messages.txt. Limits: 1 to 60 lines, "
    "120 characters each, no control characters or line breaks inside a line, at least one line "
    "that is not a comment; prefixes and effects must parse. mode add appends (default), replace "
    "swaps the whole list (the user's previous list is kept as messages.txt.previous); to add "
    "birthdays, anniversaries or holidays use mode add with one date line each (MM-DD:), never "
    "replace, so the user's own lines stay. Read the current list first with get_desk_messages. "
    + DESK_SYNTAX + DESK_EFFECTS)

THEME_GUIDE = (
    "Give the Sanduhr widget a new color theme. Call when the user asks for a theme, a new look, or "
    "colors from an image, a palette, or a vibe. The theme object uses the snake_case fields of "
    "docs/themes/template.json. Required: name (1 to 24 characters; the theme strip shows about "
    "12, so keep it short and Title Case) and fourteen colors, each '#rrggbb' (a hash and six hex "
    "digits; no '#fff', no alpha): bg (window background, the darkest shade), glass (card fill), "
    "glass_on_mica (the card as it sits over the desktop's blur, usually a touch darker than glass), "
    "title_bg, footer_bg, border, bar_bg (the empty part of a bar), text, text_secondary, text_dim, "
    "text_muted (one hue, each darker than the last), accent (the brand color: the top strip, the "
    "readout's glow), sparkline (the history line, the accent's hue) and pace_marker (the tick that "
    "says where usage should be by now). Optional: description (one line, up to 200 characters, "
    "shown when the user hovers the theme in Settings; say what the look is), and the glass dials: "
    "glass_alpha (0 to 1, readable at 0.7 to 0.9), border_alpha (0 to 1, 0.2 to 0.6 is subtle), "
    "border_tint ('#rrggbb' in the accent's hue, or null for the plain border), accent_bloom "
    "({blur: 0 to 20, alpha: 0 to 1}, 3 to 8 and 0.25 to 0.65 read well), inner_highlight "
    "({color: '#rrggbb', alpha: 0.15 to 0.30} or null), card_corner_radius (0 to 24, default 10), "
    "breath_period_ms (500 to 20000, the bar's slow breathing, default 2800), ghost_alpha (0 to 1, "
    "the pace ghost tick), opts_out_of_mica (true makes the cards opaque; then set glass_alpha 1) "
    "and monospace_font (any string gives monospaced numbers). What makes a good theme, measured "
    "by the lint: a dark base (bg, glass and glass_on_mica luminance under 0.25: light glass does "
    "not layer); text at 4.5:1 or better on the card (glass_on_mica at glass_alpha over a mid-gray "
    "desktop) and text_secondary at 3:1; the text ramp one hue at decreasing luminance; one accent "
    "hue shared by accent, sparkline and border_tint; a pace_marker distinct from the accent that "
    "stands out on the green bar fill (#4ade80) and on bar_bg by brightness (1.4:1) or by hue (60 "
    "degrees apart; a complementary hue works). The usage bars themselves stay green, yellow, "
    "orange and red. On the Mac the widget can also wear the built-in Match Desk theme (no glass, "
    "the Desk's own font and ink), and the Desk draws its meters in its own ink on the wallpaper "
    "beside the widget: a theme whose accent and text sit well next to the user's Desk ink reads "
    "as one desktop, and Match Desk itself cannot be replaced. A theme is checked here first: "
    "status rejected with findings (level, field, message) means fix the named fields and call "
    "again, nothing was written; warnings ride along with a theme that goes through. Then Sanduhr "
    "asks the user, who sees a preview card with the description and chooses Save, Save and Apply "
    "or Dismiss: pending_approval means the user decides (the result arrives later; do not "
    "propose the same theme again). When the user lets Claude change themes directly, Sanduhr saves "
    "it to its themes folder and applies it at once (apply: false saves without switching): "
    "applied or saved, with key, previous_key (the theme in use before, so the user can go back) "
    "and saved_path. A built-in's name (Obsidian, Aurora, Ember, Mint, 626 Labs, Matrix, Blueprint, "
    "Match Desk) is refused: pick another name or pass save_as. A theme whose key is taken by a "
    "different theme of the user's is saved under the next free key (ocean-2) and the result names "
    "renamed_from; the user's own theme is never overwritten. queued means Sanduhr did not answer: "
    "it is not running, and picks the request up within ten minutes. This server never writes a "
    "theme itself.")

WATCH_GUIDE_START = (
    "Put up a watcher: a live card on the user's notch or Desk for work you are keeping an eye on "
    "(a CI run, a deploy, a long build or test run, a migration). The notch shows the full line once "
    "(title, time so far, done/total when you pass a total), then rests on '<short> · <done/total>', "
    "so pass short: a name of up to 12 characters such as 'PR 140', 'v2.8.0' or 'deploy' (without "
    "it Sanduhr derives one from the title). The Desk shows the full title and a one-line note; a "
    "click opens the link. "
    "Returns the watcher's id: move it with watch_update (progress, a note, or state waiting when "
    "the user must act: it pulses and glows the notch) and finish it with watch_end (passed or "
    "failed). Without an update for 10 minutes it greys as lost touch, so update long jobs. Limits: "
    "title 1 to 80 characters on one line, short up to 12, link https only, total 1 to 1000000. Pass work: true for "
    "anything from the user's employer, so it hides in demo mode. Refused with reason "
    "watchers_off when the user has not turned on Let agents show watchers in Sanduhr (Settings, "
    "Integrations); then do not retry.")
WATCH_GUIDE_UPDATE = (
    "Move a watcher from watch_start: done (how many of its total are done), note (one line, up to "
    "140 characters, replaces the last), state running or waiting (waiting on the user: it pulses "
    "and glows the notch once; set running again when they have acted). Any update also keeps it "
    "from greying as lost touch.")
WATCH_GUIDE_END = (
    "Finish a watcher from watch_start: result passed (shows a check and fades after a few seconds) "
    "or failed (stays until the user dismisses it), with an optional one-line note (up to 140 "
    "characters) such as what failed.")
WATCH_ANNOTATIONS = {"readOnlyHint": False, "destructiveHint": False, "idempotentHint": False,
                     "openWorldHint": False}
WATCH_ID_SCHEMA = {"type": "string", "pattern": "^w[0-9a-f]{12}$", "description": "The id watch_start returned."}
WATCH_NOTE_SCHEMA = {"type": "string", "maxLength": 140, "description": "One line for the user, up to 140 characters."}

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
    {
        "name": "get_desk_messages",
        "description": DESK_GUIDE_READ,
        "inputSchema": NO_ARGS,
        "annotations": READ_ONLY,
    },
    {
        "name": "propose_desk_messages",
        "description": DESK_GUIDE_PROPOSE,
        "inputSchema": {
            "type": "object",
            "properties": {
                "lines": {"type": "array", "items": {"type": "string", "maxLength": 120},
                          "minItems": 1, "maxItems": 60,
                          "description": "The lines, exactly as they would sit in messages.txt (prefix first, then effects, then the text)."},
                "mode": {"type": "string", "enum": ["add", "replace"],
                         "description": "add appends the lines (lines already in the list are skipped); replace swaps the whole list for them (the comment block at the top of the file stays). Default add."},
                "note": {"type": "string", "maxLength": 300,
                         "description": "One short sentence for the user on why these lines (shown beside the suggestion)."},
            },
            "required": ["lines"],
            "additionalProperties": False,
        },
        "annotations": {"readOnlyHint": False, "destructiveHint": False, "idempotentHint": False,
                        "openWorldHint": False},
    },
    {
        "name": "propose_theme",
        "description": THEME_GUIDE,
        "inputSchema": {
            "type": "object",
            "properties": {
                "theme": {"type": "object",
                          "description": "The theme JSON: name, the fourteen #rrggbb colors, optional description and dials (docs/themes/template.json)."},
                "save_as": {"type": "string", "pattern": "^[a-z0-9][a-z0-9-]{0,39}$",
                            "description": "File key under the themes folder. Default: the name, slugged."},
                "apply": {"type": "boolean", "description": "Apply after saving. Default true."},
            },
            "required": ["theme"],
            "additionalProperties": False,
        },
        "annotations": {"readOnlyHint": False, "destructiveHint": False, "idempotentHint": False,
                        "openWorldHint": False},
    },
    {
        "name": "watch_start",
        "description": WATCH_GUIDE_START,
        "inputSchema": {
            "type": "object",
            "properties": {
                "title": {"type": "string", "maxLength": 80, "description": "What is being watched, one line (\"PR 140 CI: combine statuslines\")."},
                "short": {"type": "string", "maxLength": 12, "description": "The notch's resting name, up to 12 characters: \"PR 140\", \"v2.8.0\", \"deploy\". Pass one; without it Sanduhr derives one from the title."},
                "link": {"type": "string", "maxLength": 2048, "description": "An https link a click opens (the run's page)."},
                "total": {"type": "integer", "minimum": 1, "maximum": 1000000, "description": "How many steps there are, for done/total."},
                "work": {"type": "boolean", "description": "Work for the user's employer: hidden in demo mode. Default false."},
            },
            "required": ["title"],
            "additionalProperties": False,
        },
        "annotations": WATCH_ANNOTATIONS,
    },
    {
        "name": "watch_update",
        "description": WATCH_GUIDE_UPDATE,
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": WATCH_ID_SCHEMA,
                "done": {"type": "integer", "minimum": 0, "maximum": 1000000, "description": "How many are done."},
                "note": WATCH_NOTE_SCHEMA,
                "state": {"type": "string", "enum": ["running", "waiting"], "description": "waiting: on the user."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
        "annotations": WATCH_ANNOTATIONS,
    },
    {
        "name": "watch_end",
        "description": WATCH_GUIDE_END,
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": WATCH_ID_SCHEMA,
                "result": {"type": "string", "enum": ["passed", "failed"]},
                "note": WATCH_NOTE_SCHEMA,
            },
            "required": ["id", "result"],
            "additionalProperties": False,
        },
        "annotations": WATCH_ANNOTATIONS,
    },
]
TOOL_NAMES = [t["name"] for t in TOOLS]
# The Desk messages handoff (item 54). The app mirrors these names (DeskMessageHandoff); a test on
# each side pins them.
DESK_REQUEST_FILE = "desk-messages-request.json"
DESK_RESULT_FILE = "desk-messages-result.json"
DESK_STATE_FILE = "desk-messages-state.json"
DESK_MAX_LINES = 60
DESK_MAX_LINE_CHARS = 120
DESK_MAX_NOTE_CHARS = 300
DESK_MAX_FILE_BYTES = 256 * 1024
DESK_WAIT_SECONDS = 10.0
DESK_POLL_SECONDS = 0.25
TOOLS_NOT_ON_MAC = {
    "publish_usage": "dropped on Mac: nothing leaves this Mac",
}
# The theme handoff (item 55): the Windows propose_theme's file names, mirrored by the app
# (ThemeProposal); a test on each side pins them.
THEME_REQUEST_FILE = "theme-request.json"
THEME_RESULT_FILE = "theme-result.json"
THEME_WAIT_SECONDS = 10.0
THEME_MAX_BYTES = 16 * 1024
# The widget's compiled-in themes (ThemeRegistry.builtIn): a proposal never takes their key.
BUILT_IN_THEME_IDS = ["obsidian", "aurora", "ember", "mint", "626-labs", "matrix", "blueprint", "match-desk"]
# The watchers handoff (item 66). The app mirrors these names (WatcherStore); a test on each side
# pins them.
WATCH_SWITCH_FILE = "watchers.json"
WATCH_REQUEST_PREFIX = "watch-request-"
WATCH_MAX_TITLE = 80
WATCH_MAX_SHORT = 12
WATCH_MAX_NOTE = 140
WATCH_MAX_LINK = 2048
WATCH_MAX_TOTAL = 1000000
WATCH_ID_RE = re.compile(r"^w[0-9a-f]{12}$")


class Paths:
    """Where the server reads. SANDUHR_SUPPORT_DIR (tests, a test folder) moves everything;
    SANDUHR_SNAPSHOT moves the snapshot alone, as before."""

    def __init__(self, support_dir=None, snapshot=None, desk_dir=None):
        self.support_dir = support_dir or os.environ.get("SANDUHR_SUPPORT_DIR") or os.path.expanduser(
            "~/Library/Application Support/Sanduhr")
        self.snapshot = snapshot or os.environ.get("SANDUHR_SNAPSHOT") or os.path.join(
            self.support_dir, "snapshot.json")
        self.access = os.path.join(self.support_dir, "mcp-access.json")
        self.vault = os.path.join(self.support_dir, "vault")
        # Desk's folder sits beside Sanduhr's (Application Support/Desk), so a test folder moves
        # it too and a test never reaches the real messages.txt.
        self.desk_dir = desk_dir or os.path.join(os.path.dirname(os.path.abspath(self.support_dir)), "Desk")
        self.messages = os.path.join(self.desk_dir, "messages.txt")
        self.desk_state = os.path.join(self.support_dir, DESK_STATE_FILE)
        self.desk_request = os.path.join(self.support_dir, DESK_REQUEST_FILE)
        self.desk_result = os.path.join(self.support_dir, DESK_RESULT_FILE)
        self.theme_request = os.path.join(self.support_dir, THEME_REQUEST_FILE)
        self.theme_result = os.path.join(self.support_dir, THEME_RESULT_FILE)
        self.watch_switch = os.path.join(self.support_dir, WATCH_SWITCH_FILE)

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
        "watchers_allowed": watchers_allowed(paths),
    }


# -- Desk messages (item 54) -------------------------------------------------------------------
#
# The same grammar as the app's MessageEngine and MessageMarkup (Desk/Message.swift,
# Desk/MessageEffects.swift). The app is the authority: it checks every request again before
# anything reaches messages.txt.

WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
WEEKDAY_LOOKALIKES = {w.lower() for w in WEEKDAYS} | {
    "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
    "tues", "thur", "thurs"}
DATE_TAG_RE = re.compile(r"^\d{2}-\d{2}$")
DATE_LIKE_RE = re.compile(r"^\d{1,2}-\d{1,2}$")
HEX_RE = re.compile(r"^#?(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")
SIZE_RE = re.compile(r"^\d+(?:\.\d+)?$")
EFFECT_NAMES = "ink, glow, noglow, size, write, shimmer, sweep, font"
# {font:...}'s letter styles, the statusline's names (sanduhr_statusline.py FONTS), in the app's order.
FONT_STYLES = "bold, italic, bold-italic, sans, mono, double-struck, script, fraktur, small-caps"
# {sweep:<seconds>}'s period, inclusive.
SWEEP_PERIOD = (2, 3600)
DAYS_IN_MONTH = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
ROTATIONS = ("daily", "hourly")


def is_control(c):
    o = ord(c)
    return o < 0x20 or 0x7F <= o <= 0x9F or o in (0x2028, 0x2029)


def split_prefix(line):
    """(kind, tag, body) for a stripped message line: kind is "date", "weekday" or "plain".
    A line whose first colon follows something that is neither a date nor a weekday is plain."""
    colon = line.find(":")
    if colon >= 0:
        tag = line[:colon].strip()
        body = line[colon + 1:].strip()
        if DATE_TAG_RE.match(tag):
            return "date", tag, body
        if tag in WEEKDAYS:
            return "weekday", tag, body
    return "plain", None, line


def parse_effect(tag):
    """(name, value) for one tag's inside, or an error string."""
    name, sep, value = tag.partition(":")
    name = name.strip().lower()
    value = value.strip()
    if name in ("glow", "noglow", "write", "shimmer"):
        if sep:
            return "{%s} takes no value" % name
        return name, None
    if name == "ink":
        colors = [c.strip() for c in value.split(",")] if sep else []
        if not colors or any(not c for c in colors):
            return "{ink:...} needs 1 to 4 hex colors, like {ink:#ff2a6d,#05d9e8}"
        if len(colors) > 4:
            return "{ink:...} takes at most 4 colors"
        bad = [c for c in colors if not HEX_RE.match(c)]
        if bad:
            return "{ink:...} has a color that is not hex (#rgb or #rrggbb)"
        return name, colors
    if name == "size":
        if not sep or not SIZE_RE.match(value) or not 0.5 <= float(value) <= 2:
            return "{size:...} needs a number from 0.5 to 2, like {size:1.3}"
        return name, float(value)
    if name == "font":
        style = font_style(value) if sep else None
        if style is None:
            return "{font:...} needs a letter style: %s" % FONT_STYLES
        return name, style
    if name == "sweep":
        if not sep:
            return name, None
        if not SIZE_RE.match(value) or not SWEEP_PERIOD[0] <= float(value) <= SWEEP_PERIOD[1]:
            return "{sweep:...} takes a period in seconds from 2 to 3600, like {sweep:20}"
        return name, float(value)
    return "unknown effect {%s}; known: %s" % (name[:20], EFFECT_NAMES)


def _style_key(s):
    return "".join(c for c in s.lower() if c not in " -_")


def font_style(value):
    """The letter style a {font:...} value names (case, spaces, hyphens and underscores
    ignored, so smallcaps is small-caps), or None."""
    key = _style_key(value)
    for name in FONT_STYLES.split(", "):
        if _style_key(name) == key:
            return name
    return None


def parse_effects(body):
    """(effects, text, error): the tags at the start of body, strictly. error is None when every
    tag parsed and some text follows."""
    effects = {}
    rest = body.lstrip()
    while rest.startswith("{"):
        end = rest.find("}")
        if end < 0:
            return effects, rest, "an effect tag is not closed with }"
        parsed = parse_effect(rest[1:end])
        if isinstance(parsed, str):
            return effects, rest, parsed
        name, value = parsed
        if name in ("glow", "noglow"):
            effects["glow"] = name == "glow"
        else:
            effects[name] = True if value is None else value
        rest = rest[end + 1:].lstrip()
    if not rest:
        return effects, rest, "the line has effects but no text"
    return effects, rest, None


def check_desk_line(line):
    """None for a good line, else why not (the line number is added by the caller)."""
    if not isinstance(line, str):
        return "is not a string"
    try:
        line.encode("utf-8")
    except UnicodeEncodeError:
        return "is not valid UTF-8"
    if any(is_control(c) for c in line):
        return "has a control character or a line break"
    text = line.strip()
    if len(text) > DESK_MAX_LINE_CHARS:
        return "is %d characters; the limit is %d" % (len(text), DESK_MAX_LINE_CHARS)
    if not text or text.startswith("#"):
        return None
    colon = text.find(":")
    if colon >= 0:
        tag = text[:colon].strip()
        if DATE_LIKE_RE.match(tag):
            month, day = (int(x) for x in tag.split("-"))
            if not DATE_TAG_RE.match(tag) or not 1 <= month <= 12 or not 1 <= day <= DAYS_IN_MONTH[month - 1]:
                return "starts with '%s:', which is not a date; write MM-DD, like 10-31" % tag
        elif tag.lower() in WEEKDAY_LOOKALIKES and tag not in WEEKDAYS:
            return "starts with '%s:'; write the day as Mon, Tue, Wed, Thu, Fri, Sat or Sun" % tag[:12]
    kind, _, body = split_prefix(text)
    if kind != "plain" and not body:
        return "has a prefix but no text"
    _, _, error = parse_effects(body)
    return error


def is_message_line(line):
    text = line.strip() if isinstance(line, str) else ""
    return bool(text) and not text.startswith("#")


def validate_desk_lines(lines):
    """The reasons a proposal is refused, [] when it may go to the app."""
    if not isinstance(lines, list) or not lines:
        return ["lines must be a list of 1 to %d strings" % DESK_MAX_LINES]
    reasons = []
    if len(lines) > DESK_MAX_LINES:
        reasons.append("%d lines; the limit is %d" % (len(lines), DESK_MAX_LINES))
    for i, line in enumerate(lines[:DESK_MAX_LINES * 2], 1):
        why = check_desk_line(line)
        if why:
            reasons.append("line %d %s" % (i, why))
    if not reasons and not any(is_message_line(x) for x in lines):
        reasons.append("no message line: every line is blank or a # comment")
    return reasons[:20]


DESK_MAX_SPECIAL = 3


def desk_today(text, now, hourly, mix=False, pinned=None):
    """MessageEngine.today: (special, usual) at `now` (local time), raw bodies. A pinned line is the
    usual line; the date's lines still stack above it. Otherwise the usual line is
    today's weekday pool if it has lines (with the plain lines too when `mix`), else the plain
    pool; plain rotates by day, a weekday pool by week. Every line for today's date is special,
    up to 3; more take turns hourly, 3 at a time."""
    today = now.strftime("%m-%d")
    weekday = WEEKDAYS[(now.isoweekday()) % 7]
    dated, daily, plain = [], [], []
    for raw in text.splitlines():
        line = raw.strip(" \t")
        if not line or line.startswith("#"):
            continue
        kind, tag, body = split_prefix(line)
        if kind == "date":
            if tag == today and body:
                dated.append(body)
            continue
        if kind == "weekday":
            if tag == weekday and body:
                daily.append(body)
            continue
        plain.append(line)
    day_index = now.date().toordinal()

    def turn(pool, step):
        if not pool:
            return None
        slot = step * 24 + now.hour if hourly else step
        return pool[slot % len(pool)]

    if pinned:
        usual = pinned
    elif daily:
        usual = turn(daily + plain if mix else daily, day_index // 7)
    else:
        usual = turn(plain, day_index)
    special = dated
    if len(dated) > DESK_MAX_SPECIAL:
        start = ((now.year * 24 + now.hour) * DESK_MAX_SPECIAL) % len(dated)
        special = [dated[(start + i) % len(dated)] for i in range(DESK_MAX_SPECIAL)]
    return special, usual


def pick_desk_line(text, now, hourly, mix=False):
    """MessageEngine.pick: the usual line Desk shows at `now` (local time), raw, or None."""
    return desk_today(text, now, hourly, mix)[1]


def read_desk_state(paths):
    """The app's note of the user's settings (pinned, rotation, mix_daily), or the defaults."""
    doc = read_json(paths.desk_state)
    state = {"known": False, "pinned": False, "pinned_line": None, "rotate": "daily", "mix_daily": False}
    if isinstance(doc, dict) and doc.get("schema_version") == 1:
        state["known"] = True
        state["pinned"] = doc.get("pinned") is True
        line = doc.get("pinned_line")
        state["pinned_line"] = line if state["pinned"] and isinstance(line, str) else None
        if doc.get("rotate") in ROTATIONS:
            state["rotate"] = doc["rotate"]
        state["mix_daily"] = doc.get("mix_daily") is True
    return state


def build_desk_messages(now=None, paths=None):
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    state = read_desk_state(paths)
    out = {"status": "ok", "file_found": False, "lines": [], "pinned": state["pinned"],
           "rotate": state["rotate"], "mix_daily": state["mix_daily"], "today": None, "today_special": [],
           "limits": {"lines_per_proposal": DESK_MAX_LINES, "characters_per_line": DESK_MAX_LINE_CHARS}}
    if not state["known"]:
        out["settings_note"] = "Sanduhr has not reported the pin and rotation yet; shown as the defaults."
    try:
        with open(paths.messages, "rb") as f:
            data = f.read(DESK_MAX_FILE_BYTES + 1)
    except FileNotFoundError:
        out["today"] = state["pinned_line"]
        return out
    except OSError:
        return no_data("unreadable", "Desk's messages.txt could not be read.")
    if len(data) > DESK_MAX_FILE_BYTES:
        return no_data("too_large", "Desk's messages.txt is larger than 256 KB; it is not read.")
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return no_data("not_utf8", "Desk's messages.txt is not UTF-8; it is not read.")
    lines = text.splitlines()
    while lines and not lines[-1].strip():
        lines.pop()
    out["file_found"] = True
    out["lines"] = lines
    local = now.astimezone()
    out["today_special"], out["today"] = desk_today(text, local, state["rotate"] == "hourly", state["mix_daily"],
                                                    pinned=state["pinned_line"] if state["pinned"] else None)
    return out


def read_desk_result(path, request_id):
    doc = read_json(path)
    if isinstance(doc, dict) and doc.get("id") == request_id and isinstance(doc.get("result"), dict):
        return doc["result"]
    return None


def build_propose_desk_messages(args, now=None, paths=None, wait=DESK_WAIT_SECONDS, poll=DESK_POLL_SECONDS,
                                sleep=time.sleep, clock=time.monotonic):
    """Checks the lines here (instant refusals, no file written), then hands a clean proposal to
    the app through desk-messages-request.json and waits briefly for its answer. Never writes
    messages.txt."""
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    args = args if isinstance(args, dict) else {}
    unknown = sorted(k for k in args if k not in ("lines", "mode", "note"))
    mode = args.get("mode", "add")
    note = args.get("note")
    reasons = []
    if unknown:
        reasons.append("unknown argument(s): " + ", ".join(k[:20] for k in unknown[:5]))
    if mode not in ("add", "replace"):
        reasons.append("mode must be add or replace")
    if note is not None:
        if not isinstance(note, str):
            reasons.append("note must be a string")
        elif len(note) > DESK_MAX_NOTE_CHARS or any(is_control(c) for c in note):
            reasons.append("note must be one line of at most %d characters" % DESK_MAX_NOTE_CHARS)
        else:
            try:
                note.encode("utf-8")
            except UnicodeEncodeError:
                reasons.append("note is not valid UTF-8")
    reasons += validate_desk_lines(args.get("lines"))
    if reasons:
        return {"status": "rejected", "reasons": reasons}

    request_id = uuid.uuid4().hex
    request = {"schema_version": 1, "id": request_id, "requested_at": iso_o(now),
               "lines": [x.strip() for x in args["lines"]], "mode": mode,
               "note": note.strip() if isinstance(note, str) and note.strip() else None}
    try:
        os.makedirs(paths.support_dir, mode=0o700, exist_ok=True)
        tmp = paths.desk_request + ".tmp"
        fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(request, f, ensure_ascii=False)
        os.replace(tmp, paths.desk_request)   # atomic: the app never reads half a file
    except OSError as e:
        return {"status": "error", "reason": "request_write_failed",
                "remedy": "Could not queue the request (%s)." % type(e).__name__}

    deadline = clock() + wait
    while True:
        res = read_desk_result(paths.desk_result, request_id)
        if res is not None:
            out = {k: res[k] for k in ("status", "reasons", "lines_added", "lines_skipped", "mode") if k in res}
            out["request_id"] = request_id
            return out
        if clock() >= deadline:
            break
        sleep(poll)
    return {"status": "queued", "reason": "app_not_responding", "request_id": request_id,
            "remedy": "Sanduhr did not answer within %d seconds. Start Sanduhr: it picks the request up "
                      "within ten minutes of when it was made and shows it as a suggestion." % int(wait)}


# -- propose_theme (item 55) -------------------------------------------------------------------
#
# A port of the Windows ThemeLint (windows-dotnet/src/Sanduhr.Core/ThemeLint.cs): the same rules,
# thresholds and wording, plus the Mac's own fields (description, ghost_alpha, monospace_font) and
# a type check on opts_out_of_mica. The app runs the same lint again (Services/ThemeLint.swift) and
# is the authority.

THEME_MAX_NAME = 24
THEME_MAX_DESCRIPTION = 200
THEME_DARK_BASE_MAX_LUMINANCE = 0.25
THEME_TEXT_CONTRAST_MIN = 4.5
THEME_TEXT_SECONDARY_CONTRAST_MIN = 3.0
THEME_PACE_MARKER_CONTRAST_MIN = 1.4
THEME_PACE_MARKER_HUE_MIN = 60
THEME_HUE_TOLERANCE = 30
THEME_HUE_SATURATION_FLOOR = 0.15
THEME_DESKTOP = (0x80 / 255.0, 0x80 / 255.0, 0x80 / 255.0)
THEME_BAR_FILL_GREEN = "#4ade80"
THEME_COLOR_FIELDS = [
    "bg", "glass", "glass_on_mica", "title_bg", "border",
    "text", "text_secondary", "text_dim", "text_muted",
    "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
]
THEME_KEY_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,39}$")
HEX6_RE = re.compile(r"^#[0-9a-fA-F]{6}$")


def theme_hex(s):
    """'#rrggbb' to (r, g, b) in 0..1, or None."""
    if not isinstance(s, str) or not HEX6_RE.match(s):
        return None
    return tuple(int(s[i:i + 2], 16) / 255.0 for i in (1, 3, 5))


def luminance(c):
    def lin(v):
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * lin(c[0]) + 0.7152 * lin(c[1]) + 0.0722 * lin(c[2])


def contrast(l1, l2):
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def composite(top, alpha, under):
    alpha = min(1.0, max(0.0, alpha))
    return tuple(t * alpha + u * (1 - alpha) for t, u in zip(top, under))


def saturation(c):
    mx, mn = max(c), min(c)
    return 0.0 if mx <= 0 else (mx - mn) / mx


def hue(c):
    r, g, b = c
    mx, mn = max(c), min(c)
    d = mx - mn
    if d <= 0:
        return 0.0
    if mx == r:
        h = math.fmod((g - b) / d, 6)
    elif mx == g:
        h = (b - r) / d + 2
    else:
        h = (r - g) / d + 4
    h *= 60
    return h + 360 if h < 0 else h


def hue_distance(a, b):
    """Degrees between two hues, or None when either color is too gray to have one."""
    if saturation(a) < THEME_HUE_SATURATION_FLOOR or saturation(b) < THEME_HUE_SATURATION_FLOOR:
        return None
    d = abs(hue(a) - hue(b)) % 360
    return 360 - d if d > 180 else d


def fmt(v, places):
    """.NET's '0.0' / '0.00': fixed places, half away from zero."""
    q = 10 ** places
    r = math.floor(abs(v) * q + 0.5) / q
    return "%s%.*f" % ("-" if v < 0 and r else "", places, r)


def fmt_trim(v, places):
    """.NET's '0.##' / '0.###': up to `places`, trailing zeros dropped."""
    t = fmt(v, places)
    if "." in t:
        t = t.rstrip("0").rstrip(".")
    return t


def describe_json(v):
    if v is None:
        return "null"
    if isinstance(v, str):
        return '"%s"' % v
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, dict):
        return "an object"
    if isinstance(v, list):
        return "an array"
    return json.dumps(v)


def is_number(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def lint_theme(data):
    """Findings for a theme dict: [{level, field, message}]. Errors block it; warnings ride along."""
    findings = []

    def err(field, message):
        findings.append({"level": "error", "field": field, "message": message})

    def warn(field, message):
        findings.append({"level": "warning", "field": field, "message": message})

    def check_range(o, key, lo, hi, fallback, label=None):
        label = label or key
        if key not in o or o[key] is None:
            return fallback
        v = o[key]
        if not is_number(v):
            err(label, "%s must be a number between %s and %s, got %s." % (label, fmt_trim(lo, 2), fmt_trim(hi, 2), describe_json(v)))
            return fallback
        if v != v or v < lo or v > hi:
            err(label, "%s must be between %s and %s, got %s." % (label, fmt_trim(lo, 2), fmt_trim(hi, 2), fmt_trim(v, 3)))
            return fallback
        return float(v)

    def check_optional_hex(o, key, label=None):
        label = label or key
        if key not in o or o[key] is None:
            return
        if theme_hex(o[key]) is None:
            err(label, "%s must be a #rrggbb color or null, got %s." % (label, describe_json(o[key])))

    if not isinstance(data, dict):
        return [{"level": "error", "field": "json", "message": "Theme JSON must be an object."}]

    name = data.get("name")
    if not isinstance(name, str):
        err("name", "name is required (1 to 12 characters, Title Case).")
    elif not name.strip():
        err("name", "name must not be empty.")
    elif len(name) > THEME_MAX_NAME:
        err("name", "name is %d characters; the strip shows 12, %d is the limit." % (len(name), THEME_MAX_NAME))
    elif any(is_control(c) for c in name):
        err("name", "name must be one line with no control characters.")

    colors = {}
    for field in THEME_COLOR_FIELDS:
        if field not in data or data[field] is None:
            err(field, "%s is required (a #rrggbb color)." % field)
            continue
        rgb = theme_hex(data[field])
        if rgb is None:
            err(field, "%s must be a #rrggbb color (six hex digits), got %s." % (field, describe_json(data[field])))
            continue
        colors[field] = rgb

    glass_alpha = check_range(data, "glass_alpha", 0, 1, 0.80)
    check_range(data, "border_alpha", 0, 1, 0.40)
    check_optional_hex(data, "border_tint")
    if data.get("accent_bloom") is not None:
        ab = data["accent_bloom"]
        if isinstance(ab, dict):
            check_range(ab, "blur", 0, 20, 4, "accent_bloom.blur")
            check_range(ab, "alpha", 0, 1, 0.45, "accent_bloom.alpha")
        else:
            err("accent_bloom", 'accent_bloom must be an object {"blur": 3-8, "alpha": 0.25-0.65}.')
    if data.get("inner_highlight") is not None:
        ih = data["inner_highlight"]
        if isinstance(ih, dict):
            if ih.get("color") is None:
                err("inner_highlight.color", "inner_highlight needs a color (#rrggbb) or the whole field set to null.")
            else:
                check_optional_hex(ih, "color", "inner_highlight.color")
            check_range(ih, "alpha", 0, 1, 0.20, "inner_highlight.alpha")
        else:
            err("inner_highlight", 'inner_highlight must be {"color": "#rrggbb", "alpha": 0.15-0.30} or null.')
    check_range(data, "card_corner_radius", 0, 24, 10)
    check_range(data, "breath_period_ms", 500, 20000, 4000)
    # The Mac's own fields.
    check_range(data, "ghost_alpha", 0, 1, 1.0)
    desc = data.get("description")
    if desc is not None:
        if not isinstance(desc, str) or len(desc) > THEME_MAX_DESCRIPTION or any(is_control(c) for c in desc):
            err("description", "description must be one line of at most %d characters, or null." % THEME_MAX_DESCRIPTION)
    mono = data.get("monospace_font")
    if mono is not None and not isinstance(mono, str):
        err("monospace_font", "monospace_font must be a string (any value turns on monospaced numbers) or null.")
    oom = data.get("opts_out_of_mica")
    if oom is not None and not isinstance(oom, bool):
        err("opts_out_of_mica", "opts_out_of_mica must be true or false, got %s." % describe_json(oom))
    opts_out = oom is True

    if any(f["level"] == "error" for f in findings):
        return findings

    for field in ("bg", "glass", "glass_on_mica"):
        lum = luminance(colors[field])
        if lum > THEME_DARK_BASE_MAX_LUMINANCE:
            warn(field, "%s is light (luminance %s, keep it under %s); translucent glass layering does not work on light surfaces."
                 % (field, fmt(lum, 2), fmt(THEME_DARK_BASE_MAX_LUMINANCE, 2)))

    card = composite(colors["glass_on_mica"], 1.0 if opts_out else glass_alpha, THEME_DESKTOP)
    card_l = luminance(card)
    ratio = contrast(luminance(colors["text"]), card_l)
    if ratio < THEME_TEXT_CONTRAST_MIN:
        warn("text", "text reads at %s:1 on the card (needs %s:1); pick a brighter text or a darker glass_on_mica."
             % (fmt(ratio, 1), fmt(THEME_TEXT_CONTRAST_MIN, 1)))
    ratio2 = contrast(luminance(colors["text_secondary"]), card_l)
    if ratio2 < THEME_TEXT_SECONDARY_CONTRAST_MIN:
        warn("text_secondary", "text_secondary reads at %s:1 on the card (needs %s:1)."
             % (fmt(ratio2, 1), fmt(THEME_TEXT_SECONDARY_CONTRAST_MIN, 1)))

    ramp = ["text", "text_secondary", "text_dim", "text_muted"]
    for i in range(1, len(ramp)):
        if luminance(colors[ramp[i]]) >= luminance(colors[ramp[i - 1]]):
            warn(ramp[i], "%s is not darker than %s; the text ramp is one hue at decreasing luminance." % (ramp[i], ramp[i - 1]))
    for i in range(1, len(ramp)):
        d = hue_distance(colors["text"], colors[ramp[i]])
        if d is not None and d > THEME_HUE_TOLERANCE:
            warn(ramp[i], "%s is a different hue from text (%s degrees apart); keep the ramp monochrome." % (ramp[i], fmt(d, 0)))

    green = theme_hex(THEME_BAR_FILL_GREEN)
    marker = colors["pace_marker"]
    hidden_green = (contrast(luminance(marker), luminance(green)) < THEME_PACE_MARKER_CONTRAST_MIN
                    and (hue_distance(marker, green) or 0) < THEME_PACE_MARKER_HUE_MIN)
    hidden_bar = (contrast(luminance(marker), luminance(colors["bar_bg"])) < THEME_PACE_MARKER_CONTRAST_MIN
                  and (hue_distance(marker, colors["bar_bg"]) or 0) < THEME_PACE_MARKER_HUE_MIN)
    if hidden_green or hidden_bar:
        warn("pace_marker", "pace_marker disappears on the %s; it should stand out on every fill by brightness or by a complementary hue."
             % ("green bar fill" if hidden_green else "empty bar"))

    sd = hue_distance(colors["accent"], colors["sparkline"])
    if sd is not None and sd > THEME_HUE_TOLERANCE:
        warn("sparkline", "sparkline is a different hue from accent (%s degrees apart); one accent hue reads as one brand." % fmt(sd, 0))
    tint = theme_hex(data.get("border_tint"))
    if tint is not None:
        td = hue_distance(colors["accent"], tint)
        if td is not None and td > THEME_HUE_TOLERANCE:
            warn("border_tint", "border_tint is a different hue from accent (%s degrees apart); tint borders with the accent or leave it null." % fmt(td, 0))

    if opts_out and glass_alpha < 1.0:
        warn("glass_alpha", "opts_out_of_mica is true, so set glass_alpha to 1.0; cards render translucent on non-Mica fallbacks otherwise.")
    return findings


def theme_slug(name):
    """Display name to file key (ThemeHandoff.Slugify): lowercase ASCII letters and digits, runs of
    anything else one hyphen, at most 40; 'theme' when nothing survives."""
    out = []
    pending = False
    for c in name.strip().lower():
        if ("a" <= c <= "z") or ("0" <= c <= "9"):
            if pending and out:
                out.append("-")
            pending = False
            out.append(c)
        else:
            pending = True
    s = "".join(out)
    if len(s) > 40:
        s = s[:40].rstrip("-")
    return s or "theme"


THEME_RESULT_FIELDS = ("status", "reason", "remedy", "key", "name", "previous_key", "saved_path",
                       "renamed_from", "findings")


def theme_refusal(reason, remedy, findings=None):
    out = {"status": "rejected", "reason": reason, "remedy": remedy}
    if findings is not None:
        out["findings"] = findings
    return out


def build_propose_theme(args, now=None, paths=None, wait=THEME_WAIT_SECONDS, poll=DESK_POLL_SECONDS,
                        sleep=time.sleep, clock=time.monotonic):
    """Lints the theme here (an instant refusal writes nothing), then hands it to the app through
    theme-request.json and waits briefly for theme-result.json. Never writes a theme."""
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    args = args if isinstance(args, dict) else {}
    unknown = sorted(k for k in args if k not in ("theme", "save_as", "apply"))
    if unknown:
        return theme_refusal("invalid_params", "Unknown argument(s): " + ", ".join(k[:20] for k in unknown[:5]) +
                             ". propose_theme takes theme, save_as and apply.")
    theme = args.get("theme")
    if not isinstance(theme, dict):
        return theme_refusal("invalid_params", "theme must be an object: the theme JSON per docs/themes/template.json.")
    save_as = args.get("save_as")
    if save_as is not None and (not isinstance(save_as, str) or not THEME_KEY_RE.match(save_as)):
        return theme_refusal("invalid_params", "save_as must be 1-40 lowercase letters, digits or hyphens, not starting "
                                               "with a hyphen (omit it to slug the name).")
    apply = args.get("apply", True)
    if not isinstance(apply, bool):
        return theme_refusal("invalid_params", "apply must be true or false.")
    if len(json.dumps(theme, ensure_ascii=False).encode("utf-8")) > THEME_MAX_BYTES:
        return theme_refusal("invalid_params", "theme is larger than %d KB; send only the theme's fields." % (THEME_MAX_BYTES // 1024))

    findings = lint_theme(theme)
    if any(f["level"] == "error" for f in findings):
        return theme_refusal("invalid_theme", "Fix the fields named in findings and call again.", findings)
    key = save_as or theme_slug(theme["name"])
    if key in BUILT_IN_THEME_IDS:
        return theme_refusal("reserved_name", "'%s' is a built-in theme; pick another name or pass save_as." % key, findings)

    request_id = uuid.uuid4().hex
    request = {"schema_version": 1, "id": request_id, "requested_at": iso_o(now), "theme": theme,
               "save_as": save_as, "apply": apply}
    try:
        os.makedirs(paths.support_dir, mode=0o700, exist_ok=True)
        tmp = paths.theme_request + ".tmp"
        fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(request, f, ensure_ascii=False)
        os.replace(tmp, paths.theme_request)   # atomic: the app never reads half a file
    except OSError as e:
        return {"status": "error", "reason": "request_write_failed",
                "remedy": "Could not queue the request (%s)." % type(e).__name__}

    deadline = clock() + wait
    while True:
        res = read_desk_result(paths.theme_result, request_id)
        if res is not None:
            out = {k: res[k] for k in THEME_RESULT_FIELDS if k in res}
            out["request_id"] = request_id
            if "findings" not in out:
                out["findings"] = findings
            return out
        if clock() >= deadline:
            break
        sleep(poll)
    return {"status": "queued", "reason": "app_not_responding", "request_id": request_id, "name": theme["name"],
            "findings": findings,
            "remedy": "Sanduhr did not answer within %d seconds. Start Sanduhr: it picks the request up within "
                      "ten minutes of when it was made." % int(wait)}


# -- Watchers (item 66) ------------------------------------------------------------------------
#
# watch_start, watch_update and watch_end hand a request to the app through
# watch-request-<ms>-<seq>-<id>.json; the app checks it again, shows the card and deletes the file.
# Nothing is written while watchers.json (Sanduhr's switches) lacks "agents": true. The ids this
# server started are remembered for the session, so an update to an unknown id is refused here.

WATCH_STARTED = set()
WATCH_ENDED = set()
_watch_seq = [0]


def watchers_allowed(paths):
    doc = read_json(paths.watch_switch)
    return isinstance(doc, dict) and doc.get("schema_version") == 1 and doc.get("agents") is True


def watch_refusal(reason, remedy):
    return {"status": "rejected", "reason": reason, "remedy": remedy}


def watch_line(v, field, cap, required=False):
    """(clean, error): one line of at most `cap` characters."""
    if v is None:
        return None, ("%s is required" % field) if required else None
    if not isinstance(v, str):
        return None, "%s must be a string" % field
    s = v.strip()
    if required and not s:
        return None, "%s must not be empty" % field
    if len(s) > cap or any(is_control(c) for c in s):
        return None, "%s must be one line of at most %d characters" % (field, cap)
    return (s or None), None


def watch_int(v, field, lo, hi):
    if v is None:
        return None, None
    if isinstance(v, bool) or not isinstance(v, int) or not lo <= v <= hi:
        return None, "%s must be a whole number from %d to %d" % (field, lo, hi)
    return v, None


def watch_link(v):
    if v is None:
        return None, None
    if not isinstance(v, str) or len(v) > WATCH_MAX_LINK or not re.match(r"^https://[^\s/@:]+(:\d+)?(/\S*)?$", v, re.I):
        return None, "link must be an https:// link of at most %d characters, with no user or password" % WATCH_MAX_LINK
    return v, None


def claude_folder(env=None):
    """The Claude Code folder this session runs with, for the app's work tag."""
    env = os.environ if env is None else env
    v = env.get("CLAUDE_CONFIG_DIR")
    return os.path.abspath(os.path.expanduser(v)) if v else os.path.expanduser("~/.claude")


def write_watch_request(paths, doc, now):
    _watch_seq[0] += 1
    name = "%s%013d-%06d-%s.json" % (WATCH_REQUEST_PREFIX, int(now.timestamp() * 1000), _watch_seq[0], doc["id"])
    try:
        os.makedirs(paths.support_dir, mode=0o700, exist_ok=True)
        tmp = os.path.join(paths.support_dir, "." + name + ".tmp")
        fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False)
        os.replace(tmp, os.path.join(paths.support_dir, name))   # atomic: the app never reads half a file
    except OSError as e:
        return {"status": "error", "reason": "request_write_failed",
                "remedy": "Could not hand the watcher to Sanduhr (%s)." % type(e).__name__}
    return None


def watch_checks(args, allowed, paths):
    """The common refusals: arguments not an object or unknown, the switch off."""
    if not isinstance(args, dict):
        return watch_refusal("invalid_params", "Arguments must be an object.")
    unknown = sorted(k for k in args if k not in allowed)
    if unknown:
        return watch_refusal("invalid_params", "Unknown argument(s): " + ", ".join(str(k)[:20] for k in unknown[:5]) +
                             ". Takes " + ", ".join(allowed) + ".")
    if not watchers_allowed(paths):
        return watch_refusal("watchers_off", "The user has not turned on Let agents show watchers in Sanduhr "
                                             "(Settings, Integrations, Watchers). Do not retry; tell the user "
                                             "if they asked for a watcher.")
    return None


def watch_known(wid):
    if not isinstance(wid, str) or not WATCH_ID_RE.match(wid):
        return watch_refusal("invalid_params", "id must be the id watch_start returned.")
    if wid not in WATCH_STARTED:
        return watch_refusal("unknown_id", "No watcher with that id was started in this session; call watch_start.")
    if wid in WATCH_ENDED:
        return watch_refusal("ended", "That watcher has ended; call watch_start for a new one.")
    return None


def build_watch_start(args, now=None, paths=None, env=None):
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    bad = watch_checks(args, ("title", "short", "link", "total", "work"), paths)
    if bad:
        return bad
    errors = []
    title, e = watch_line(args.get("title"), "title", WATCH_MAX_TITLE, required=True)
    errors.append(e)
    short, e = watch_line(args.get("short"), "short", WATCH_MAX_SHORT)
    errors.append(e)
    link, e = watch_link(args.get("link"))
    errors.append(e)
    total, e = watch_int(args.get("total"), "total", 1, WATCH_MAX_TOTAL)
    errors.append(e)
    work = args.get("work", False)
    if not isinstance(work, bool):
        errors.append("work must be true or false")
    errors = [x for x in errors if x]
    if errors:
        return watch_refusal("invalid_params", "; ".join(errors) + ".")
    wid = "w" + uuid.uuid4().hex[:12]
    doc = {"schema_version": 1, "op": "start", "id": wid, "requested_at": iso_o(now), "title": title,
           "work": work, "folder": claude_folder(env)}
    if short is not None:
        doc["short"] = short
    if link is not None:
        doc["link"] = link
    if total is not None:
        doc["total"] = total
    failed = write_watch_request(paths, doc, now)
    if failed:
        return failed
    WATCH_STARTED.add(wid)
    return {"status": "ok", "id": wid,
            "note": "Sanduhr shows the watcher while it runs. Update it at least every 10 minutes, and end it with watch_end."}


def build_watch_update(args, now=None, paths=None):
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    bad = watch_checks(args, ("id", "done", "note", "state"), paths) or watch_known(args.get("id"))
    if bad:
        return bad
    errors = []
    done, e = watch_int(args.get("done"), "done", 0, WATCH_MAX_TOTAL)
    errors.append(e)
    note, e = watch_line(args.get("note"), "note", WATCH_MAX_NOTE)
    errors.append(e)
    state = args.get("state")
    if state is not None and state not in ("running", "waiting"):
        errors.append("state must be running or waiting")
    errors = [x for x in errors if x]
    if errors:
        return watch_refusal("invalid_params", "; ".join(errors) + ".")
    doc = {"schema_version": 1, "op": "update", "id": args["id"], "requested_at": iso_o(now)}
    if done is not None:
        doc["done"] = done
    if note is not None:
        doc["note"] = note
    if state is not None:
        doc["state"] = state
    return write_watch_request(paths, doc, now) or {"status": "ok", "id": args["id"]}


def build_watch_end(args, now=None, paths=None):
    now = now or datetime.now(timezone.utc)
    paths = paths or Paths()
    bad = watch_checks(args, ("id", "result", "note"), paths) or watch_known(args.get("id"))
    if bad:
        return bad
    errors = []
    result_ = args.get("result")
    if result_ not in ("passed", "failed"):
        errors.append("result must be passed or failed")
    note, e = watch_line(args.get("note"), "note", WATCH_MAX_NOTE)
    errors.append(e)
    errors = [x for x in errors if x]
    if errors:
        return watch_refusal("invalid_params", "; ".join(errors) + ".")
    doc = {"schema_version": 1, "op": "end", "id": args["id"], "requested_at": iso_o(now), "result": result_}
    if note is not None:
        doc["note"] = note
    failed = write_watch_request(paths, doc, now)
    if failed:
        return failed
    WATCH_ENDED.add(args["id"])
    return {"status": "ok", "id": args["id"]}


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
    if name == "get_desk_messages":
        return build_desk_messages()
    if name == "propose_desk_messages":
        return build_propose_desk_messages(args)
    if name == "propose_theme":
        return build_propose_theme(args)
    if name == "watch_start":
        return build_watch_start(args)
    if name == "watch_update":
        return build_watch_update(args)
    if name == "watch_end":
        return build_watch_end(args)
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

#!/usr/bin/env python3
"""Sanduhr statusline for Claude Code on macOS (snapshot schema v1).

Mac port of windows-dotnet/src/Sanduhr.Core/StatuslineScript.cs. Same bands, same
output, so a statusline reads the same on either machine:
  fresh (< 7.5 min)  5h 42% | wk 18% | wk resets Thu 3p
  stale (to 15 min)  same, plus "(9m ago)"
  dead  (> 15 min)   sanduhr: stale 22m - start widget
  error              sanduhr: reauth needed | last 5h 42% | wk 18%
  missing/malformed  prints nothing (the uninstalled look)
Per-model weeklies join only when hot (>= 80%). Reads the snapshot only; never
touches claude.ai or the session key. When the snapshot is dead or missing and Claude
Code's session JSON on stdin carries `rate_limits`, those numbers stand in, each marked
`*` (5h 42%* | wk 18%*). Called with no stdin (from inside another statusline script),
it waits for none.

Combine (item 63): with `--chain-b64 <base64 of a command> --join line|same`, Sanduhr
runs that command (the user's own statusline) first and prints both. Claude Code's JSON
is read from stdin once and handed to the command through `/bin/sh -c`, with the same
environment, in its own process group, within 1.5 s; a command that hangs, fails or
floods (64 KiB) never blanks Sanduhr's segment, and its group is killed on timeout and
when Claude Code cancels this script. `line` prints their rows, then Sanduhr's on its
own row; `same` appends ` | <segment>` (after an SGR reset) to their last row, falling
back to `line` when that would not fit `COLUMNS`. `--padding <n>` is the statusline's
padding, counted against the width. Exit is always 0; reasons go to stderr only.

Segments (item 63b): `--keep-theirs-b64 <base64 of JSON>` keeps only some of their segments.
Each line of theirs is cut on the separator it uses (`|`, `·`, `│`, `•`, powerline arrows, or
two-plus spaces; detected per line unless the JSON's `sep` names one), each segment is known by
its leading token (`matcher`), and segments whose matcher is dropped go, the rest rejoined on
their own separators with their colors (reset between). Segments never seen follow `new`. A line
that loses every segment goes; when nothing of theirs is left, Sanduhr's line shows alone. Any
doubt (an unfinished escape, a carriage return, an empty segment) keeps the line whole.
`--mine <list>` picks Sanduhr's own: session, weekly, resets (the default three), context,
model (the last two from Claude Code's stdin).

Usage: sanduhr_statusline.py                                    print the Sanduhr segment
       sanduhr_statusline.py --chain-b64 B --join line|same [--padding N]
                             [--keep-theirs-b64 K] [--mine M]
       sanduhr_statusline.py --inspect-b64 B                    the Combine sheet's pieces (JSON)
       sanduhr_statusline.py --compose-b64 O --join ...         the sheet's preview from output O
       SANDUHR_SNAPSHOT=path ...                                read another snapshot (tests)
"""
import base64
import binascii
import json
import os
import re
import select
import signal
import subprocess
import sys
import time
import unicodedata
from datetime import datetime, timezone

SCHEMA_VERSION = 1
FRESH_SECONDS = 450
DEAD_SECONDS = 900
SNAPSHOT = os.environ.get("SANDUHR_SNAPSHOT") or os.path.expanduser(
    "~/Library/Application Support/Sanduhr/snapshot.json")
LABELS = {"five_hour": "5h", "seven_day": "wk"}

CHAIN_BUDGET = 1.5          # seconds the user's command gets
CHAIN_CAP = 64 * 1024       # bytes of its output kept
DEPTH_ENV = "SANDUHR_CHAIN_DEPTH"
RIGHT_ROOM = 20             # columns left for Claude Code's right-side notices
SEPARATOR = " │ "      # " | " with a box-drawing bar
RESET = "\x1b[0m"
STDIN_WAIT = 0.25           # plain mode: how long to wait for stdin that may never come
B64 = re.compile(r"\A[A-Za-z0-9+/]+={0,2}\Z")

# Sanduhr's own segments, in the order they print (item 63b); the first three are the default.
MINE = ("session", "weekly", "resets", "context", "model")
MINE_DEFAULT = ("session", "weekly", "resets")


def parse(ts):
    if not ts:
        return None
    try:
        s = ts.strip().replace("Z", "+00:00")
        # Python 3.9 fromisoformat wants 0, 3 or 6 fraction digits.
        if "." in s:
            head, rest = s.split(".", 1)
            i = 0
            while i < len(rest) and rest[i].isdigit():
                i += 1
            frac, tz = rest[:i], rest[i:]
            s = head + "." + (frac + "000000")[:6] + tz
        d = datetime.fromisoformat(s)
        return d if d.tzinfo else d.replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def week_reset(reset):
    loc = reset.astimezone()
    hour = loc.strftime("%I").lstrip("0") if loc.minute == 0 else loc.strftime("%I:%M").lstrip("0")
    return "wk resets %s %s%s" % (loc.strftime("%a"), hour, "a" if loc.hour < 12 else "p")


def snapshot_age(snap, now):
    """Seconds since the snapshot was captured, or None when it can't be read."""
    if not isinstance(snap, dict):
        return None
    captured = parse(snap.get("captured_at"))
    if captured is None:
        return None
    return max(0, int((now - captured).total_seconds()))


def render_parts(snap, now):
    """The snapshot as Sanduhr's line in named parts: `{"notice": text}` for a line that stands
    whole (update, dead, or "" when unreadable), else `{"parts": [(name, text)], "error": kind
    or None, "ago": minutes or None}`. Names: session, weekly (with hot per-model weeklies),
    resets."""
    try:
        if int(snap.get("schema_version", 0)) > SCHEMA_VERSION:
            return {"notice": "sanduhr: update statusline"}
    except (TypeError, ValueError):
        return {"notice": ""}
    age = snapshot_age(snap, now)
    if age is None:
        return {"notice": ""}
    if age > DEAD_SECONDS:
        return {"notice": "sanduhr: stale %dm - start widget" % (age // 60)}

    parts, reset_suffix = [], None
    for tier in snap.get("tiers") or []:
        key = str(tier.get("key") or "")
        util = tier.get("utilization")
        label = LABELS.get(key)
        if label is None:
            if not key.startswith("seven_day_") or util is None or int(util) < 80:
                continue
            label = key[len("seven_day_"):].replace("_", " ")
        if util is None:
            continue
        reset = parse(tier.get("resets_at"))
        if reset is not None:
            if reset <= now:
                continue  # reset crossed: the stored % is arbitrarily wrong
            if key == "seven_day":
                reset_suffix = week_reset(reset)
        parts.append(("session" if key == "five_hour" else "weekly", "%s %d%%" % (label, int(util))))
    if reset_suffix and parts:
        parts.append(("resets", reset_suffix))
    error = None
    if snap.get("status") == "error":
        error = {"session_expired": "reauth needed", "cloudflare": "blocked - reauth"}.get(
            snap.get("error_kind"), "offline")
    return {"parts": parts, "error": error, "ago": age // 60 if age >= FRESH_SECONDS else None}


def format_parts(base, mine=MINE_DEFAULT, extra=(), sep=" | "):
    """Sanduhr's line from `render_parts`, keeping the parts named in `mine`, then the picked
    `extra` parts (context, model) from Claude Code's stdin, joined with `sep`. A notice always
    shows."""
    if "notice" in base:
        line = base["notice"]
    else:
        line = sep.join(text for name, text in base["parts"] if name in mine)
        if base.get("error"):
            head = "sanduhr: " + base["error"]
            line = head + " | last " + line if line else head
        elif line and base.get("ago") is not None:
            line += " (%dm ago)" % base["ago"]
    return sep.join(x for x in [line] + [text for name, text in extra if name in mine] if x)


def render(snap, now):
    return format_parts(render_parts(snap, now))


def render_stdin_parts(session, now):
    """Claude Code's own `rate_limits` (stdin), for when the snapshot is dead: the parts with
    each percent marked `*`, or None when the session JSON carries none."""
    if not isinstance(session, dict):
        return None
    limits = session.get("rate_limits")
    if not isinstance(limits, dict):
        return None
    parts, reset_suffix = [], None
    for key in ("five_hour", "seven_day"):
        window = limits.get(key)
        if not isinstance(window, dict):
            continue
        used = window.get("used_percentage")
        if isinstance(used, bool) or not isinstance(used, (int, float)):
            continue
        reset = None
        at = window.get("resets_at")
        if isinstance(at, (int, float)) and not isinstance(at, bool):
            try:
                reset = datetime.fromtimestamp(at, timezone.utc)
            except (OverflowError, OSError, ValueError):
                reset = None
        elif isinstance(at, str):
            reset = parse(at)
        if reset is not None and reset <= now:
            continue
        if key == "seven_day" and reset is not None:
            reset_suffix = week_reset(reset)
        parts.append(("session" if key == "five_hour" else "weekly", "%s %d%%*" % (LABELS[key], int(used))))
    if not parts:
        return None
    if reset_suffix:
        parts.append(("resets", reset_suffix))
    return {"parts": parts, "error": None, "ago": None}


def render_stdin(session, now):
    """`render_stdin_parts` as the line, or "" without `rate_limits`."""
    base = render_stdin_parts(session, now)
    return format_parts(base) if base else ""


def session_parts(session):
    """The parts only Claude Code's stdin knows: context used and the model's name."""
    out = []
    if not isinstance(session, dict):
        return out
    ctx = session.get("context_window")
    used = ctx.get("used_percentage") if isinstance(ctx, dict) else None
    if isinstance(used, (int, float)) and not isinstance(used, bool) and 0 <= used <= 1000:
        out.append(("context", "ctx %d%%" % int(used)))
    model = session.get("model")
    name = model.get("display_name") if isinstance(model, dict) else None
    if isinstance(name, str):
        name = "".join(ch for ch in name if unicodedata.category(ch) not in ("Cc", "Cf")).strip()[:40]
        if name:
            out.append(("model", name))
    return out


def load_snapshot():
    try:
        with open(SNAPSHOT, encoding="utf-8") as f:
            snap = json.load(f)
    except (OSError, ValueError):
        return None
    return snap if isinstance(snap, dict) else None


def segment_parts(stdin_bytes, now, mine=MINE_DEFAULT):
    """Sanduhr's line as (base, extra) for `format_parts`: the snapshot's parts, or stdin's
    `rate_limits` when the snapshot is dead or missing (`stdin_bytes` is a callable so plain
    mode reads stdin only then), and stdin's context and model when `mine` asks for them."""
    cache = []

    def session():
        if not cache:
            raw = stdin_bytes()
            value = None
            if raw:
                try:
                    value = json.loads(raw.decode("utf-8", "replace"))
                except ValueError:
                    value = None
            cache.append(value)
        return cache[0]

    snap = load_snapshot()
    age = snapshot_age(snap, now) if snap is not None else None
    if snap is not None and age is not None and age <= DEAD_SECONDS:
        base = render_parts(snap, now)
    else:
        base = render_stdin_parts(session(), now)
        if base is None:
            base = render_parts(snap, now) if snap is not None else {"notice": ""}
    extra = session_parts(session()) if "context" in mine or "model" in mine else []
    return base, extra


def segment(stdin_bytes, now, mine=MINE_DEFAULT, sep=" | "):
    """Sanduhr's text, its parts filtered by `mine` and joined with `sep`."""
    base, extra = segment_parts(stdin_bytes, now, mine)
    return format_parts(base, mine, extra, sep)


# MARK: Width

CSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
OSC = re.compile(r"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")
OTHER_ESC = re.compile(r"\x1b[@-Z\\-_]")


def visible_width(text):
    """Columns `text` takes in a terminal: CSI and OSC sequences take none, wide and
    full-width characters two, combining marks and controls none."""
    plain = OTHER_ESC.sub("", OSC.sub("", CSI.sub("", text)))
    width = 0
    for ch in plain:
        if unicodedata.combining(ch) or unicodedata.category(ch) in ("Cc", "Cf"):
            continue
        width += 2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1
    return width


def columns():
    try:
        n = int(os.environ.get("COLUMNS", ""))
    except ValueError:
        return None
    return n if n > 0 else None


def join(theirs, ours, mode, padding=0, cols=None, seam=SEPARATOR):
    """Their output (text) and Sanduhr's segment as the lines to print; `seam` goes between
    their last row and Sanduhr's on a same-row join."""
    rows = theirs.rstrip("\r\n")
    if not ours:
        return theirs
    if not rows.strip():
        return ours + "\n"
    if mode == "same":
        lines = rows.split("\n")
        last = lines[-1].rstrip("\r")
        fits = True
        if cols is not None:
            room = cols - padding - RIGHT_ROOM
            fits = visible_width(last) + visible_width(seam) + visible_width(ours) <= room
        if fits:
            lines[-1] = last + RESET + seam + ours
            return "\n".join(lines) + "\n"
    return rows + "\n" + ours + "\n"


# MARK: Their segments (item 63b)

# The separators a statusline uses between its segments, in the order a tie goes to.
SEPARATORS = (
    ("powerline", "\ue0b0"),
    ("powerline-thin", "\ue0b1"),
    ("bar", "\u2502"),
    ("pipe", "|"),
    ("bullet", "\u2022"),
    ("dot", "\u00b7"),
)
GLYPHS = dict(SEPARATORS)
SEPARATOR_IDS = tuple(i for i, _ in SEPARATORS) + ("spaces", "none")
ESCAPE = re.compile(r"\x1b(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1b]*(?:\x07|\x1b\\)|[@-Z\\^_])")
SPACES = re.compile(r"(?<=\S)[ \t]{2,}(?=\S)")
WORD = re.compile(r"[^\W\d_][\w-]*")
MAX_MATCHERS = 64
MAX_MATCHER = 64
MAX_LINES = 16


def tokenize(line):
    """`line` as [(is_escape, text)], one visible character per text token; None when the line
    holds what a splitter can't be sure of (an unfinished escape, a carriage return or another
    control character)."""
    out = []
    i = 0
    while i < len(line):
        ch = line[i]
        if ch == "\x1b":
            m = ESCAPE.match(line, i)
            if m is None:
                return None
            out.append((True, m.group(0)))
            i = m.end()
            continue
        if ch != "\t" and unicodedata.category(ch) == "Cc":
            return None
        out.append((False, ch))
        i += 1
    return out


def detect(plain):
    """The separator `plain` (a line, escapes taken out) uses: the glyph it holds most (ties
    to the order of SEPARATORS), else runs of two or more spaces, else none."""
    best, count = "none", 0
    for sid, glyph in SEPARATORS:
        n = plain.count(glyph)
        if n > count:
            best, count = sid, n
    if count:
        return best
    return "spaces" if SPACES.search(plain) else "none"


def matcher(plain):
    """A segment's leading token, escapes taken out: a word (letter first), `#` for a number,
    else the first glyph with what joins it (variation selectors, combining marks, a ZWJ
    sequence). Stable while the segment's content changes: `⎇ main`, `⎇ dev` are both `⎇`."""
    s = plain.strip()
    if not s:
        return ""
    m = WORD.match(s)
    if m:
        return m.group(0)[:MAX_MATCHER]
    if s[0].isdigit():
        return "#"
    i = 1
    while i < len(s):
        c = s[i]
        if unicodedata.category(c) in ("Mn", "Mc", "Me") or c in ("\ufe0e", "\ufe0f"):
            i += 1
        elif c == "\u200d" and i + 1 < len(s):
            i += 2
        else:
            break
    return s[:i][:MAX_MATCHER]


def _sgr_state(tokens, upto):
    """The SGR escapes in force before token `upto`, since the last reset."""
    state = []
    for is_esc, text in tokens[:upto]:
        if is_esc and text.startswith("\x1b[") and text.endswith("m"):
            params = text[2:-1]
            if params in ("", "0"):
                state = []
            elif params.startswith("0;") or params.startswith(";"):
                state = [text]
            else:
                state.append(text)
    return state


def split_line(line, sep=None):
    """`line` cut on `sep` (detected when None) into a dict: `sep`, `doubt` (True when the line
    must stay whole), and when there's no doubt `prefix`, `segments` [{plain, matcher, out}],
    `seps` [out] (the separator after each segment but the last) and `suffix`. Each `out` is
    that piece's own bytes with the colors in force before it and a reset after, so pieces
    rejoin in any subset. Caps (a separator with nothing before or after) go to the prefix or
    the suffix."""
    tokens = tokenize(line)
    if tokens is None:
        return {"sep": sep or "none", "doubt": True}
    pos = [k for k, (is_esc, _) in enumerate(tokens) if not is_esc]
    plain = "".join(tokens[k][1] for k in pos)
    n = len(plain)
    if sep is None:
        sep = detect(plain)
    # A powerline segment's padding is part of its colored block: cut on the arrow alone.
    blocks = sep == "powerline"
    if sep == "spaces":
        cuts = [m.span() for m in SPACES.finditer(plain)]
    elif blocks:
        cuts = [m.span() for m in re.finditer(re.escape(GLYPHS[sep]), plain)]
    elif sep in GLYPHS:
        cuts = [m.span() for m in re.finditer(r"[ \t]*" + re.escape(GLYPHS[sep]) + r"[ \t]*", plain)]
    else:
        cuts = []
    edges = [(0, 0)] + cuts + [(n, n)]
    pieces = [(edges[i][1], edges[i + 1][0]) for i in range(len(edges) - 1)]
    full = [i for i, (a, b) in enumerate(pieces) if plain[a:b].strip()]
    if not full or any(not plain[a:b].strip() for a, b in pieces[full[0]:full[-1] + 1]):
        return {"sep": sep, "doubt": True}
    spans = []
    for a, b in pieces[full[0]:full[-1] + 1]:
        while not blocks and plain[a].isspace():
            a += 1
        while not blocks and plain[b - 1].isspace():
            b -= 1
        spans.append((a, b))

    def tstart(i):
        return 0 if i == 0 else pos[i - 1] + 1

    def out(a, b, last=False):
        ts = tstart(a)
        te = len(tokens) if last else tstart(b)
        piece = tokens[ts:te]
        if last and all(is_esc for is_esc, _ in piece):
            return ""  # only escapes after the last segment: each piece already resets
        state = _sgr_state(tokens, ts)
        for is_esc, text in piece:
            if not is_esc:
                break
            if text.startswith("\x1b[") and text.endswith("m") and (text[2:-1] in ("", "0") or text[2:].startswith("0;")):
                state = []  # the piece starts by resetting: what came before doesn't matter
        open_after = _sgr_state(tokens, te)
        return "".join(state) + "".join(text for _, text in piece) + (RESET if open_after and not last else "")

    segments = [{"plain": plain[a:b].strip(), "matcher": matcher(plain[a:b]), "out": out(a, b),
                 "bg": _background(_sgr_state(tokens, tstart(b)))} for a, b in spans]
    seps = [out(spans[k][1], spans[k + 1][0]) for k in range(len(spans) - 1)]
    prefix = "".join(text for _, text in tokens[:tstart(spans[0][0])])
    return {"sep": sep, "doubt": False, "prefix": prefix, "segments": segments, "seps": seps,
            "suffix": out(spans[-1][1], n, last=True), "cap": blocks and GLYPHS[sep] in plain[spans[-1][1]:]}


def _background(state):
    """The background the SGR escapes in `state` leave set, as SGR parameters ("44",
    "48;5;23", "48;2;1;2;3"), or None for the terminal's own."""
    bg = None
    for esc in state:
        codes = esc[2:-1].split(";")
        i = 0
        while i < len(codes):
            c = codes[i]
            if c in ("", "0", "49"):
                bg = None
            elif c.isdigit() and (40 <= int(c) <= 47 or 100 <= int(c) <= 107):
                bg = c
            elif c in ("38", "48") and i + 1 < len(codes):
                width = {"5": 1, "2": 3}.get(codes[i + 1], 0)
                if width and i + 1 + width < len(codes):
                    if c == "48":
                        bg = ";".join(codes[i:i + 2 + width])
                    i += 1 + width
            i += 1
    return bg


# Join with (item 63b): what goes between segments when the user picks a glyph of their own.
JOIN_WITH = {
    "bar": " \u2502 ",
    "pipe": " | ",
    "dot": " \u00b7 ",
    "bullet": " \u2022 ",
    "powerline-thin": " \ue0b1 ",
    "spaces": "  ",
}
JOIN_WITH_IDS = tuple(JOIN_WITH) + ("powerline",)


def _glue(glue, left=None, right=None, blocks=False):
    """The text Join with puts between two segments: the glyph with a space each side, or a
    powerline arrow in the two segments' backgrounds (padded unless the segments are powerline
    blocks with padding of their own)."""
    if glue != "powerline":
        return JOIN_WITH[glue]
    arrow = _arrow(left, right, GLYPHS["powerline"])
    return arrow if blocks else " " + arrow + " "


def _arrow(left, right, glyph):
    """A powerline arrow drawn from a segment with background `left` into one with `right`
    (None: the terminal's own), as their own arrows are when the two are neighbors."""
    if left is None:
        fg = "39"
    elif left.startswith("48;"):
        fg = "38;" + left[3:]
    else:
        fg = str(int(left) - 10)
    return "\x1b[0;%s;%sm%s%s" % (fg, right or "49", glyph, RESET)


def _lines(text):
    """`text` as (lines, ended with a newline); a line keeps no `\\r` of its CRLF."""
    ended = text.endswith("\n")
    body = text[:-1] if ended else text
    return [line[:-1] if line.endswith("\r") else line for line in body.split("\n")], ended


def keeps(m, picks):
    """Whether a segment led by `m` stays: dropped and kept matchers first, new ones per `new`."""
    if m in (picks.get("drop") or ()):
        return False
    if m in (picks.get("keep") or ()):
        return True
    return picks.get("new", True) is not False


def filter_line(line, picks, index):
    """`line` with the segments `picks` drops taken out, rejoined on its own separators; the line
    itself when nothing drops or on any doubt, None when nothing of it stays."""
    seps = picks.get("sep") or []
    sp = split_line(line, seps[index] if index < len(seps) else None)
    if sp["doubt"]:
        return line
    kept = [k for k, seg in enumerate(sp["segments"]) if keeps(seg["matcher"], picks)]
    glue = picks.get("with")
    if len(kept) == len(sp["segments"]) and not glue:
        return line
    if not kept:
        return None
    segs = sp["segments"]
    # Powerline arrows take their colors from both neighbors: one between segments that weren't
    # neighbors, or a cap after a new last segment, is drawn again in the new neighbors' colors.
    glyph = GLYPHS.get(sp["sep"]) if sp["sep"] == "powerline" else None
    out = [sp["prefix"]]
    for j, k in enumerate(kept):
        out.append(segs[k]["out"])
        if j + 1 < len(kept):
            nxt = kept[j + 1]
            if glue:
                out.append(_glue(glue, segs[k]["bg"], segs[nxt]["bg"], blocks=sp["sep"] == "powerline"))
            elif glyph and nxt != k + 1:
                out.append(_arrow(segs[k]["bg"], segs[nxt]["bg"], glyph))
            else:
                out.append(sp["seps"][k])
    if glyph and sp["cap"] and kept[-1] != len(segs) - 1:
        out.append(_arrow(segs[kept[-1]]["bg"], None, glyph))
    else:
        out.append(sp["suffix"])
    return "".join(out)


def filter_theirs(text, picks):
    """Their output with `picks` applied line by line: a line that loses every segment goes, and
    when nothing visible of theirs is left, the result is empty (Sanduhr's line still shows)."""
    if not text or picks is None:
        return text
    lines, ended = _lines(text)
    out = []
    for i, line in enumerate(lines):
        if not line.strip():
            out.append(line)
            continue
        kept = filter_line(line, picks, i)
        if kept is not None:
            out.append(kept)
    if not any(visible_width(line) for line in out):
        return ""
    return "\n".join(out) + ("\n" if ended else "")


def parse_picks(b64):
    """The `--keep-theirs-b64` payload: base64 of a JSON object with only `keep` and `drop`
    (lists of matchers), `new` (keep segments never seen, default true) and `sep` (per line,
    a separator id or null for detected). None when it is anything else."""
    if not B64.match(b64) or len(b64) % 4:
        return None
    try:
        picks = json.loads(base64.b64decode(b64, validate=True).decode("utf-8"))
    except (binascii.Error, ValueError):
        return None
    if not isinstance(picks, dict) or set(picks) - {"keep", "drop", "new", "sep", "with"}:
        return None
    for key in ("keep", "drop"):
        if key in picks:
            v = picks[key]
            if not isinstance(v, list) or len(v) > MAX_MATCHERS:
                return None
            if not all(isinstance(m, str) and 0 < len(m) <= MAX_MATCHER for m in v):
                return None
    if "new" in picks and not isinstance(picks["new"], bool):
        return None
    if "with" in picks and picks["with"] not in JOIN_WITH_IDS:
        return None
    if "sep" in picks:
        v = picks["sep"]
        if not isinstance(v, list) or len(v) > MAX_LINES:
            return None
        if not all(s is None or s in SEPARATOR_IDS for s in v):
            return None
    return picks


def parse_mine(text):
    """The `--mine` list: Sanduhr's segment names, comma-separated, in MINE's order, each once.
    None when it is anything else."""
    names = text.split(",")
    try:
        idx = [MINE.index(n) for n in names]
    except ValueError:
        return None
    if any(b <= a for a, b in zip(idx, idx[1:])):
        return None
    return tuple(names)


# MARK: The chained command

_child = None


def _kill_group(proc):
    try:
        os.killpg(proc.pid, signal.SIGKILL)
    except (ProcessLookupError, PermissionError, OSError):
        pass


def _on_signal(signum, _frame):
    """Claude Code cancelled this run: take the user's command (and its group) down too."""
    if _child is not None:
        _kill_group(_child)
    os._exit(0)


def run_chain(command, data, budget=CHAIN_BUDGET, cap=CHAIN_CAP):
    """Runs `command` through /bin/sh with `data` on stdin. Returns (stdout bytes, reason)
    where reason is None on a clean exit, else why its output may be partial or missing."""
    global _child
    env = dict(os.environ)
    env[DEPTH_ENV] = "1"
    try:
        proc = subprocess.Popen(["/bin/sh", "-c", command], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, env=env, start_new_session=True)
    except OSError as e:
        return b"", "could not start: %s" % e.__class__.__name__
    _child = proc
    deadline = time.monotonic() + budget
    out = bytearray()
    pending = memoryview(data or b"")
    reason = None
    stdin_fd = proc.stdin.fileno()
    stdout_fd = proc.stdout.fileno()
    os.set_blocking(stdin_fd, False)
    if not pending:
        proc.stdin.close()
        stdin_fd = None
    try:
        while True:
            left = deadline - time.monotonic()
            if left <= 0:
                reason = "timed out after %.1fs" % budget
                break
            readers = [stdout_fd]
            writers = [stdin_fd] if stdin_fd is not None else []
            r, w, _ = select.select(readers, writers, [], left)
            if w:
                try:
                    n = os.write(stdin_fd, pending[:65536])
                    pending = pending[n:]
                except BlockingIOError:
                    pass
                except (BrokenPipeError, OSError):
                    pending = pending[len(pending):]
                if not pending:
                    try:
                        proc.stdin.close()
                    except OSError:
                        pass
                    stdin_fd = None
            if r:
                chunk = os.read(stdout_fd, 65536)
                if not chunk:
                    break
                out.extend(chunk)
                if len(out) > cap:
                    del out[cap:]
                    reason = "output over %d bytes, cut" % cap
                    break
        if reason is None:
            try:
                code = proc.wait(timeout=max(0.0, deadline - time.monotonic()))
                if code != 0:
                    reason = "exited %d" % code
            except subprocess.TimeoutExpired:
                reason = "timed out after %.1fs" % budget
                _kill_group(proc)
        else:
            # Timed out or flooding: the whole group goes, whatever it spawned.
            _kill_group(proc)
    finally:
        if stdin_fd is not None:
            try:
                proc.stdin.close()
            except OSError:
                pass
        try:
            proc.stdout.close()
        except OSError:
            pass
        if proc.poll() is None:
            _kill_group(proc)
        try:
            proc.wait(timeout=1)
        except subprocess.TimeoutExpired:
            pass
        _child = None
    return bytes(out), reason


def parse_args(argv, flag="--chain-b64", blank=False):
    """`[]` for plain mode, `(command, join, padding, picks, mine)` for Combine, or None when the
    arguments aren't exactly the grammar:
        --chain-b64 B --join line|same [--padding N] [--keep-theirs-b64 K] [--mine M]
    `picks` is None (keep all of theirs) or `parse_picks`'s object, `mine` None (the default
    three) or `parse_mine`'s names. `flag` and `blank` serve the preview's compose mode, whose
    payload is their output (possibly empty) instead of a command."""
    if not argv:
        return []
    if len(argv) < 4 or argv[0] != flag or argv[2] != "--join":
        return None
    b64, mode = argv[1], argv[3]
    if mode not in ("line", "same"):
        return None
    if blank and b64 == "":
        command = ""
    else:
        if not B64.match(b64) or len(b64) % 4:
            return None
        try:
            command = base64.b64decode(b64, validate=True).decode("utf-8")
        except (binascii.Error, ValueError):
            return None
        if not blank and not command.strip():
            return None
    rest = argv[4:]
    padding, picks, mine = 0, None, None
    if rest[:1] == ["--padding"]:
        if len(rest) < 2 or not re.match(r"\A[0-9]{1,3}\Z", rest[1]):
            return None
        padding = int(rest[1])
        rest = rest[2:]
    if rest[:1] == ["--keep-theirs-b64"]:
        picks = parse_picks(rest[1]) if len(rest) >= 2 else None
        if picks is None:
            return None
        rest = rest[2:]
    if rest[:1] == ["--mine"]:
        mine = parse_mine(rest[1]) if len(rest) >= 2 else None
        if mine is None:
            return None
        rest = rest[2:]
    if rest:
        return None
    return (command, mode, padding, picks, mine)


def combined(theirs, data, now, mode, padding, picks, mine, cols):
    """What a combined statusline prints: their output (text) with `picks` applied, joined
    with Sanduhr's segment, its parts filtered by `mine`. A `with` pick (Join with) sets the
    glyph between segments everywhere in the line: theirs, Sanduhr's and the seam."""
    glue = (picks or {}).get("with")
    sep = _glue(glue) if glue else " | "
    ours = segment(lambda: data, now, mine or MINE_DEFAULT, sep)
    return join(filter_theirs(theirs, picks), ours, mode, padding, cols, _glue(glue) if glue else SEPARATOR)


def inspect(theirs, data, now):
    """For the Combine sheet's chips: their output split per line under every separator (the
    detected one named), and each of Sanduhr's segments on its own."""
    lines = []
    for text in _lines(theirs)[0] if theirs else []:
        tokens = tokenize(text)
        plain = "".join(t for e, t in tokens if not e) if tokens is not None else ""
        splits = {}
        for sid in SEPARATOR_IDS:
            sp = split_line(text, sid)
            splits[sid] = {"doubt": sp["doubt"], "segments": [
                {"matcher": s["matcher"], "text": s["plain"]} for s in sp.get("segments", [])]}
        lines.append({"text": text, "auto": detect(plain) if tokens is not None else "none", "splits": splits})
    base, extra = segment_parts(lambda: data, now, MINE)
    parts = base.get("parts", []) + extra
    mine = [{"name": n, "text": " | ".join(t for name, t in parts if name == n)} for n in MINE]
    return {"theirs": theirs, "lines": lines, "mine": mine, "notice": base.get("notice", "")}


def read_stdin_all():
    try:
        if sys.stdin is None or sys.stdin.isatty():
            return b""
        return sys.stdin.buffer.read()
    except (OSError, ValueError):
        return b""


def read_stdin_patiently(wait=STDIN_WAIT):
    """Stdin when it comes promptly: a script calling this one may give it none."""
    try:
        if sys.stdin is None or sys.stdin.isatty():
            return b""
        fd = sys.stdin.fileno()
    except (OSError, ValueError):
        return b""
    out = bytearray()
    deadline = time.monotonic() + wait
    while len(out) < CHAIN_CAP:
        left = deadline - time.monotonic()
        if left <= 0:
            break
        r, _, _ = select.select([fd], [], [], left)
        if not r:
            break
        chunk = os.read(fd, 65536)
        if not chunk:
            break
        out.extend(chunk)
    return bytes(out)


def say(reason):
    try:
        sys.stderr.write("sanduhr statusline: %s\n" % reason)
    except (OSError, ValueError):
        pass


def write(text):
    try:
        sys.stdout.write(text)
        sys.stdout.flush()
    except (OSError, ValueError):
        pass


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    now = datetime.now(timezone.utc)
    if argv[:1] == ["--inspect-b64"]:
        # The Combine sheet (never a statusline): run theirs once, print the pieces as JSON.
        args = parse_args(["--chain-b64", argv[1] if len(argv) > 1 else "", "--join", "line"])
        if len(argv) != 2 or not args:
            say("--inspect-b64 takes one base64 command")
            return 0
        data = read_stdin_all()
        theirs, reason = run_chain(args[0], data)
        if reason:
            say("your statusline " + reason)
        write(json.dumps(inspect(theirs.decode("utf-8", "replace"), data, now), ensure_ascii=False) + "\n")
        return 0
    if argv[:1] == ["--compose-b64"]:
        # The Combine sheet's live preview: what the combined line prints, from their output.
        args = parse_args(argv, flag="--compose-b64", blank=True)
        if not args:
            say("--compose-b64 takes the combined grammar with their output")
            return 0
        theirs, mode, padding, picks, mine = args
        write(combined(theirs, read_stdin_all(), now, mode, padding, picks, mine, columns()))
        return 0

    args = parse_args(argv)
    if args is None:
        say("arguments aren't --chain-b64 <base64> --join line|same [--padding n] "
            "[--keep-theirs-b64 <base64>] [--mine <list>]; showing Sanduhr only")
        args = []
    if not args:
        out = segment(read_stdin_patiently, now)
        if out:
            print(out)
        return 0

    command, mode, padding, picks, mine = args
    signal.signal(signal.SIGTERM, _on_signal)
    signal.signal(signal.SIGINT, _on_signal)
    data = read_stdin_all()
    theirs = b""
    if os.environ.get(DEPTH_ENV):
        say("already inside a combined statusline; not chaining again")
    else:
        theirs, reason = run_chain(command, data)
        if reason:
            say("your statusline " + reason)
    write(combined(theirs.decode("utf-8", "replace"), data, now, mode, padding, picks, mine, columns()))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:  # never a non-zero exit: Claude Code would blank the row
        say("failed: %s" % e.__class__.__name__)
        sys.exit(0)

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

Usage: sanduhr_statusline.py                                    print the Sanduhr segment
       sanduhr_statusline.py --chain-b64 B --join line|same [--padding N]
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


def render(snap, now):
    try:
        if int(snap.get("schema_version", 0)) > SCHEMA_VERSION:
            return "sanduhr: update statusline"
    except (TypeError, ValueError):
        return ""
    age = snapshot_age(snap, now)
    if age is None:
        return ""
    if age > DEAD_SECONDS:
        return "sanduhr: stale %dm - start widget" % (age // 60)

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
        parts.append("%s %d%%" % (label, int(util)))

    line = " | ".join(parts)
    if reset_suffix and line:
        line += " | " + reset_suffix
    if snap.get("status") == "error":
        kind = {"session_expired": "reauth needed", "cloudflare": "blocked - reauth"}.get(
            snap.get("error_kind"), "offline")
        return "sanduhr: %s | last %s" % (kind, line) if line else "sanduhr: " + kind
    if not line:
        return ""
    if age >= FRESH_SECONDS:
        line += " (%dm ago)" % (age // 60)
    return line


def render_stdin(session, now):
    """Claude Code's own `rate_limits` (stdin), for when the snapshot is dead: the same
    line with each percent marked `*`, or "" when the session JSON carries none."""
    if not isinstance(session, dict):
        return ""
    limits = session.get("rate_limits")
    if not isinstance(limits, dict):
        return ""
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
        parts.append("%s %d%%*" % (LABELS[key], int(used)))
    line = " | ".join(parts)
    if reset_suffix and line:
        line += " | " + reset_suffix
    return line


def load_snapshot():
    try:
        with open(SNAPSHOT, encoding="utf-8") as f:
            snap = json.load(f)
    except (OSError, ValueError):
        return None
    return snap if isinstance(snap, dict) else None


def segment(stdin_bytes, now):
    """Sanduhr's text: the snapshot's line, or stdin's `rate_limits` when the snapshot is
    dead or missing (`stdin_bytes` is a callable so plain mode reads stdin only then)."""
    snap = load_snapshot()
    age = snapshot_age(snap, now) if snap is not None else None
    if snap is not None and age is not None and age <= DEAD_SECONDS:
        return render(snap, now)
    raw = stdin_bytes()
    session = None
    if raw:
        try:
            session = json.loads(raw.decode("utf-8", "replace"))
        except ValueError:
            session = None
    fallback = render_stdin(session, now)
    if fallback:
        return fallback
    return render(snap, now) if snap is not None else ""


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


def join(theirs, ours, mode, padding=0, cols=None):
    """Their output (text) and Sanduhr's segment as the lines to print."""
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
            fits = visible_width(last) + visible_width(SEPARATOR) + visible_width(ours) <= room
        if fits:
            lines[-1] = last + RESET + SEPARATOR + ours
            return "\n".join(lines) + "\n"
    return rows + "\n" + ours + "\n"


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


def parse_args(argv):
    """`[]` for plain mode, `(command, join, padding)` for Combine, or None when the
    arguments aren't exactly the grammar."""
    if not argv:
        return []
    if len(argv) not in (4, 6) or argv[0] != "--chain-b64" or argv[2] != "--join":
        return None
    b64, mode = argv[1], argv[3]
    if mode not in ("line", "same") or not B64.match(b64) or len(b64) % 4:
        return None
    padding = 0
    if len(argv) == 6:
        if argv[4] != "--padding" or not argv[5].isdigit() or len(argv[5]) > 3:
            return None
        padding = int(argv[5])
    try:
        command = base64.b64decode(b64, validate=True).decode("utf-8")
    except (binascii.Error, ValueError):
        return None
    if not command.strip():
        return None
    return (command, mode, padding)


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


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    now = datetime.now(timezone.utc)
    args = parse_args(argv)
    if args is None:
        say("arguments aren't --chain-b64 <base64> --join line|same [--padding n]; showing Sanduhr only")
        args = []
    if not args:
        out = segment(read_stdin_patiently, now)
        if out:
            print(out)
        return 0

    command, mode, padding = args
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
    ours = segment(lambda: data, now)
    text = join(theirs.decode("utf-8", "replace"), ours, mode, padding, columns())
    try:
        sys.stdout.write(text)
        sys.stdout.flush()
    except (OSError, ValueError):
        pass
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:  # never a non-zero exit: Claude Code would blank the row
        say("failed: %s" % e.__class__.__name__)
        sys.exit(0)

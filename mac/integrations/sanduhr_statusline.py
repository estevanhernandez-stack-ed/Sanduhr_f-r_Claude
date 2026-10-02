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
touches claude.ai or the session key. Claude Code's session JSON on stdin is ignored,
so this can also be called from inside another statusline script.

Usage: sanduhr_statusline.py            print the Sanduhr segment
       SANDUHR_SNAPSHOT=path ...        read another snapshot (tests)
"""
import json
import os
import sys
from datetime import datetime, timezone

SCHEMA_VERSION = 1
FRESH_SECONDS = 450
DEAD_SECONDS = 900
SNAPSHOT = os.environ.get("SANDUHR_SNAPSHOT") or os.path.expanduser(
    "~/Library/Application Support/Sanduhr/snapshot.json")
LABELS = {"five_hour": "5h", "seven_day": "wk"}


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


def render(snap, now):
    try:
        if int(snap.get("schema_version", 0)) > SCHEMA_VERSION:
            return "sanduhr: update statusline"
    except (TypeError, ValueError):
        return ""
    captured = parse(snap.get("captured_at"))
    if captured is None:
        return ""
    age = max(0, int((now - captured).total_seconds()))
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
                loc = reset.astimezone()
                hour = loc.strftime("%I").lstrip("0") if loc.minute == 0 else loc.strftime("%I:%M").lstrip("0")
                reset_suffix = "wk resets %s %s%s" % (loc.strftime("%a"), hour, "a" if loc.hour < 12 else "p")
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


def main():
    try:
        with open(SNAPSHOT, encoding="utf-8") as f:
            snap = json.load(f)
    except (OSError, ValueError):
        return 0
    if not isinstance(snap, dict):
        return 0
    out = render(snap, datetime.now(timezone.utc))
    if out:
        print(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

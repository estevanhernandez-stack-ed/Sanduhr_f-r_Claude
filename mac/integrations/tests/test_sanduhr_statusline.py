"""Tests for mac/integrations/sanduhr_statusline.py, the Combine runner (item 63): the flag
grammar, the 1.5 s budget and process-group kill, exit codes, the ANSI reset on a same-line
join, the width fallback, the depth guard and the stdin fallback for a dead snapshot. Temp
folders and made-up numbers only; the script runs as Claude Code runs it, as a process.
Standard library only:

    python3 -m unittest discover -s mac/integrations/tests
"""
import base64
import importlib.util
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(os.path.dirname(HERE), "sanduhr_statusline.py")

spec = importlib.util.spec_from_file_location("sanduhr_statusline", SCRIPT)
sl = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sl)


def b64(command):
    return base64.b64encode(command.encode("utf-8")).decode("ascii")


def iso(t):
    return t.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f+00:00")


def gone(pid, within=1.0):
    """The process `pid` has ended (and been reaped) within `within` seconds."""
    deadline = time.monotonic() + within
    while time.monotonic() < deadline:
        try:
            os.kill(pid, 0)
        except ProcessLookupError:
            return True
        except PermissionError:
            return False
        time.sleep(0.02)
    return False


# The docs' statusline JSON, trimmed, with made-up numbers.
def session_json(five=23.5, week=41.2):
    now = int(time.time())
    return json.dumps({
        "session_id": "test-session-abc",
        "model": {"id": "claude-opus-5-5", "display_name": "Opus"},
        "workspace": {"current_dir": "/home/user/project", "project_dir": "/home/user/project"},
        "context_window": {"used_percentage": 8, "remaining_percentage": 92},
        "rate_limits": {
            "five_hour": {"used_percentage": five, "resets_at": now + 3600},
            "seven_day": {"used_percentage": week, "resets_at": now + 3 * 86400},
        },
    }).encode("utf-8")


class Rig:
    def __init__(self):
        self.dir = tempfile.mkdtemp(prefix="sanduhr-statusline-test-")
        self.snapshot = os.path.join(self.dir, "snapshot.json")

    def close(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def path(self, name):
        return os.path.join(self.dir, name)

    def fresh(self, five=42, week=18):
        now = datetime.now(timezone.utc)
        snap = {"schema_version": 1, "captured_at": iso(now), "status": "ok", "tiers": [
            {"key": "five_hour", "utilization": five, "resets_at": iso(now + timedelta(hours=2))},
            {"key": "seven_day", "utilization": week, "resets_at": iso(now + timedelta(days=3))},
        ]}
        with open(self.snapshot, "w", encoding="utf-8") as f:
            json.dump(snap, f)

    def dead(self):
        now = datetime.now(timezone.utc)
        snap = {"schema_version": 1, "captured_at": iso(now - timedelta(minutes=40)), "status": "ok",
                "tiers": [{"key": "five_hour", "utilization": 99}]}
        with open(self.snapshot, "w", encoding="utf-8") as f:
            json.dump(snap, f)

    def env(self, **extra):
        env = {k: v for k, v in os.environ.items() if k not in (sl.DEPTH_ENV, "COLUMNS")}
        env.update({"SANDUHR_SNAPSHOT": self.snapshot, "TZ": "UTC"})
        env.update(extra)
        return env

    def run(self, args, stdin=b"", **env):
        started = time.monotonic()
        p = subprocess.run([sys.executable, SCRIPT] + args, input=stdin, capture_output=True,
                           env=self.env(**env), timeout=20)
        return p, time.monotonic() - started

    def chain(self, command, join="line", stdin=None, **env):
        return self.run(["--chain-b64", b64(command), "--join", join],
                        session_json() if stdin is None else stdin, **env)


class GrammarTests(unittest.TestCase):
    def test_plain_and_combined(self):
        self.assertEqual(sl.parse_args([]), [])
        self.assertEqual(sl.parse_args(["--chain-b64", b64("~/bin/line.sh"), "--join", "line"]),
                         ("~/bin/line.sh", "line", 0, None, None))
        self.assertEqual(sl.parse_args(["--chain-b64", b64("a | b"), "--join", "same", "--padding", "2"]),
                         ("a | b", "same", 2, None, None))

    def test_anything_else_is_refused(self):
        good = b64("echo hi")
        for argv in (
            ["--join", "line", "--chain-b64", good],
            ["--chain-b64", good],
            ["--chain-b64", good, "--join", "both"],
            ["--chain-b64", good, "--join", "line", "--padding"],
            ["--chain-b64", good, "--join", "line", "--padding", "-1"],
            ["--chain-b64", good, "--join", "line", "--padding", "1000"],
            ["--chain-b64", good, "--join", "line", "--extra", "1"],
            ["--chain-b64", good + "!", "--join", "line"],
            ["--chain-b64", "ZWNobw", "--join", "line"],              # unpadded
            ["--chain-b64", "ZWNobyBoaQ==\n", "--join", "line"],
            ["--chain-b64", base64.b64encode(b"\xff\xfe").decode(), "--join", "line"],  # not UTF-8
            ["--chain-b64", b64("   "), "--join", "line"],
            ["--chain-b64=" + good, "--join", "line"],
        ):
            self.assertIsNone(sl.parse_args(argv), argv)

    def test_bad_arguments_still_show_sanduhr(self):
        r = Rig()
        try:
            r.fresh()
            p, _ = r.run(["--chain-b64", "!!", "--join", "line"])
            self.assertEqual(p.returncode, 0)
            self.assertTrue(p.stdout.decode().startswith("5h 42% | wk 18%"))
            self.assertIn(b"arguments", p.stderr)
        finally:
            r.close()


class WidthTests(unittest.TestCase):
    def test_escapes_take_no_columns_and_wide_chars_two(self):
        self.assertEqual(sl.visible_width("\x1b[1;38;2;255;0;0mab\x1b[0m"), 2)
        self.assertEqual(sl.visible_width("\x1b]8;;https://x.test\x1b\\link\x1b]8;;\x1b\\"), 4)
        self.assertEqual(sl.visible_width("\x1b]8;;https://x.test\x07go\x1b]8;;\x07"), 2)
        self.assertEqual(sl.visible_width("日本"), 4)
        self.assertEqual(sl.visible_width("é"), 1)

    def test_same_falls_back_to_line_when_it_would_not_fit(self):
        theirs = "x" * 60 + "\n"
        self.assertEqual(sl.join(theirs, "5h 42%", "same", 0, 200), "x" * 60 + sl.RESET + sl.SEPARATOR + "5h 42%\n")
        # 60 + 3 + 6 = 69 > 80 - 0 - 20.
        self.assertEqual(sl.join(theirs, "5h 42%", "same", 0, 80), "x" * 60 + "\n5h 42%\n")
        # Padding counts against the room: 69 fits 100 - 0 - 20, not 100 - 12 - 20.
        self.assertIn(sl.SEPARATOR, sl.join(theirs, "5h 42%", "same", 0, 100))
        self.assertNotIn(sl.SEPARATOR, sl.join(theirs, "5h 42%", "same", 12, 100))
        # Escapes don't count toward their width.
        colored = "\x1b[31m" + "x" * 50 + "\x1b[0m"
        self.assertIn(sl.SEPARATOR, sl.join(colored, "5h", "same", 0, 80))

    def test_join_shapes(self):
        self.assertEqual(sl.join("a\nb\n", "5h", "line"), "a\nb\n5h\n")
        self.assertEqual(sl.join("a\nb", "5h", "same"), "a\nb" + sl.RESET + sl.SEPARATOR + "5h\n")
        self.assertEqual(sl.join("", "5h", "same"), "5h\n")
        # Sanduhr has nothing: theirs exactly as they printed it.
        self.assertEqual(sl.join("\x1b[1mmine\x1b[0m  \n", "", "same"), "\x1b[1mmine\x1b[0m  \n")


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.r = Rig()
        self.r.fresh()

    def tearDown(self):
        self.r.close()

    def test_a_hanging_command_is_killed_with_its_group_and_sanduhr_still_shows(self):
        pidfile = self.r.path("sleeper.pid")
        p, took = self.r.chain("sleep 5 & echo $! > '%s'; echo $$ >> '%s'; wait" % (pidfile, pidfile))
        self.assertEqual(p.returncode, 0)
        self.assertLess(took, 2.5)          # 1.5 s budget plus Python's start
        self.assertEqual(p.stdout.decode(), "5h 42% | wk 18% | " + p.stdout.decode().split(" | ", 2)[2])
        self.assertIn(b"timed out", p.stderr)
        with open(pidfile) as f:
            pids = [int(x) for x in f.read().split()]
        self.assertEqual(len(pids), 2)
        for pid in pids:
            self.assertTrue(gone(pid), "pid %d outlived the statusline" % pid)

    def test_run_chain_returns_within_the_budget(self):
        started = time.monotonic()
        out, reason = sl.run_chain("printf early; sleep 5", b"{}")
        took = time.monotonic() - started
        self.assertLess(took, 1.7)
        self.assertGreaterEqual(took, 1.4)
        self.assertEqual(out, b"early")
        self.assertIn("timed out", reason)

    def test_a_failing_command_keeps_its_output_and_the_exit_is_zero(self):
        p, _ = self.r.chain("printf 'mine\\n'; exit 3")
        self.assertEqual(p.returncode, 0)
        self.assertTrue(p.stdout.decode().startswith("mine\n5h 42%"))
        self.assertIn(b"exited 3", p.stderr)
        self.assertNotIn(b"exited", p.stdout)

    def test_a_failing_command_without_output_still_shows_sanduhr(self):
        p, _ = self.r.chain("exit 1")
        self.assertEqual(p.returncode, 0)
        self.assertTrue(p.stdout.decode().startswith("5h 42% | wk 18%"))
        p, _ = self.r.chain("/no/such/command-here")
        self.assertEqual(p.returncode, 0)
        self.assertTrue(p.stdout.decode().startswith("5h 42%"))

    def test_same_line_join_resets_their_open_ansi(self):
        p, _ = self.r.chain("printf '\\033[1;34mmine'", join="same", COLUMNS="200")
        out = p.stdout.decode()
        self.assertTrue(out.startswith("\x1b[1;34mmine\x1b[0m │ 5h 42%"), repr(out))
        self.assertEqual(out.count("\n"), 1)

    def test_same_line_join_falls_back_on_a_narrow_terminal(self):
        p, _ = self.r.chain("printf '%s'" % ("x" * 70), join="same", COLUMNS="80")
        self.assertTrue(p.stdout.decode().startswith("x" * 70 + "\n5h 42%"))

    def test_the_command_gets_the_same_stdin_and_environment(self):
        seen = self.r.path("seen.json")
        envfile = self.r.path("env.txt")
        stdin = session_json(five=11)
        p, _ = self.r.chain("cat > '%s'; printf '%%s %%s' \"$COLUMNS\" \"$%s\" > '%s'; echo ok"
                            % (seen, sl.DEPTH_ENV, envfile), stdin=stdin, COLUMNS="123")
        with open(seen, "rb") as f:
            self.assertEqual(f.read(), stdin)
        with open(envfile) as f:
            self.assertEqual(f.read(), "123 1")
        self.assertTrue(p.stdout.decode().startswith("ok\n"))

    def test_the_depth_guard_never_chains_twice(self):
        marker = self.r.path("ran")
        p, _ = self.r.chain("touch '%s'; echo mine" % marker, **{sl.DEPTH_ENV: "1"})
        self.assertEqual(p.returncode, 0)
        self.assertFalse(os.path.exists(marker))
        self.assertTrue(p.stdout.decode().startswith("5h 42%"))
        self.assertIn(b"already inside", p.stderr)

    def test_flooding_output_is_capped(self):
        p, took = self.r.chain("yes mine")
        self.assertEqual(p.returncode, 0)
        self.assertLess(took, 2.5)
        lines = p.stdout.decode().split("\n")
        self.assertLessEqual(len(p.stdout), sl.CHAIN_CAP + 200)
        self.assertTrue(lines[-2].startswith("5h 42%"))

    def test_sigterm_takes_the_command_group_down(self):
        pidfile = self.r.path("sleeper.pid")
        proc = subprocess.Popen([sys.executable, SCRIPT, "--chain-b64",
                                 b64("sleep 5 & echo $! > '%s'; wait" % pidfile), "--join", "line"],
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                env=self.r.env())
        proc.stdin.write(session_json())
        proc.stdin.close()
        deadline = time.monotonic() + 1.2
        while time.monotonic() < deadline and not os.path.exists(pidfile):
            time.sleep(0.02)
        time.sleep(0.05)
        with open(pidfile) as f:
            pid = int(f.read().strip())
        proc.send_signal(signal.SIGTERM)
        self.assertEqual(proc.wait(timeout=5), 0)
        proc.stdout.close()
        proc.stderr.close()
        self.assertTrue(gone(pid), "the sleeper outlived a cancelled statusline")

    def test_a_quiet_sanduhr_leaves_their_output_untouched(self):
        os.remove(self.r.snapshot)
        p, _ = self.r.chain("printf '\\033[1mmine\\033[0m  \\n'", join="same", stdin=b"{}")
        self.assertEqual(p.stdout, b"\x1b[1mmine\x1b[0m  \n")


class FallbackTests(unittest.TestCase):
    def setUp(self):
        self.r = Rig()

    def tearDown(self):
        self.r.close()

    def test_a_dead_snapshot_falls_back_on_stdin_marked(self):
        self.r.dead()
        p, _ = self.r.run([], stdin=session_json())
        self.assertTrue(p.stdout.decode().startswith("5h 23%* | wk 41%* | wk resets "), p.stdout)
        p, _ = self.r.chain("echo mine")
        self.assertTrue(p.stdout.decode().startswith("mine\n5h 23%* | wk 41%*"))

    def test_a_missing_snapshot_falls_back_too_and_nothing_without_rate_limits(self):
        p, _ = self.r.run([], stdin=session_json(five=5))
        self.assertTrue(p.stdout.decode().startswith("5h 5%*"))
        p, _ = self.r.run([], stdin=b'{"model": {"display_name": "Opus"}}')
        self.assertEqual(p.stdout, b"")

    def test_dead_without_rate_limits_keeps_the_stale_notice(self):
        self.r.dead()
        p, _ = self.r.run([], stdin=b"{}")
        self.assertTrue(p.stdout.decode().startswith("sanduhr: stale 40m"))

    def test_a_fresh_snapshot_wins_over_stdin(self):
        self.r.fresh()
        p, _ = self.r.run([], stdin=session_json())
        self.assertTrue(p.stdout.decode().startswith("5h 42% | wk 18%"))

    def test_passed_windows_are_dropped(self):
        now = datetime(2026, 10, 6, 12, 0, tzinfo=timezone.utc)
        past = int(now.timestamp()) - 60
        s = {"rate_limits": {"five_hour": {"used_percentage": 50, "resets_at": past},
                             "seven_day": {"used_percentage": 10}}}
        self.assertEqual(sl.render_stdin(s, now), "wk 10%*")


# Item 63b: picking the segments you keep.

PL = chr(0xE0B0)        # powerline arrow
PL_THIN = chr(0xE0B1)
BAR = chr(0x2502)
BULLET = chr(0x2022)
DOT = chr(0x00B7)
ESC = "\x1b"


def picks_b64(**picks):
    return base64.b64encode(json.dumps(picks).encode("utf-8")).decode("ascii")


def segs(line, sep=None):
    sp = sl.split_line(line, sep)
    return None if sp["doubt"] else [s["plain"] for s in sp["segments"]]


class SplitTests(unittest.TestCase):
    def test_each_separator_is_detected_and_cut(self):
        for sep, line in (
            ("pipe", "~/proj | main | +3 -1"),
            ("dot", "~/proj %s main %s +3 -1" % (DOT, DOT)),
            ("bar", "~/proj %s main %s +3 -1" % (BAR, BAR)),
            ("bullet", "~/proj %s main %s +3 -1" % (BULLET, BULLET)),
            ("powerline-thin", "~/proj %s main %s +3 -1" % (PL_THIN, PL_THIN)),
            ("powerline", " ~/proj %s main %s +3 -1 %s" % (PL, PL, PL)),
            ("spaces", "~/proj  main   +3 -1"),
        ):
            sp = sl.split_line(line)
            self.assertEqual(sp["sep"], sep, line)
            self.assertEqual([s["plain"] for s in sp["segments"]], ["~/proj", "main", "+3 -1"], line)
            self.assertEqual([s["matcher"] for s in sp["segments"]], ["~", "main", "+"], line)
        self.assertEqual(sl.split_line("just one thing")["sep"], "none")
        self.assertEqual(segs("just one thing"), ["just one thing"])

    def test_the_separator_used_most_wins_and_can_be_changed(self):
        line = "a | b %s c %s d" % (DOT, DOT)
        self.assertEqual(sl.split_line(line)["sep"], "dot")
        self.assertEqual(segs(line), ["a | b", "c", "d"])
        self.assertEqual(segs(line, "pipe"), ["a", "b %s c %s d" % (DOT, DOT)])
        self.assertEqual(segs(line, "none"), [line])
        # A tie goes to the earlier in SEPARATORS (the bar before the pipe).
        self.assertEqual(sl.detect("a %s b | c" % BAR), "bar")

    def test_ansi_colored_segments_keep_their_colors_with_resets_between(self):
        line = ESC + "[1;34m~/proj" + ESC + "[0m | " + ESC + "[32mmain" + ESC + "[0m | " + ESC + "[33m+3" + ESC + "[0m"
        sp = sl.split_line(line)
        self.assertEqual([s["plain"] for s in sp["segments"]], ["~/proj", "main", "+3"])
        out = sl.filter_line(line, {"drop": ["main"]}, 0)
        self.assertEqual(out, ESC + "[1;34m~/proj" + ESC + "[0m" + ESC + "[0m | " + ESC + "[33m+3" + ESC + "[0m")
        self.assertEqual(sl.visible_width(out), len("~/proj | +3"))

    def test_a_color_set_before_a_dropped_segment_still_reaches_the_next(self):
        # Their color runs on across segments: the kept one still gets it, then a reset.
        line = ESC + "[35mone | two | three"
        self.assertEqual(sl.filter_line(line, {"drop": ["two"]}, 0),
                         ESC + "[35mone" + sl.RESET + ESC + "[35m | " + sl.RESET + ESC + "[35mthree" + sl.RESET)

    def test_powerline_arrows_are_redrawn_in_the_new_neighbors_colors(self):
        line = (ESC + "[44;30m ~/proj " + ESC + "[34;42m" + PL + ESC + "[30m main " + ESC + "[32;45m" + PL
                + ESC + "[30m +3 " + ESC + "[0;35m" + PL + ESC + "[0m")
        sp = sl.split_line(line)
        self.assertEqual(sp["sep"], "powerline")
        self.assertEqual([s["bg"] for s in sp["segments"]], ["44", "42", "45"])
        out = sl.filter_line(line, {"drop": ["main"]}, 0)
        self.assertIn(ESC + "[0;34;45m" + PL, out)            # blue into magenta
        self.assertTrue(out.endswith(ESC + "[0;35m" + PL + ESC + "[0m"))   # their own cap
        out = sl.filter_line(line, {"drop": ["+"]}, 0)
        self.assertTrue(out.endswith(ESC + "[0;32;49m" + PL + sl.RESET))  # a new cap from green
        self.assertEqual(sl.visible_width(out), len(" ~/proj ") + 1 + len(" main ") + 1)
        self.assertEqual(sl._background([ESC + "[48;5;23m"]), "48;5;23")
        self.assertEqual(sl._background([ESC + "[48;2;1;2;3;1m"]), "48;2;1;2;3")
        self.assertIsNone(sl._background([ESC + "[44m", ESC + "[49m"]))

    def test_multi_line_output_is_split_per_line(self):
        text = "~/proj | main\nOpus  ctx 8%  $0.12\n"
        self.assertEqual(sl.filter_theirs(text, {"drop": ["main"]}), "~/proj\nOpus  ctx 8%  $0.12\n")
        self.assertEqual(sl.filter_theirs(text, {"drop": ["ctx"]}), "~/proj | main\nOpus  $0.12\n")
        # A line that loses every segment goes.
        self.assertEqual(sl.filter_theirs("solo\nx | y\n", {"drop": ["solo"]}), "x | y\n")
        # The separator can be chosen per line.
        self.assertEqual(sl.filter_theirs("a b | c\nd | e  f\n", {"drop": ["b", "d"], "sep": [None, "spaces"]}),
                         "a b | c\nf\n")
        self.assertEqual(sl.filter_theirs("a | b\nd | e\n", {"drop": ["b"], "sep": ["none"]}), "a | b\nd | e\n")

    def test_wide_characters(self):
        line = "日本語 | 🌿 main | 😀 ok"
        self.assertEqual(segs(line), ["日本語", "🌿 main", "😀 ok"])
        self.assertEqual([s["matcher"] for s in sl.split_line(line)["segments"]], ["日本語", "🌿", "😀"])
        self.assertEqual(sl.filter_line(line, {"drop": ["🌿"]}, 0), "日本語 | 😀 ok")
        # A glyph keeps what joins it: a variation selector, a ZWJ sequence.
        self.assertEqual(sl.matcher("❤️ love"), "❤️")
        self.assertEqual(sl.matcher("\U0001F468‍\U0001F4BB dev"), "\U0001F468‍\U0001F4BB")
        self.assertEqual(sl.matcher("42% ctx"), "#")
        self.assertEqual(sl.matcher("claude-opus-5 x"), "claude-opus-5")

    def test_any_doubt_keeps_the_line_whole(self):
        for line in (
            "a | b\rc | d",                         # a carriage return redraws
            "a | " + ESC + "]8;;https://x.test b",  # an unfinished escape
            "a |  | b",                             # an empty segment
            "a | b\x07",                            # a stray control character
        ):
            self.assertTrue(sl.split_line(line)["doubt"], repr(line))
            self.assertEqual(sl.filter_line(line, {"drop": ["a"]}, 0), line)
        # A cap at either end is not an empty segment.
        self.assertEqual(segs("| a | b |"), ["a", "b"])

    def test_nothing_dropped_leaves_the_bytes_alone(self):
        text = ESC + "[1ma" + ESC + "[0m  |  b   \n"
        self.assertEqual(sl.filter_theirs(text, {"drop": ["zzz"]}), text)
        self.assertEqual(sl.filter_theirs(text, None), text)


class MatcherStabilityTests(unittest.TestCase):
    picks = {"keep": ["~", "+"], "drop": ["⎇"]}

    def test_a_dropped_segment_missing_from_a_run_shifts_nothing(self):
        # Outside a repo the branch isn't printed: the others keep their places.
        self.assertEqual(sl.filter_line("~/proj | ⎇ main | +3", self.picks, 0), "~/proj | +3")
        self.assertEqual(sl.filter_line("~/tmp | +0", self.picks, 0), "~/tmp | +0")

    def test_reordered_segments_follow_their_matchers(self):
        self.assertEqual(sl.filter_line("+3 | ⎇ dev | ~/proj", self.picks, 0), "+3 | ~/proj")

    def test_new_segments_follow_keep_new(self):
        self.assertEqual(sl.filter_line("~/proj | $0.12 | ⎇ main", self.picks, 0), "~/proj | $0.12")
        strict = dict(self.picks, new=False)
        self.assertEqual(sl.filter_line("~/proj | $0.12 | ⎇ main", strict, 0), "~/proj")
        self.assertEqual(sl.filter_line("$0.12", strict, 0), None)

    def test_segments_sharing_a_matcher_go_together(self):
        self.assertEqual(sl.filter_line("a | ● x | ● y | b", {"drop": ["●"]}, 0), "a | b")


class PicksGrammarTests(unittest.TestCase):
    good = b64("my.sh")

    def test_round_trip(self):
        k = picks_b64(keep=["~"], drop=["⎇"], new=False, sep=[None, "pipe"])
        args = sl.parse_args(["--chain-b64", self.good, "--join", "same", "--padding", "2",
                              "--keep-theirs-b64", k, "--mine", "session,resets,model"])
        self.assertEqual(args, ("my.sh", "same", 2, {"keep": ["~"], "drop": ["⎇"], "new": False, "sep": [None, "pipe"]},
                                ("session", "resets", "model")))
        self.assertEqual(sl.parse_args(["--chain-b64", self.good, "--join", "line", "--mine", "weekly"])[4], ("weekly",))
        self.assertEqual(sl.parse_args(["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64()])[3], {})

    def test_anything_else_is_refused(self):
        k = picks_b64(drop=["a"])
        for argv in (
            ["--chain-b64", self.good, "--join", "line", "--mine", "session", "--keep-theirs-b64", k],   # order
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", k, "--padding", "2"],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64"],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", k, "--keep-theirs-b64", k],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", "!!!!"],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", b64("[]")],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", b64("{not json")],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(extra=1)],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(drop="a")],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(drop=[""])],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(drop=[1])],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(drop=["x" * 65])],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(new="yes")],
            ["--chain-b64", self.good, "--join", "line", "--keep-theirs-b64", picks_b64(sep=["slash"])],
            ["--chain-b64", self.good, "--join", "line", "--mine", ""],
            ["--chain-b64", self.good, "--join", "line", "--mine", "weekly,session"],     # out of order
            ["--chain-b64", self.good, "--join", "line", "--mine", "session,session"],
            ["--chain-b64", self.good, "--join", "line", "--mine", "session,cost"],
            ["--chain-b64", self.good, "--join", "line", "--mine", "session,"],
            ["--chain-b64", self.good, "--join", "line", "--padding", "²"],
            ["--mine", "session"],
        ):
            self.assertIsNone(sl.parse_args(argv), argv)


class MineTests(unittest.TestCase):
    def setUp(self):
        self.r = Rig()
        self.r.fresh()

    def tearDown(self):
        self.r.close()

    def test_no_picks_prints_exactly_what_it_did(self):
        plain, _ = self.r.run([], stdin=session_json())
        combined, _ = self.r.chain("echo mine")
        self.assertEqual(combined.stdout.decode(), "mine\n" + plain.stdout.decode())
        default, _ = self.r.run(["--chain-b64", b64("echo mine"), "--join", "line", "--mine", "session,weekly,resets"],
                                session_json())
        self.assertEqual(default.stdout, combined.stdout)

    def test_sanduhrs_segments_are_switches(self):
        p, _ = self.r.run(["--chain-b64", b64("echo mine"), "--join", "line", "--mine", "weekly,context,model"],
                          session_json())
        self.assertEqual(p.stdout.decode(), "mine\nwk 18% | ctx 8% | Opus\n")
        p, _ = self.r.run(["--chain-b64", b64("echo mine"), "--join", "line", "--mine", "session"], session_json())
        self.assertEqual(p.stdout.decode(), "mine\n5h 42%\n")

    def test_a_notice_always_shows(self):
        now = datetime.now(timezone.utc)
        base = sl.render_parts({"schema_version": 1, "captured_at": iso(now - timedelta(minutes=10)), "status": "ok",
                                "tiers": [{"key": "five_hour", "utilization": 42},
                                          {"key": "seven_day", "utilization": 18}]}, now)
        self.assertEqual(sl.format_parts(base, ("weekly",)), "wk 18% (10m ago)")
        self.assertEqual(sl.format_parts({"notice": "sanduhr: stale 22m - start widget"}, ("model",)),
                         "sanduhr: stale 22m - start widget")
        self.assertEqual(sl.format_parts({"parts": [("session", "5h 1%")], "error": "offline", "ago": None}, ("weekly",)),
                         "sanduhr: offline")


class FilteredRunTests(unittest.TestCase):
    def setUp(self):
        self.r = Rig()
        self.r.fresh()

    def tearDown(self):
        self.r.close()

    def run_picks(self, command, picks, mine=None, join="line", **env):
        args = ["--chain-b64", b64(command), "--join", join, "--keep-theirs-b64", picks_b64(**picks)]
        if mine:
            args += ["--mine", mine]
        return self.r.run(args, session_json(), **env)[0]

    def test_their_segments_are_filtered_at_run_time(self):
        p = self.run_picks("printf '~/proj | main | +3\\n'", {"drop": ["main"]})
        self.assertEqual(p.returncode, 0)
        self.assertTrue(p.stdout.decode().startswith("~/proj | +3\n5h 42%"), p.stdout)

    def test_nothing_of_theirs_left_omits_their_part_never_sanduhrs(self):
        p = self.run_picks("printf 'main | dev\\n'", {"drop": ["main", "dev"]}, join="same", COLUMNS="200")
        self.assertTrue(p.stdout.decode().startswith("5h 42% | wk 18%"), p.stdout)
        self.assertEqual(p.stdout.decode().count("\n"), 1)
        p = self.run_picks("exit 1", {"drop": ["main"]})
        self.assertTrue(p.stdout.decode().startswith("5h 42%"))

    def test_same_row_join_after_filtering(self):
        p = self.run_picks("printf '\\033[31ma\\033[0m | b'", {"drop": ["b"]}, mine="session", join="same", COLUMNS="200")
        self.assertEqual(p.stdout.decode(), "\x1b[31ma\x1b[0m" + sl.RESET + sl.SEPARATOR + "5h 42%\n")


class PreviewModeTests(unittest.TestCase):
    def setUp(self):
        self.r = Rig()
        self.r.fresh()

    def tearDown(self):
        self.r.close()

    def test_inspect_runs_theirs_once_and_lists_the_pieces(self):
        counter = self.r.path("runs")
        p, _ = self.r.run(["--inspect-b64", b64("echo x >> '%s'; printf '~/p | main\\nOpus  ctx\\n'" % counter)],
                          session_json())
        self.assertEqual(p.returncode, 0)
        got = json.loads(p.stdout.decode())
        with open(counter) as f:
            self.assertEqual(f.read(), "x\n")
        self.assertEqual(got["theirs"], "~/p | main\nOpus  ctx\n")
        self.assertEqual([line["auto"] for line in got["lines"]], ["pipe", "spaces"])
        self.assertEqual(got["lines"][0]["splits"]["pipe"]["segments"],
                         [{"matcher": "~", "text": "~/p"}, {"matcher": "main", "text": "main"}])
        self.assertEqual(set(got["lines"][0]["splits"]), set(sl.SEPARATOR_IDS))
        self.assertEqual({m["name"]: m["text"] for m in got["mine"]}["model"], "Opus")
        self.assertEqual([m["name"] for m in got["mine"]], list(sl.MINE))

    def test_compose_prints_what_the_runner_would(self):
        out = "~/proj | main | +3\n"
        picks = picks_b64(drop=["main"])
        composed, _ = self.r.run(["--compose-b64", b64(out), "--join", "same", "--keep-theirs-b64", picks,
                                  "--mine", "session,context"], session_json(), COLUMNS="200")
        ran, _ = self.r.run(["--chain-b64", b64("printf '%s'" % out.replace("\n", "\\n")), "--join", "same",
                             "--keep-theirs-b64", picks, "--mine", "session,context"], session_json(), COLUMNS="200")
        self.assertEqual(composed.stdout, ran.stdout)
        self.assertEqual(composed.stdout.decode(), "~/proj | +3" + sl.RESET + sl.SEPARATOR + "5h 42% | ctx 8%\n")
        empty, _ = self.r.run(["--compose-b64", "", "--join", "line"], session_json())
        self.assertTrue(empty.stdout.decode().startswith("5h 42%"))



class JoinWithTests(unittest.TestCase):
    """Join with (item 63b): the glyph between segments in the final line, apart from the split."""

    def test_join_with_changes_the_glyph_even_when_nothing_drops(self):
        line = "~/proj %s main %s +3" % (DOT, DOT)
        self.assertEqual(sl.filter_line(line, {"with": "pipe"}, 0), "~/proj | main | +3")
        self.assertEqual(sl.filter_line(line, {"with": "bar", "drop": ["main"]}, 0), "~/proj %s +3" % BAR)
        self.assertEqual(sl.filter_line(line, {"with": "spaces"}, 0), "~/proj  main  +3")
        self.assertEqual(sl.filter_line(line, {"with": "powerline-thin"}, 0), "~/proj %s main %s +3" % (PL_THIN, PL_THIN))
        # Without it, nothing dropped leaves the bytes alone.
        self.assertEqual(sl.filter_line(line, {}, 0), line)
        # The split still decides what a segment is.
        self.assertEqual(sl.filter_line(line, {"with": "pipe", "sep": ["none"]}, 0), line)
        self.assertEqual(sl.filter_theirs(line + "\n", {"with": "pipe", "sep": ["none"]}), line + "\n")

    def test_powerline_join_draws_arrows_in_the_neighbors_colors(self):
        line = ESC + "[44m a " + ESC + "[0m | " + ESC + "[42m b " + ESC + "[0m"
        out = sl.filter_line(line, {"with": "powerline"}, 0)
        self.assertIn(" " + ESC + "[0;34;42m" + PL + sl.RESET + " ", out)
        self.assertEqual(sl.visible_width(out), len(" a") + 3 + len("b "))   # their outer padding stays

    def test_join_with_reaches_sanduhrs_part_and_the_seam(self):
        r = Rig()
        try:
            r.fresh()
            p, _ = r.run(["--compose-b64", b64("a %s b\n" % DOT), "--join", "same",
                          "--keep-theirs-b64", picks_b64(**{"with": "bar"})], session_json(), COLUMNS="200")
            self.assertEqual(p.stdout.decode(), "a %s b" % BAR + sl.RESET + " %s 5h 42%% %s wk 18%% %s " % (BAR, BAR, BAR)
                             + p.stdout.decode().rsplit(" %s " % BAR, 1)[1])
            self.assertNotIn(" | ", p.stdout.decode())
        finally:
            r.close()

    def test_the_grammar_takes_with_and_refuses_anything_else(self):
        good = b64("my.sh")
        args = sl.parse_args(["--chain-b64", good, "--join", "line", "--keep-theirs-b64", picks_b64(**{"with": "powerline"})])
        self.assertEqual(args[3], {"with": "powerline"})
        for value in ("same", "slash", "", 1, None):
            self.assertIsNone(sl.parse_args(["--chain-b64", good, "--join", "line",
                                             "--keep-theirs-b64", picks_b64(**{"with": value})]), value)


if __name__ == "__main__":
    unittest.main()

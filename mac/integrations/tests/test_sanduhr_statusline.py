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
                         ("~/bin/line.sh", "line", 0))
        self.assertEqual(sl.parse_args(["--chain-b64", b64("a | b"), "--join", "same", "--padding", "2"]),
                         ("a | b", "same", 2))

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


if __name__ == "__main__":
    unittest.main()

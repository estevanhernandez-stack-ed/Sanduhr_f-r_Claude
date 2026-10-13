"""Tests for per-seat meters (item 74) in the statusline and the MCP server: the session's Claude
Code folder (CLAUDE_CONFIG_DIR, unset meaning ~/.claude) looked up in seats.json, that account's
snapshot.<ref>.json read, snapshot.json otherwise. Temp folders, made-up labels and an explicit
environment only; never a real ~/.claude* folder or the real Application Support folder.
Standard library only:

    python3 -m unittest discover -s mac/integrations/tests
"""
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
STATUSLINE = os.path.join(os.path.dirname(HERE), "sanduhr_statusline.py")
SERVER = os.path.join(os.path.dirname(HERE), "sanduhr_mcp.py")


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


sl = load("sanduhr_statusline_seats", STATUSLINE)
mcp = load("sanduhr_mcp_seats", SERVER)


def ref(label):
    return hashlib.sha256(label.encode("utf-8")).hexdigest()[:8]


def iso(t):
    return t.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f+00:00")


class Seats(unittest.TestCase):
    """A temp Application Support folder with two seats beside two Claude Code folders."""

    def setUp(self):
        self.dir = os.path.realpath(tempfile.mkdtemp(prefix="sanduhr-seats-test-"))
        self.support = os.path.join(self.dir, "Sanduhr")
        os.makedirs(self.support)
        self.work = self.folder(".claude")
        self.home = self.folder(".claude-home")
        self.other = self.folder(".claude-other")

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def folder(self, name):
        p = os.path.join(self.dir, name)
        os.makedirs(p, exist_ok=True)
        return p

    def write(self, name, doc):
        with open(os.path.join(self.support, name), "w", encoding="utf-8") as f:
            f.write(doc if isinstance(doc, str) else json.dumps(doc))

    def seats(self, rows=None, schema=1):
        rows = rows if rows is not None else [
            {"folder": self.work, "account_ref": ref("Work")},
            {"folder": self.home, "account_ref": ref("Home")},
        ]
        self.write("seats.json", {"schema_version": schema, "seats": rows})

    def snapshot(self, label, five, name=None, captured=None):
        captured = captured or datetime.now(timezone.utc) - timedelta(minutes=1)
        self.write(name or "snapshot.%s.json" % ref(label), {
            "schema_version": 1, "writer_version": "2.16.0", "captured_at": iso(captured),
            "account_ref": ref(label), "plan": None, "status": "ok", "error_kind": None,
            "tiers": [
                {"key": "five_hour", "utilization": five, "resets_at": iso(captured + timedelta(hours=3)), "used": None, "limit": None},
                {"key": "seven_day", "utilization": 10, "resets_at": iso(captured + timedelta(days=3)), "used": None, "limit": None},
            ]})

    def env(self, config_dir=None, **extra):
        # HOME points into the temp folder, so "unset" never means the real ~/.claude.
        env = {"HOME": self.dir, "PATH": os.environ.get("PATH", ""), "TZ": "UTC", "SANDUHR_SUPPORT_DIR": self.support}
        if config_dir is not None:
            env["CLAUDE_CONFIG_DIR"] = config_dir
        env.update(extra)
        return env

    def resolve(self, config_dir=None, **extra):
        return sl.seat_snapshot(self.support, self.env(config_dir, **extra))

    def path(self, label):
        return os.path.join(self.support, "snapshot.%s.json" % ref(label))


class Resolution(Seats):
    def test_the_two_scripts_share_one_block(self):
        def block(path):
            with open(path, encoding="utf-8") as f:
                text = f.read()
            return text[text.index("# -- seats (item 74)"):text.index("# -- end seats --")]
        self.assertEqual(block(STATUSLINE), block(SERVER))

    def test_a_linked_folder_reads_its_account(self):
        self.seats()
        self.assertEqual(self.resolve(self.work), (self.path("Work"), ref("Work")))
        self.assertEqual(self.resolve(self.home), (self.path("Home"), ref("Home")))

    def test_unset_means_dot_claude_under_home(self):
        self.seats()
        self.assertEqual(self.resolve(None), (self.path("Work"), ref("Work")))
        self.assertEqual(self.resolve(""), (self.path("Work"), ref("Work")))

    def test_tilde_and_a_trailing_slash(self):
        self.seats()
        self.assertEqual(self.resolve("~/.claude-home/")[1], ref("Home"))
        self.assertEqual(self.resolve(self.home + "//")[1], ref("Home"))

    def test_a_symlinked_folder_resolves_to_its_target(self):
        self.seats()
        link = os.path.join(self.dir, "home-link")
        os.symlink(self.home, link)
        self.assertEqual(self.resolve(link)[1], ref("Home"))
        # A seats.json row written through a link matches the real folder too.
        self.seats([{"folder": link + "/", "account_ref": ref("Home")}])
        self.assertEqual(self.resolve(self.home)[1], ref("Home"))

    def test_an_unlinked_folder_reads_snapshot_json(self):
        self.seats()
        self.assertEqual(self.resolve(self.other), (os.path.join(self.support, "snapshot.json"), None))

    def test_no_or_a_bad_seats_file_reads_snapshot_json(self):
        default = (os.path.join(self.support, "snapshot.json"), None)
        self.assertEqual(self.resolve(self.work), default)                       # missing
        for text in ("{", "[]", "null", '{"schema_version": "x", "seats": []}', '{"schema_version": 1, "seats": {}}',
                     '{"seats": []}'):
            self.write("seats.json", text)
            self.assertEqual(self.resolve(self.work), default, text)
        self.seats(schema=2)                                                     # newer: not read
        self.assertEqual(self.resolve(self.work), default)

    def test_the_same_folder_spelled_another_way_matches_by_identity(self):
        self.seats()
        # macOS volumes ignore case and normalization: the same folder, so the same seat.
        self.assertEqual(self.resolve(self.home.replace(".claude-home", ".CLAUDE-Home"))[1], ref("Home"))
        nfc = self.folder("café")
        self.seats([{"folder": nfc, "account_ref": ref("Home")}])
        self.assertEqual(self.resolve(os.path.join(self.dir, "café"))[1], ref("Home"))
        # A folder that doesn't exist compares by spelling, folded the same way.
        gone = os.path.join(self.dir, "Gone")
        self.seats([{"folder": gone, "account_ref": ref("Home")}])
        self.assertEqual(self.resolve(os.path.join(self.dir, "gone"))[1], ref("Home"))

    def test_a_tilde_row_is_expanded_like_the_folder(self):
        self.seats([{"folder": "~/.claude-home/", "account_ref": ref("Home")}])
        self.assertEqual(self.resolve(self.home)[1], ref("Home"))

    def test_a_relative_config_dir_reads_as_unlinked(self):
        self.seats([{"folder": self.home, "account_ref": ref("Home")}])
        self.assertEqual(self.resolve(".claude-home"), (os.path.join(self.support, "snapshot.json"), None))

    def test_schema_version_must_be_the_number_one_and_the_file_small(self):
        rows = [{"folder": self.work, "account_ref": ref("Work")}]
        for version, found in (("1", False), (True, False), (2, False), (1.0, True), (1, True)):
            self.write("seats.json", {"schema_version": version, "seats": rows})
            self.assertEqual(self.resolve(self.work)[1] is not None, found, version)
        self.write("seats.json", json.dumps({"schema_version": 1, "seats": rows}) + " " * (64 * 1024))
        self.assertIsNone(self.resolve(self.work)[1])

    def test_a_ref_that_is_not_eight_hex_digits_is_skipped(self):
        self.seats([{"folder": self.work, "account_ref": "../../x"},
                    {"folder": self.work, "account_ref": "ABCDEF12"},
                    {"folder": self.work},
                    "junk"])
        self.assertEqual(self.resolve(self.work)[1], None)


class Statusline(Seats):
    def run_line(self, config_dir=None, stdin=b"", **extra):
        p = subprocess.run([sys.executable, STATUSLINE], input=stdin, capture_output=True,
                           env=self.env(config_dir, **extra), timeout=20)
        self.assertEqual(p.returncode, 0, p.stderr)
        return p.stdout.decode("utf-8")

    def stdin(self, five):
        now = int(time.time())
        return json.dumps({"rate_limits": {"five_hour": {"used_percentage": five, "resets_at": now + 3600},
                                           "seven_day": {"used_percentage": 3, "resets_at": now + 86400}}}).encode()

    def test_two_seats_show_their_own_accounts_at_the_same_time(self):
        self.seats()
        self.snapshot("Work", 61, name="snapshot.json")      # Work is active in Sanduhr
        self.snapshot("Work", 61)
        self.snapshot("Home", 12)
        self.assertIn("5h 61%", self.run_line(self.work))
        self.assertIn("5h 12%", self.run_line(self.home))

    def test_an_unlinked_seat_shows_the_active_account_as_before(self):
        self.seats()
        self.snapshot("Work", 61, name="snapshot.json")
        self.snapshot("Home", 12)
        self.assertIn("5h 61%", self.run_line(self.other))

    def test_without_seats_json_everything_reads_snapshot_json(self):
        self.snapshot("Work", 61, name="snapshot.json")
        for folder in (self.work, self.home, self.other, None):
            self.assertIn("5h 61%", self.run_line(folder))

    def test_a_linked_seat_never_shows_another_accounts_numbers(self):
        # Home linked but never fetched: Claude Code's own numbers (marked *), not Work's.
        self.seats()
        self.snapshot("Work", 61, name="snapshot.json")
        line = self.run_line(self.home, stdin=self.stdin(33))
        self.assertIn("5h 33%*", line)
        self.assertNotIn("61%", line)
        # With no stdin either: nothing, the uninstalled look.
        self.assertEqual(self.run_line(self.home).strip(), "")

    def test_a_linked_folder_leads_with_its_accounts_label(self):
        self.seats([{"folder": self.work, "account_ref": ref("Work"), "label": "Work"},
                    {"folder": self.home, "account_ref": ref("Home"), "label": "Ho\x1bme\n"}])
        self.snapshot("Work", 61, name="snapshot.json")
        self.snapshot("Work", 61)
        self.snapshot("Home", 12)
        self.assertTrue(self.run_line(self.work).startswith("Work | 5h 61%"))
        # Control characters never reach the terminal.
        self.assertTrue(self.run_line(self.home).startswith("Home | 5h 12%"))
        # An unlinked folder has no label; nor does a linked one with nothing else to show.
        self.assertTrue(self.run_line(self.other).startswith("5h 61%"))
        os.remove(self.path("Home"))
        self.assertEqual(self.run_line(self.home).strip(), "")

    def test_a_row_without_a_label_and_a_long_label(self):
        self.seats([{"folder": self.work, "account_ref": ref("Work")},
                    {"folder": self.home, "account_ref": ref("Home"), "label": "H" * 60}])
        self.snapshot("Work", 61)
        self.snapshot("Home", 12)
        self.assertTrue(self.run_line(self.work).startswith("5h 61%"))
        self.assertTrue(self.run_line(self.home).startswith("H" * sl.LABEL_MAX + " | 5h 12%"))

    def test_a_stale_seat_file_says_its_age(self):
        self.seats()
        self.snapshot("Home", 12, captured=datetime.now(timezone.utc) - timedelta(minutes=10))
        self.assertIn("(10m ago)", self.run_line(self.home))

    def test_an_old_seat_reading_says_its_age_not_start_widget(self):
        self.seats()
        self.snapshot("Home", 12, captured=datetime.now(timezone.utc) - timedelta(minutes=23))
        line = self.run_line(self.home)
        self.assertIn("5h 12%", line)
        self.assertIn("(as of 23m)", line)
        self.assertNotIn("start widget", line)
        # snapshot.json past the dead band still says so.
        self.snapshot("Work", 61, name="snapshot.json", captured=datetime.now(timezone.utc) - timedelta(minutes=23))
        self.assertIn("start widget", self.run_line(self.other))

    def test_sanduhr_snapshot_still_names_one_file(self):
        self.seats()
        self.snapshot("Home", 12)
        self.snapshot("Work", 61, name="snapshot.json")
        self.assertIn("5h 61%", self.run_line(self.home, SANDUHR_SNAPSHOT=os.path.join(self.support, "snapshot.json")))


class GetUsage(Seats):
    def access(self, *labels):
        self.write("mcp-access.json", {"schema_version": 1, "accounts": [
            {"account_ref": ref(l), "active": l == "Work", "share": "meters", "history_file": "history.%s.json" % l}
            for l in labels]})

    def usage(self, config_dir):
        return mcp.build_usage(paths=mcp.Paths(support_dir=self.support), env=self.env(config_dir))

    def test_each_seat_answers_for_its_account(self):
        self.seats()
        self.access("Work", "Home")
        self.snapshot("Work", 61, name="snapshot.json")
        self.snapshot("Work", 61)
        self.snapshot("Home", 12)
        r = self.usage(self.home)
        self.assertEqual((r["status"], r["scope"], r["account"]["ref"]), ("ok", "seat_account", ref("Home")))
        self.assertEqual(r["tiers"][0]["utilization_pct"], 12)
        w = self.usage(self.work)
        self.assertEqual((w["scope"], w["account"]["ref"]), ("seat_account", ref("Work")))

    def test_an_unlinked_seat_answers_for_the_active_account(self):
        self.seats()
        self.access("Work", "Home")
        self.snapshot("Work", 61, name="snapshot.json")
        r = self.usage(self.other)
        self.assertEqual((r["status"], r["scope"], r["account"]["ref"]), ("ok", "active_account_only", ref("Work")))

    def test_a_seat_without_a_reading_says_so_and_borrows_nothing(self):
        self.seats()
        self.access("Work", "Home")
        self.snapshot("Work", 61, name="snapshot.json")
        r = self.usage(self.home)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "missing"))
        self.assertIn("linked", r["remedy"])

    def test_share_with_your_agents_still_decides(self):
        # Home's seat, Home not shared: not_shared, even though Work (active) is.
        self.seats()
        self.access("Work")
        self.snapshot("Work", 61, name="snapshot.json")
        self.snapshot("Home", 12)
        r = self.usage(self.home)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "not_shared"))
        self.assertNotIn("12", json.dumps(r.get("tiers")))

    def test_an_old_seat_reading_is_not_a_stopped_widget(self):
        self.seats()
        self.access("Work", "Home")
        self.snapshot("Home", 12, captured=datetime.now(timezone.utc) - timedelta(hours=3))
        r = self.usage(self.home)
        self.assertEqual((r["status"], r["reason"]), ("stale", "seat_not_in_use"))
        self.assertIn("3h", r["remedy"])

    def test_a_seat_file_naming_another_account_is_refused(self):
        self.seats()
        self.access("Work", "Home")
        self.snapshot("Work", 61, name="snapshot.%s.json" % ref("Home"))
        r = self.usage(self.home)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "not_shared"))


if __name__ == "__main__":
    unittest.main()

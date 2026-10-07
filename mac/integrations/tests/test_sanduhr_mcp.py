"""Tests for mac/integrations/sanduhr_mcp.py (item 47): the access file, each sharing level, and
parity with the Windows sanduhr-mcp tests (windows-dotnet/tests/Sanduhr.Mcp.Tests) on the same
inputs. Temp folders and made-up accounts only; never a real ~/.claude* folder or the real
Application Support folder. Standard library only:

    python3 -m unittest discover -s mac/integrations/tests
"""
import hashlib
import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile
import time
import unittest
import uuid
from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = os.path.join(os.path.dirname(HERE), "sanduhr_mcp.py")
FIXTURES = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Tests", "SanduhrTests", "Fixtures")

spec = importlib.util.spec_from_file_location("sanduhr_mcp", SERVER)
mcp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mcp)

# The Windows tests' clock: 2026-07-26 12:00 UTC, on a UTC local clock.
NOW = datetime(2026, 7, 26, 12, 0, 0, tzinfo=timezone.utc)


def setUpModule():
    os.environ["TZ"] = "UTC"
    time.tzset()


def ref(label):
    """AccountRef: the first 4 bytes of the SHA-256 of the label, lowercase hex."""
    return hashlib.sha256(label.encode("utf-8")).hexdigest()[:8]


def iso(t):
    return t.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f+00:00")


def event_line(ts, model, inp, out, cwd):
    msg = {"usage": {"input_tokens": inp, "output_tokens": out}}
    if model is not None:
        msg["model"] = model
    d = {"type": "assistant", "timestamp": iso(ts), "message": msg}
    if cwd is not None:
        d["cwd"] = cwd
    return json.dumps(d)


def no_git(_):
    return False


class Fixture:
    """A temp Application Support folder plus Claude Code folders beside it."""

    def __init__(self):
        self.dir = tempfile.mkdtemp(prefix="sanduhr-mcp-test-")
        self.support = os.path.join(self.dir, "Sanduhr")
        os.makedirs(self.support)
        self.paths = mcp.Paths(support_dir=self.support)

    def close(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def folder(self, name):
        p = os.path.join(self.dir, name)
        os.makedirs(p, exist_ok=True)
        return p

    def access(self, *accounts, schema=1):
        self.write_json("mcp-access.json", {"schema_version": schema, "accounts": list(accounts)})

    def write_json(self, name, doc):
        with open(os.path.join(self.support, name), "w", encoding="utf-8") as f:
            json.dump(doc, f)

    def snapshot(self, label, tiers=None, captured=None, status="ok"):
        captured = captured or NOW - timedelta(minutes=2)
        if tiers is None:
            tiers = [
                {"key": "five_hour", "utilization": 42, "resets_at": iso(captured + timedelta(hours=3)), "used": None, "limit": None},
                {"key": "seven_day", "utilization": 62, "resets_at": iso(captured + timedelta(days=5)), "used": None, "limit": None},
            ]
        self.write_json("snapshot.json", {
            "schema_version": 1, "writer_version": "2.6.0", "captured_at": iso(captured),
            "account_ref": ref(label) if label else None, "plan": None, "status": status,
            "error_kind": None, "tiers": tiers})

    def session_log(self, folder, project_dir, *lines):
        d = os.path.join(folder, "projects", project_dir)
        os.makedirs(d, exist_ok=True)
        path = os.path.join(d, uuid.uuid4().hex + ".jsonl")
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        return path

    def rollup(self, vault_id, month, days):
        d = os.path.join(self.support, "vault", vault_id)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "rollups-%s.json" % month), "w", encoding="utf-8") as f:
            json.dump({"schema_version": 1, "days": days}, f)


def entry(label, share, active=True, names=None, vault_id=None, live_folder=None):
    e = {"account_ref": ref(label), "active": active, "share": share, "history_file": "history.%s.json" % label}
    if names:
        e["names"] = names
    if vault_id:
        e["vault_id"] = vault_id
    if live_folder:
        e["live_folder"] = live_folder
    return e


VAULT_A = "0123456789abcdef"
VAULT_B = "fedcba9876543210"


class Base(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture()
        self.paths = self.fx.paths

    def tearDown(self):
        self.fx.close()

    def usage(self):
        return mcp.build_usage(now=NOW, paths=self.paths)

    def burn(self, days=7, full=False):
        return mcp.build_burn(days, full, now=NOW, paths=self.paths, dir_holds_git=no_git)

    def models(self, days=7):
        return mcp.build_model_usage(days, now=NOW, paths=self.paths)

    def history(self, days=30):
        return mcp.build_history(days, now=NOW, paths=self.paths)

    def ping(self):
        return mcp.build_ping(now=NOW, paths=self.paths)

    def all_tools(self):
        return [self.usage(), self.burn(), self.models(), self.history()]


class NoAccessFile(Base):
    """No file, an unreadable one or an unknown schema: nothing is shared, whatever else is on disk."""

    def setUp(self):
        super().setUp()
        work = self.fx.folder(".claude-test")
        self.fx.session_log(work, "p", event_line(NOW - timedelta(hours=1), "claude-fable-5", 10, 10, "/tmp/x/proj"))
        self.fx.snapshot("Home")
        self.fx.write_json("history.Home.json", {"five_hour": [{"t": iso(NOW), "v": 10}]})
        self.fx.rollup(VAULT_A, "2026-07", {"2026-07-25": {"total": 5}})

    def assert_nothing(self, reason="not_shared"):
        for r in self.all_tools():
            self.assertEqual(r["status"], "no_data")
            self.assertEqual(r["reason"], reason)
            self.assertNotIn("tiers", r)

    def test_missing_file_shares_nothing(self):
        self.assert_nothing()
        p = self.ping()
        self.assertEqual(p["sharing"]["access_file"], "missing")
        self.assertEqual(p["usage_reason"], "not_shared")
        self.assertEqual(p["sharing"]["accounts_shared"], 0)

    def test_unreadable_file_shares_nothing(self):
        with open(os.path.join(self.fx.support, "mcp-access.json"), "w") as f:
            f.write("{not json")
        self.assert_nothing()
        self.assertEqual(self.ping()["sharing"]["access_file"], "unreadable")

    def test_unknown_schema_shares_nothing(self):
        self.fx.access(entry("Home", "activity", vault_id=VAULT_A), schema=2)
        self.assert_nothing()
        self.assertEqual(self.ping()["sharing"]["access_file"], "schema_unsupported")
        self.assertIn("Update", self.usage()["remedy"])

    def test_empty_account_list_shares_nothing(self):
        self.fx.access()
        self.assert_nothing()
        self.assertEqual(self.ping()["sharing"]["access_file"], "ok")

    def test_invalid_entries_are_dropped(self):
        bad = [
            {"account_ref": "Home", "share": "meters"},                       # a label, not a ref
            {"account_ref": ref("Home"), "share": "off"},
            {"account_ref": ref("Home"), "share": "everything"},
        ]
        self.fx.access(*bad)
        self.assert_nothing()


class SharingOff(Base):
    def test_the_active_account_off_is_not_served_while_another_shares(self):
        self.fx.access(entry("Work", "meters", active=False))
        self.fx.snapshot("Home")
        r = self.usage()
        self.assertEqual((r["status"], r["reason"]), ("no_data", "not_shared"))
        self.assertNotIn(ref("Home"), json.dumps(r))
        self.assertEqual(self.ping()["usage_reason"], "not_shared")

    def test_a_snapshot_without_an_account_ref_is_not_served(self):
        self.fx.access(entry("Home", "meters"))
        self.fx.snapshot(None)
        self.assertEqual(self.usage()["reason"], "not_shared")


class MetersLevel(Base):
    def setUp(self):
        super().setUp()
        self.work = self.fx.folder(".claude-test")
        self.fx.session_log(self.work, "p", event_line(NOW - timedelta(hours=1), "claude-fable-5", 10, 10, "/tmp/x/proj"))
        self.fx.rollup(VAULT_A, "2026-07", {"2026-07-25": {"total": 5}})
        # Even a hand-edited file can't widen meters: vault and folder are read for activity only.
        e = entry("Home", "meters", vault_id=VAULT_A, live_folder=self.work)
        self.fx.access(e)
        self.fx.snapshot("Home")

    def test_get_usage_serves_the_meters_without_local_burn(self):
        r = self.usage()
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["account"], {"ref": ref("Home"), "plan": None})
        self.assertEqual([t["key"] for t in r["tiers"]], ["five_hour", "seven_day"])
        self.assertIsNone(r["local_burn_since_snapshot"])

    def test_activity_tools_are_disabled(self):
        for r in (self.burn(), self.models()):
            self.assertEqual((r["status"], r["reason"]), ("no_data", "disabled"))
            self.assertIn("Meters and activity", r["remedy"])

    def test_history_has_meter_peaks_and_no_record(self):
        self.fx.write_json("history.Home.json", {
            "five_hour": [
                {"t": iso(NOW - timedelta(days=40)), "v": 99},      # outside the window
                {"t": "2026-07-25T08:00:00Z", "v": 20},
                {"t": "2026-07-25T09:00:00Z", "v": 35.4},
                {"t": "2026-07-26T11:00:00Z", "v": 5},
                {"t": "not a time", "v": 80},
            ],
            "seven_day": [{"t": "2026-07-26T11:00:00Z", "v": 61}],
        })
        r = self.history(7)
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["roots_scanned"], [])
        self.assertEqual(r["days"], [])
        self.assertEqual(r["total_tokens"], 0)
        m = r["meter_history"]
        self.assertEqual(len(m), 1)
        self.assertEqual(m[0]["account_ref"], ref("Home"))
        five = m[0]["tiers"][0]
        self.assertEqual(five["key"], "five_hour")
        self.assertEqual(five["days"], [{"date": "2026-07-25", "peak_pct": 35}, {"date": "2026-07-26", "peak_pct": 5}])
        self.assertEqual(m[0]["tiers"][1]["label"], "Weekly - All Models")
        self.assertNotIn("Home", json.dumps(r).replace(ref("Home"), ""))

    def test_history_without_any_data_is_missing(self):
        r = self.history(30)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "missing"))

    def test_ping_counts(self):
        p = self.ping()
        self.assertEqual(p["usage_status"], "ok")
        self.assertEqual(p["sharing"]["meters_only"], 1)
        self.assertEqual(p["sharing"]["activity_read"], 0)
        self.assertEqual(p["sharing"]["active_account"], "meters")
        self.assertEqual(p["cc_roots_consented"], [])


class ActivityLevel(Base):
    """Windows ToolLogicBurnTests and ToolLogicAbilitiesTests, with accounts in place of roots."""

    def setUp(self):
        super().setUp()
        self.personal = self.fx.folder(".claude-personal")
        self.work = self.fx.folder(".claude")

    def share(self, *entries):
        self.fx.access(*entries)

    # -- get_local_burn_by_project (Windows ToolLogicBurnTests) --

    def test_invalid_window_is_a_typed_result(self):
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.burn(3)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "invalid_params"))
        self.assertEqual(self.models(3)["reason"], "invalid_params")
        self.assertEqual(self.history(14)["reason"], "invalid_params")

    def test_results_are_keyed_per_account_with_basenames_by_default(self):
        self.fx.session_log(self.personal, "c--sanduhr",
                            event_line(NOW - timedelta(hours=1), "claude-fable-5", 100, 200, "C:\\Users\\estev\\Projects\\Sanduhr"),
                            event_line(NOW - timedelta(hours=2), "claude-sonnet-5", 10, 20, "C:\\Users\\estev\\Projects\\Sanduhr"))
        self.fx.session_log(self.work, "c--wbp",
                            event_line(NOW - timedelta(hours=1), "claude-opus-4", 500, 500, os.path.join(self.fx.dir, "Client", "wbp")))
        self.share(entry("Work", "activity", active=False, names="names", live_folder=self.work),
                   entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.burn(7)
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["roots_scanned"], [ref("Work"), ref("Home")])
        work, personal = r["roots"]
        self.assertEqual((work["root"], work["total_tokens"]), (ref("Work"), 1000))
        self.assertEqual(work["projects"][0]["name"], "wbp")
        self.assertEqual(personal["total_tokens"], 330)
        self.assertEqual(personal["projects"][0]["name"], "Sanduhr")
        self.assertNotIn("estev", json.dumps(r))
        self.assertGreaterEqual(r["files_scanned"], 2)
        self.assertTrue(r["since"].startswith("2026-07-19T12:00:00.0000000"))

    def test_full_paths_needs_the_accounts_full_paths_choice(self):
        cwd = "C:\\Users\\estev\\Projects\\Sanduhr"
        self.fx.session_log(self.personal, "c--sanduhr", event_line(NOW - timedelta(hours=1), "claude-fable-5", 1, 1, cwd))
        self.share(entry("Home", "activity", names="full", live_folder=self.personal))
        r = self.burn(7, full=True)
        self.assertEqual(r["roots"][0]["projects"][0]["name"], cwd)
        self.assertEqual(r["roots"][0]["names"], "full")
        self.assertEqual(self.burn(7)["roots"][0]["projects"][0]["name"], "Sanduhr")
        # Names: a full_paths request still gets basenames.
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.burn(7, full=True)
        self.assertEqual(r["roots"][0]["projects"][0]["name"], "Sanduhr")
        self.assertEqual(r["roots"][0]["names"], "names")
        self.assertTrue(r["full_paths"])

    def test_hidden_names_return_the_records_code_only(self):
        self.fx.session_log(self.personal, "c--x",
                            event_line(NOW - timedelta(hours=1), "claude-fable-5", 30, 12, "/Users/someone/Projects/secret-client/.claude/worktrees/feat"),
                            event_line(NOW - timedelta(hours=1), "claude-fable-5", 8, 0, "/Users/someone/Projects/secret-client"),
                            event_line(NOW - timedelta(hours=1), "claude-fable-5", 5, 0, None))
        for names in ("hidden", "bogus"):          # an unknown choice reads as Hidden
            e = entry("Home", "activity", live_folder=self.personal)
            e["names"] = names
            self.share(e)
            r = self.burn(7, full=True)
            projects = r["roots"][0]["projects"]
            code = "p-" + hashlib.sha256(b"secret-client").hexdigest()[:10]
            self.assertEqual(projects, [{"name": code, "tokens": 50}, {"name": "(unknown)", "tokens": 5}])
            self.assertEqual(r["roots"][0]["names"], "hidden")
            self.assertNotIn("secret", json.dumps(r))
            self.assertNotIn("someone", json.dumps(r))

    def test_hidden_code_matches_the_vaults(self):
        # VaultHiddenName.of in Swift: "p-" + the first 10 hex digits of SHA-256(name).
        self.assertEqual(mcp.hidden_name("api"), "p-" + hashlib.sha256(b"api").hexdigest()[:10])
        self.assertEqual(len(mcp.hidden_name("api")), 12)

    def test_window_filter_excludes_old_events_and_counts_files(self):
        self.fx.session_log(self.personal, "c--old", event_line(NOW - timedelta(days=3), "claude-fable-5", 999, 0, "C:\\p\\old"))
        self.fx.session_log(self.personal, "c--new", event_line(NOW - timedelta(hours=1), "claude-fable-5", 100, 0, "C:\\p\\new"))
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.burn(1)
        self.assertEqual(r["roots"][0]["total_tokens"], 100)
        self.assertEqual(r["window_days"], 1)
        self.assertGreaterEqual(r["files_scanned"], 1)

    def test_events_without_cwd_stay_visible_as_unknown(self):
        self.fx.session_log(self.personal, "c--x", event_line(NOW - timedelta(hours=1), "claude-fable-5", 40, 2, None))
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        self.assertEqual(self.burn(7)["roots"][0]["projects"][0], {"name": "(unknown)", "tokens": 42})

    def test_every_response_names_the_accounts_it_covered(self):
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        self.assertEqual(self.burn(7)["roots_scanned"], [ref("Home")])

    def test_not_tracked_or_unlinked_reads_nothing(self):
        # share activity, but no live_folder (Not tracked, or no folder linked)
        self.fx.session_log(self.personal, "c--x", event_line(NOW - timedelta(hours=1), "claude-fable-5", 40, 2, None))
        self.share(entry("Home", "activity", names="names"))
        self.assertEqual(self.burn()["reason"], "disabled")
        self.assertEqual(self.models()["reason"], "disabled")

    def test_relative_or_dotted_folders_are_refused(self):
        self.fx.session_log(self.personal, "c--x", event_line(NOW - timedelta(hours=1), "claude-fable-5", 40, 2, None))
        for bad in (".claude-personal", self.personal + "/../.claude-personal", ""):
            self.share(entry("Home", "activity", names="names", live_folder=bad or None))
            self.assertEqual(self.burn()["reason"], "disabled", bad)

    def test_the_parity_fixture_by_project(self):
        # Fixtures/cc-logs/by-project.jsonl (the Windows CcLogReader fixture, item 45).
        d = os.path.join(self.personal, "projects", "p")
        os.makedirs(d)
        shutil.copy(os.path.join(FIXTURES, "cc-logs", "by-project.jsonl"), d)
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = mcp.build_burn(1, False, now=datetime(2026, 5, 5, 12, 0, tzinfo=timezone.utc),
                           paths=self.paths, dir_holds_git=no_git)
        projects = {p["name"]: p["tokens"] for p in r["roots"][0]["projects"]}
        self.assertEqual(projects, {"Sanduhr": 300, "Other": 100, "(unknown)": 1998})

    # -- get_model_usage (Windows ToolLogicAbilitiesTests) --

    def test_model_usage_ranks_models_and_joins_the_weekly_meter(self):
        self.fx.session_log(self.personal, "c--x",
                            event_line(NOW - timedelta(hours=2), "claude-fable-5", 6000, 2000, "C:\\p\\x"),
                            event_line(NOW - timedelta(hours=1), "claude-sonnet-5", 1000, 500, "C:\\p\\x"),
                            event_line(NOW - timedelta(hours=1), "mystery-model-9", 400, 100, "C:\\p\\x"))
        self.fx.snapshot("Home", tiers=[{"key": "seven_day_fable", "utilization": 8,
                                         "resets_at": iso(NOW + timedelta(days=5)), "used": None, "limit": None}])
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.models(7)
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["total_tokens"], 10000)
        top = r["models"][0]
        self.assertEqual((top["model"], top["tokens"], top["share_pct"]), ("claude-fable-5", 8000, 80))
        self.assertEqual((top["tier_key"], top["tier_label"]), ("seven_day_fable", "Weekly - Fable"))
        self.assertEqual(top["meter_utilization_pct"], 8)
        mystery = next(m for m in r["models"] if m["model"] == "mystery-model-9")
        self.assertIsNone(mystery["tier_key"])
        self.assertIsNone(mystery["meter_utilization_pct"])
        self.assertEqual(r["meter_source"]["account_ref"], ref("Home"))

    def test_model_usage_without_a_snapshot_still_serves_tokens(self):
        self.fx.session_log(self.personal, "c--x", event_line(NOW - timedelta(hours=1), "claude-fable-5", 100, 0, "C:\\p\\x"))
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.models(7)
        self.assertEqual(r["status"], "ok")
        self.assertIsNone(r["meter_source"])
        self.assertIsNone(r["models"][0]["meter_utilization_pct"])

    def test_model_usage_never_joins_another_accounts_meter(self):
        self.fx.session_log(self.work, "c--x", event_line(NOW - timedelta(hours=1), "claude-fable-5", 100, 0, "C:\\p\\x"))
        self.fx.snapshot("Home", tiers=[{"key": "seven_day_fable", "utilization": 8,
                                         "resets_at": iso(NOW + timedelta(days=5)), "used": None, "limit": None}])
        self.share(entry("Home", "meters"), entry("Work", "activity", active=False, names="names", live_folder=self.work))
        r = self.models(7)
        self.assertEqual(r["total_tokens"], 100)
        self.assertIsNone(r["meter_source"])
        self.assertIsNone(r["models"][0]["meter_utilization_pct"])

    def test_model_usage_parity_fixture_by_tier(self):
        d = os.path.join(self.personal, "projects", "p")
        os.makedirs(d)
        shutil.copy(os.path.join(FIXTURES, "cc-logs", "by-tier.jsonl"), d)
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = mcp.build_model_usage(1, now=datetime(2026, 5, 5, 12, 0, tzinfo=timezone.utc), paths=self.paths)
        by = {m["model"]: (m["tokens"], m["tier_key"]) for m in r["models"]}
        self.assertEqual(r["total_tokens"], 2398)
        self.assertEqual(by, {"gpt-4o": (1998, None), "claude-opus-4-7": (300, "seven_day_opus"),
                              "claude-sonnet-4-6": (100, "seven_day_sonnet")})

    # -- get_usage with activity --

    def test_get_usage_adds_this_accounts_local_burn_only(self):
        captured = NOW - timedelta(minutes=2)
        self.fx.session_log(self.personal, "c--x",
                            event_line(NOW - timedelta(minutes=5), "claude-fable-5", 999, 0, "/x"),   # before the snapshot
                            event_line(NOW - timedelta(minutes=1), "claude-fable-5", 30, 10, "/x"),
                            event_line(NOW - timedelta(minutes=1), "claude-opus-4", 5, 5, "/x"))
        self.fx.session_log(self.work, "c--y", event_line(NOW - timedelta(minutes=1), "claude-fable-5", 7000, 0, "/y"))
        self.fx.snapshot("Home", captured=captured)
        self.share(entry("Home", "activity", names="names", live_folder=self.personal),
                   entry("Work", "activity", active=False, names="names", live_folder=self.work))
        burn = self.usage()["local_burn_since_snapshot"]
        self.assertEqual(burn["total_tokens"], 50)
        self.assertEqual(burn["by_tier"], {"seven_day_fable": 40, "seven_day_opus": 10})
        self.assertEqual(list(burn["by_tier"]), ["seven_day_fable", "seven_day_opus"])

    # -- get_usage_history (Windows ToolLogicAbilitiesTests) --

    def test_history_with_no_vault_is_missing(self):
        self.share(entry("Home", "activity", names="names", live_folder=self.personal))
        r = self.history(30)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "missing"))
        self.assertIn("record", r["remedy"])

    def test_history_serves_recorded_days_with_split_and_omits_absent_days(self):
        self.fx.rollup(VAULT_A, "2026-07", {
            "2026-07-24": {"total": 500000, "input": 100000, "output": 400000,
                           "by_project": {"Sanduhr~abc123": 300000, "vibe-plugins~def456": 200000}},
            "2026-07-25": {"total": 250000, "input": 50000, "output": 200000},
        })
        self.share(entry("Home", "activity", names="names", vault_id=VAULT_A, live_folder=self.personal))
        r = self.history(7)
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["total_tokens"], 750000)
        self.assertEqual(r["days_recorded"], 2)
        self.assertEqual(r["days"][0], {"date": "2026-07-24", "tokens": 500000, "sent": 100000, "received": 400000})
        self.assertNotIn("2026-07-23", json.dumps(r))
        self.assertEqual(r["top_projects"][0], {"name": "Sanduhr", "tokens": 300000})
        self.assertIn("no-record", r["caveat"])
        self.assertEqual((r["from"], r["to"]), ("2026-07-20", "2026-07-26"))
        self.assertEqual(r["roots_scanned"], [ref("Home")])

    def test_history_window_excludes_days_before_the_range(self):
        self.fx.rollup(VAULT_A, "2026-07", {"2026-07-10": {"total": 999}, "2026-07-25": {"total": 111}})
        self.share(entry("Home", "activity", names="names", vault_id=VAULT_A))
        r = self.history(7)
        self.assertEqual(r["total_tokens"], 111)
        self.assertEqual(r["days_recorded"], 1)

    def test_history_spans_months_and_merges_accounts(self):
        self.fx.rollup(VAULT_A, "2026-06", {"2026-06-30": {"total": 10, "input": 4, "output": 6}})
        self.fx.rollup(VAULT_A, "2026-07", {"2026-07-01": {"total": 20}})
        self.fx.rollup(VAULT_B, "2026-07", {"2026-07-01": {"total": 5, "by_project": {"p-0123456789": 5}}})
        self.share(entry("Home", "activity", names="names", vault_id=VAULT_A),
                   entry("Work", "activity", active=False, names="hidden", vault_id=VAULT_B))
        r = self.history(30)
        self.assertEqual([(d["date"], d["tokens"]) for d in r["days"]], [("2026-06-30", 10), ("2026-07-01", 25)])
        self.assertEqual(r["top_projects"], [{"name": "p-0123456789", "tokens": 5}])

    def test_history_reads_only_the_vaults_the_file_names(self):
        self.fx.rollup(VAULT_A, "2026-07", {"2026-07-25": {"total": 111}})
        self.fx.rollup(VAULT_B, "2026-07", {"2026-07-25": {"total": 999}})
        # Work keeps a record but shares meters only: its vault is never read.
        self.share(entry("Home", "activity", names="names", vault_id=VAULT_A),
                   entry("Work", "meters", active=False, vault_id=VAULT_B))
        self.assertEqual(self.history(7)["total_tokens"], 111)

    def test_hidden_record_returns_what_the_vault_stored(self):
        code = mcp.hidden_name("secret-client")
        self.fx.rollup(VAULT_A, "2026-07", {"2026-07-25": {"total": 40, "by_project": {code: 40}}})
        self.share(entry("Home", "activity", names="hidden", vault_id=VAULT_A))
        self.assertEqual(self.history(7)["top_projects"], [{"name": code, "tokens": 40}])

    def test_corrupt_rollups_read_as_empty(self):
        d = os.path.join(self.fx.support, "vault", VAULT_A)
        os.makedirs(d)
        with open(os.path.join(d, "rollups-2026-07.json"), "w") as f:
            f.write("{torn")
        self.share(entry("Home", "activity", names="names", vault_id=VAULT_A))
        self.assertEqual(self.history(7)["reason"], "missing")

    def test_ping_summarises_without_labels(self):
        self.fx.snapshot("Home")
        self.fx.write_json("history.Home.json", {"five_hour": [{"t": iso(NOW), "v": 1}]})
        self.share(entry("Home", "activity", names="hidden", vault_id=VAULT_A, live_folder=self.personal),
                   entry("Work", "meters", active=False))
        p = self.ping()
        self.assertEqual(p["usage_status"], "ok")
        self.assertTrue(p["history_found"])
        self.assertEqual(p["sharing"], {
            "access_file": "ok", "access_schema_supported": 1, "accounts_shared": 2, "meters_only": 1,
            "meters_and_activity": 1, "activity_read": 1, "records_shared": 1, "active_account": "activity"})
        self.assertEqual(p["cc_roots_consented"], [ref("Home")])
        self.assertEqual(p["tools_available"],
                         ["get_usage", "get_local_burn_by_project", "get_model_usage", "get_usage_history", "ping",
                          "get_desk_messages", "propose_desk_messages", "propose_theme",
                          "watch_start", "watch_update", "watch_end"])
        self.assertFalse(p["watchers_allowed"])
        self.assertEqual(sorted(p["tools_not_on_mac"]), ["publish_usage"])
        text = json.dumps(p)
        for secret in ("Home", "Work", ".claude-personal", VAULT_A):
            self.assertNotIn(secret, text)


class GetUsageParity(Base):
    """Windows ToolLogicAbilitiesTests pacing cases, through a shared account."""

    def setUp(self):
        super().setUp()
        self.fx.access(entry("Home", "meters"))

    def tier(self, util, resets_in):
        self.fx.snapshot("Home", captured=NOW - timedelta(minutes=1), tiers=[
            {"key": "five_hour", "utilization": util, "resets_at": iso(NOW + resets_in), "used": None, "limit": None}])
        return self.usage()["tiers"][0]["pace"]

    def test_ahead_of_pace(self):
        pace = self.tier(80, timedelta(hours=4))
        self.assertEqual(pace["verdict"], "ahead")
        self.assertEqual(pace["cooldown_seconds"], 10800)
        self.assertIsNone(pace["surplus_pct"])
        self.assertEqual(pace["projected_final_pct"], 200)

    def test_under_pace(self):
        pace = self.tier(10, timedelta(hours=2.5))
        self.assertEqual(pace["verdict"], "under")
        self.assertEqual(pace["surplus_pct"], 40)
        self.assertIsNone(pace["cooldown_seconds"])
        self.assertEqual(pace["projected_final_pct"], 20)


class Protocol(Base):
    def run_server(self, *messages):
        env = dict(os.environ, SANDUHR_SUPPORT_DIR=self.fx.support)
        env.pop("SANDUHR_SNAPSHOT", None)
        import subprocess
        out = subprocess.run([sys.executable, SERVER], input="\n".join(json.dumps(m) for m in messages) + "\n",
                             capture_output=True, text=True, env=env, timeout=30)
        self.assertEqual(out.stderr, "")
        return [json.loads(line) for line in out.stdout.splitlines()]

    def test_tools_list_and_calls_over_stdio(self):
        self.fx.access(entry("Home", "meters"))
        self.fx.snapshot("Home", captured=datetime.now(timezone.utc))
        frames = self.run_server(
            {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {}},
            {"jsonrpc": "2.0", "method": "notifications/initialized"},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
            {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "get_usage"}},
            {"jsonrpc": "2.0", "id": 4, "method": "tools/call",
             "params": {"name": "get_local_burn_by_project", "arguments": {"window_days": "7"}}},
            {"jsonrpc": "2.0", "id": 5, "method": "tools/call", "params": {"name": "publish_usage"}},
            {"jsonrpc": "2.0", "id": 6, "method": "tools/call", "params": {"name": "get_usage_history", "arguments": {"window_days": 7}}},
        )
        self.assertEqual([f.get("id") for f in frames], [1, 2, 3, 4, 5, 6])
        names = [t["name"] for t in frames[1]["result"]["tools"]]
        self.assertEqual(names, ["get_usage", "get_local_burn_by_project", "get_model_usage", "get_usage_history", "ping",
                                 "get_desk_messages", "propose_desk_messages", "propose_theme",
                                 "watch_start", "watch_update", "watch_end"])
        for t in frames[1]["result"]["tools"]:
            if t["name"] == "propose_desk_messages":
                # The one tool that asks for a change: not read-only, never destructive, no path.
                self.assertFalse(t["annotations"]["readOnlyHint"])
                self.assertFalse(t["annotations"]["destructiveHint"])
                self.assertEqual(sorted(t["inputSchema"]["properties"]), ["lines", "mode", "note"])
                self.assertFalse(t["inputSchema"]["additionalProperties"])
                continue
            if t["name"].startswith("watch_"):
                # Item 66: they change what Sanduhr shows, never destructive, no path, closed shapes.
                self.assertFalse(t["annotations"]["readOnlyHint"])
                self.assertFalse(t["annotations"]["destructiveHint"])
                self.assertFalse(t["inputSchema"]["additionalProperties"])
                self.assertEqual(sorted(t["inputSchema"]["properties"]),
                                 {"watch_start": ["link", "short", "title", "total", "work"],
                                  "watch_update": ["done", "id", "note", "state"],
                                  "watch_end": ["id", "note", "result"]}[t["name"]])
                continue
            if t["name"] == "propose_theme":
                # The Windows tool's inputs, no path.
                self.assertFalse(t["annotations"]["readOnlyHint"])
                self.assertFalse(t["annotations"]["destructiveHint"])
                self.assertEqual(sorted(t["inputSchema"]["properties"]), ["apply", "save_as", "theme"])
                self.assertEqual(t["inputSchema"]["required"], ["theme"])
                self.assertFalse(t["inputSchema"]["additionalProperties"])
                continue
            self.assertTrue(t["annotations"]["readOnlyHint"])
            # No free-form argument: closed integer enums and booleans only.
            for prop in t["inputSchema"]["properties"].values():
                self.assertTrue(prop["type"] == "boolean" or (prop["type"] == "integer" and "enum" in prop))
            self.assertFalse(t["inputSchema"]["additionalProperties"])
        usage = json.loads(frames[2]["result"]["content"][0]["text"])
        self.assertEqual(usage["status"], "ok")
        bad = json.loads(frames[3]["result"]["content"][0]["text"])
        self.assertEqual(bad["reason"], "invalid_params")
        self.assertEqual(frames[4]["error"]["code"], -32602)
        self.assertNotIn("publish_usage", frames[4]["error"]["message"])   # never echoes the request
        hist = json.loads(frames[5]["result"]["content"][0]["text"])
        self.assertEqual(hist["reason"], "missing")

    def test_the_server_never_writes_without_a_good_proposal(self):
        self.fx.access(entry("Home", "activity", names="names", vault_id=VAULT_A))
        before = sorted(os.listdir(self.fx.support))
        self.run_server(*[{"jsonrpc": "2.0", "id": i, "method": "tools/call", "params": {"name": n}}
                          for i, n in enumerate(mcp.TOOL_NAMES)])
        self.assertEqual(sorted(os.listdir(self.fx.support)), before)


class LocalDays(Base):
    def test_history_uses_the_local_calendar_day(self):
        os.environ["TZ"] = "America/Chicago"
        time.tzset()
        try:
            self.fx.rollup(VAULT_A, "2026-07", {"2026-07-25": {"total": 7}, "2026-07-26": {"total": 9}})
            self.fx.access(entry("Home", "activity", names="names", vault_id=VAULT_A))
            # 2026-07-26 03:00 UTC is still the 25th in Chicago.
            r = mcp.build_history(7, now=datetime(2026, 7, 26, 3, 0, tzinfo=timezone.utc), paths=self.paths)
            self.assertEqual(r["to"], "2026-07-25")
            self.assertEqual(r["total_tokens"], 7)
        finally:
            os.environ["TZ"] = "UTC"
            time.tzset()


class DeskMessages(Base):
    """get_desk_messages and propose_desk_messages (item 54): a temp Desk folder beside the temp
    Sanduhr folder, never the real messages.txt; the app's answer is played by a fake sleep."""

    def setUp(self):
        super().setUp()
        self.desk = self.fx.folder("Desk")

    def write_messages(self, text):
        with open(os.path.join(self.desk, "messages.txt"), "w", encoding="utf-8") as f:
            f.write(text)

    def get(self, now=NOW):
        return mcp.build_desk_messages(now=now, paths=self.paths)

    def propose(self, args, answer=None, wait=1.0):
        """Proposes; `answer(request)` returns the app's result payload (None: no answer)."""
        ticks = [0.0]

        def clock():
            return ticks[0]

        def sleep(seconds):
            ticks[0] += seconds
            if answer is None or not os.path.exists(self.paths.desk_request):
                return
            with open(self.paths.desk_request, encoding="utf-8") as f:
                req = json.load(f)
            res = answer(req)
            if res is not None:
                self.fx.write_json(mcp.DESK_RESULT_FILE, {"id": req["id"], "completed_at": iso(NOW), "result": res})

        return mcp.build_propose_desk_messages(args, now=NOW, paths=self.paths, wait=wait, poll=0.25,
                                               sleep=sleep, clock=clock)

    def request(self):
        with open(self.paths.desk_request, encoding="utf-8") as f:
            return json.load(f)

    # -- reading

    def test_paths_stay_in_the_test_folder(self):
        self.assertEqual(self.paths.messages, os.path.join(self.fx.dir, "Desk", "messages.txt"))
        self.assertTrue(self.paths.desk_request.startswith(self.fx.support))

    def test_missing_file(self):
        r = self.get()
        self.assertEqual((r["status"], r["file_found"], r["lines"], r["today"]), ("ok", False, [], None))
        self.assertEqual(r["rotate"], "daily")
        self.assertFalse(r["pinned"])
        self.assertIn("settings_note", r)

    def test_lines_and_today_follow_the_most_specific_pool(self):
        # NOW is Sunday 2026-07-26 (UTC clock in these tests).
        self.write_messages("# header\nkeep building.\n{ink:#fff} also plain\nSun: {glow} rest.\n\n\n")
        r = self.get()
        self.assertEqual(r["lines"], ["# header", "keep building.", "{ink:#fff} also plain", "Sun: {glow} rest."])
        self.assertEqual(r["today"], "{glow} rest.")
        self.assertEqual(r["today_special"], [])
        # Item 69: a date line adds above the day's line; it no longer replaces it.
        self.write_messages("Sun: rest.\n07-26: {write} today only.\n07-26: and Sam's birthday.\n")
        r = self.get()
        self.assertEqual(r["today"], "rest.")
        self.assertEqual(r["today_special"], ["{write} today only.", "and Sam's birthday."])
        self.write_messages("Mon: monday.\na\nb\nc\n")
        # Plain pool, rotated by the day number like MessageEngine.pick.
        self.assertEqual(self.get()["today"], ["a", "b", "c"][NOW.date().toordinal() % 3])

    def test_hourly_rotation_and_pin_from_the_state_file(self):
        self.write_messages("a\nb\nc\n")
        self.fx.write_json(mcp.DESK_STATE_FILE, {"schema_version": 1, "pinned": False, "rotate": "hourly"})
        r = self.get()
        self.assertEqual(r["rotate"], "hourly")
        self.assertNotIn("settings_note", r)
        self.assertEqual(r["today"], ["a", "b", "c"][(NOW.date().toordinal() * 24 + NOW.hour) % 3])
        self.fx.write_json(mcp.DESK_STATE_FILE, {"schema_version": 1, "pinned": True,
                                                 "pinned_line": "{shimmer} pinned.", "rotate": "daily"})
        r = self.get()
        self.assertTrue(r["pinned"])
        self.assertEqual(r["today"], "{shimmer} pinned.")

    def test_weekday_lines_rotate_by_week_and_mix(self):
        # Item 69: seven Friday lines used to show the same one every Friday.
        for n in range(1, 9):
            text = "plain.\n" + "\n".join("Fri: f%d" % i for i in range(n))
            fridays = [datetime(2026, 10, 9, 12) + timedelta(days=7 * i) for i in range(n)]
            self.assertEqual({mcp.pick_desk_line(text, d, False) for d in fridays},
                             {"f%d" % i for i in range(n)}, n)
            hours = {mcp.pick_desk_line(text, datetime(2026, 10, 9, h), True) for h in range(24)}
            self.assertEqual(hours, {"f%d" % i for i in range(n)}, n)
        text = "a\nb\nFri: f\n"
        fridays = [datetime(2026, 10, 9, 12) + timedelta(days=7 * i) for i in range(3)]
        self.assertEqual({mcp.pick_desk_line(text, d, False) for d in fridays}, {"f"})
        self.assertEqual({mcp.pick_desk_line(text, d, False, mix=True) for d in fridays}, {"f", "a", "b"})
        self.write_messages("a\nb\nSun: s\n")
        self.fx.write_json(mcp.DESK_STATE_FILE, {"schema_version": 1, "pinned": False, "rotate": "daily", "mix_daily": True})
        r = self.get()
        self.assertTrue(r["mix_daily"])
        self.assertEqual(r["today"], ["s", "a", "b"][(NOW.date().toordinal() // 7) % 3])

    def test_more_than_three_for_a_date_take_turns_hourly(self):
        for n in range(4, 9):
            text = "keep.\n" + "\n".join("03-14: %d" % i for i in range(n))
            seen = set()
            for h in range(24):
                special, usual = mcp.desk_today(text, datetime(2026, 3, 14, h), False)
                self.assertEqual((len(special), len(set(special)), usual), (3, 3, "keep."))
                seen.update(special)
            self.assertEqual(seen, {str(i) for i in range(n)}, n)
        self.assertEqual(mcp.desk_today("03-14: a\n03-14: b\n", datetime(2026, 3, 14, 9), False), (["a", "b"], None))

    def test_a_pinned_line_keeps_special_days(self):
        # NOW is 2026-07-26: a pinned line replaces the usual line; date lines still stack above it.
        self.write_messages("keep.\nSun: rest.\n07-26: happy birthday, Sam.\n")
        self.fx.write_json(mcp.DESK_STATE_FILE, {"schema_version": 1, "pinned": True,
                                                 "pinned_line": "Good vibes only", "rotate": "daily"})
        r = self.get()
        self.assertEqual((r["today"], r["today_special"]), ("Good vibes only", ["happy birthday, Sam."]))
        self.write_messages("keep.\n" + "\n".join("07-26: b%d" % i for i in range(4)) + "\n")
        r = self.get()
        self.assertEqual(r["today"], "Good vibes only")
        self.assertEqual(len(r["today_special"]), 3)
        seen = set()
        for h in range(24):
            special, usual = mcp.desk_today("keep.\n" + "\n".join("07-26: b%d" % i for i in range(4)),
                                            datetime(2026, 7, 26, h), False, pinned="Good vibes only")
            self.assertEqual((len(special), usual), (3, "Good vibes only"))
            seen.update(special)
        self.assertEqual(seen, {"b0", "b1", "b2", "b3"})

    def test_unknown_state_schema_reads_as_defaults(self):
        self.fx.write_json(mcp.DESK_STATE_FILE, {"schema_version": 9, "pinned": True, "rotate": "hourly"})
        r = self.get()
        self.assertFalse(r["pinned"])
        self.assertEqual(r["rotate"], "daily")

    def test_not_utf8_is_refused(self):
        with open(os.path.join(self.desk, "messages.txt"), "wb") as f:
            f.write(b"\xff\xfe bad")
        self.assertEqual(self.get()["reason"], "not_utf8")

    def test_reading_needs_no_sharing(self):
        self.write_messages("hello.\n")
        self.assertFalse(os.path.exists(self.paths.access))
        self.assertEqual(self.get()["lines"], ["hello."])

    # -- the checks

    def test_good_lines_pass(self):
        good = ["keep building.", "Mon: one thing at a time.", "10-31: {ink:#ff7518,#6b2fa0} {write} boo.",
                "{ink:#ff2a6d,#05d9e8} {glow} hello", "{size:0.5}{noglow} small.", "{SHIMMER} loud", "# a note",
                "", "Note: a colon in a plain line.", "02-29: leap.", "{ink:fff} three digits.", "x" * 120,
                "hello {glow} mid-line braces are text", "ünïcödé ✨ fine.", "{sweep} {font:small-caps} hi.",
                "{font:smallcaps} {sweep:20} hi.", "{FONT:Bold Italic} hi.", "{sweep:2} {font:fraktur} hi.",
                "{sweep:3600}{font:double_struck} hi."]
        self.assertEqual(mcp.validate_desk_lines(good), [])

    def test_font_and_sweep_tags(self):
        effects, text, error = mcp.parse_effects("{sweep} {font:smallcaps} {ink:#fff} hi.")
        self.assertIsNone(error)
        self.assertEqual(text, "hi.")
        self.assertEqual(effects, {"sweep": True, "font": "small-caps", "ink": ["#fff"]})
        self.assertEqual(mcp.parse_effects("{sweep:20} x")[0], {"sweep": 20.0})
        for name in mcp.FONT_STYLES.split(", "):
            self.assertEqual(mcp.parse_effects("{font:%s} x" % name)[0], {"font": name})
        # The names are the statusline's letter styles.
        spec = importlib.util.spec_from_file_location("sanduhr_statusline_names", os.path.join(
            os.path.dirname(HERE), "sanduhr_statusline.py"))
        statusline = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(statusline)
        self.assertEqual(set(mcp.FONT_STYLES.split(", ")), set(statusline.FONTS))

    def test_bad_lines_are_named(self):
        cases = {
            "x" * 121: "121 characters",
            "tab\there": "control character",
            "line\nbreak": "control character",
            "sep arator": "control character",
            "13-01: no such month": "not a date",
            "1-5: short date": "not a date",
            "02-30: no such day": "not a date",
            "Monday: long name": "write the day as Mon",
            "mon: lowercase": "write the day as Mon",
            "Mon:   ": "prefix but no text",
            "{blink} hi": "unknown effect {blink}",
            "{ink:#zzzzzz} hi": "not hex",
            "{ink:} hi": "1 to 4 hex colors",
            "{ink:#111,#222,#333,#444,#555} hi": "at most 4",
            "{size:3} big": "0.5 to 2",
            "{size:big} big": "0.5 to 2",
            "{glow:yes} hi": "takes no value",
            "{glow hi": "not closed",
            "{glow} {write}": "no text",
            "{font} hi": "needs a letter style",
            "{font:outline} hi": "needs a letter style: bold, italic",
            "{font:} hi": "needs a letter style",
            "{sweep:1} hi": "from 2 to 3600",
            "{sweep:3601} hi": "from 2 to 3600",
            "{sweep:fast} hi": "from 2 to 3600",
            "{sweep:} hi": "from 2 to 3600",
            "{sweeps} hi": "unknown effect {sweeps}; known: ink, glow, noglow, size, write, shimmer, sweep, font",
            "\ud800 lone surrogate": "UTF-8",
        }
        for line, want in cases.items():
            reasons = mcp.validate_desk_lines([line])
            self.assertEqual(len(reasons), 1, line)
            self.assertIn("line 1", reasons[0])
            self.assertIn(want, reasons[0], line)

    def test_counts_and_types(self):
        self.assertIn("61 lines", mcp.validate_desk_lines(["a"] * 61)[0])
        self.assertEqual(mcp.validate_desk_lines(["a"] * 60), [])
        self.assertIn("list", mcp.validate_desk_lines([])[0])
        self.assertIn("list", mcp.validate_desk_lines("a")[0])
        self.assertIn("not a string", mcp.validate_desk_lines([3])[0])
        self.assertIn("no message line", mcp.validate_desk_lines(["# only", ""])[0])

    # -- proposing

    def test_a_refusal_writes_nothing(self):
        before = sorted(os.listdir(self.fx.support))
        for args in ({"lines": ["{blink} x"]}, {"lines": ["ok"], "mode": "merge"}, {"lines": ["ok"], "note": "a\nb"},
                     {"lines": ["ok"], "path": "/etc"}, {}, None):
            r = self.propose(args)
            self.assertEqual(r["status"], "rejected")
            self.assertTrue(r["reasons"])
        self.assertEqual(sorted(os.listdir(self.fx.support)), before)
        self.assertFalse(os.path.exists(os.path.join(self.desk, "messages.txt")))

    def test_the_request_file_and_a_pending_answer(self):
        seen = []

        def app(req):
            seen.append(req)
            return {"status": "pending_approval"}

        r = self.propose({"lines": ["  {glow} hi.  ", "Fri: showtime."], "mode": "replace", "note": " for fridays "}, app)
        self.assertEqual(r["status"], "pending_approval")
        req = seen[0]
        self.assertEqual(r["request_id"], req["id"])
        self.assertEqual(req["schema_version"], 1)
        self.assertEqual(req["lines"], ["{glow} hi.", "Fri: showtime."])
        self.assertEqual((req["mode"], req["note"]), ("replace", "for fridays"))
        self.assertTrue(mcp.parse(req["requested_at"]))
        self.assertEqual(os.stat(self.paths.desk_request).st_mode & 0o777, 0o600)
        self.assertFalse(os.path.exists(self.paths.desk_request + ".tmp"))
        self.assertFalse(os.path.exists(os.path.join(self.desk, "messages.txt")))   # never the list itself

    def test_applied_and_rejected_answers_pass_through(self):
        r = self.propose({"lines": ["a."]}, lambda req: {"status": "applied", "mode": "add", "lines_added": 1,
                                                         "lines_skipped": 0, "extra": "dropped"})
        self.assertEqual((r["status"], r["lines_added"], r["mode"]), ("applied", 1, "add"))
        self.assertNotIn("extra", r)
        r = self.propose({"lines": ["b."]}, lambda req: {"status": "rejected", "reasons": ["line 1 is bad"]})
        self.assertEqual(r["reasons"], ["line 1 is bad"])

    def test_another_requests_result_is_not_taken(self):
        self.fx.write_json(mcp.DESK_RESULT_FILE, {"id": "someone-else", "result": {"status": "applied"}})
        r = self.propose({"lines": ["a."]}, answer=lambda req: None, wait=1.0)
        self.assertEqual((r["status"], r["reason"]), ("queued", "app_not_responding"))
        self.assertTrue(os.path.exists(self.paths.desk_request))   # left for the app to pick up

    def test_the_support_folder_is_created(self):
        shutil.rmtree(self.fx.support)
        r = self.propose({"lines": ["a."]}, wait=0)
        self.assertEqual(r["status"], "queued")
        self.assertTrue(os.path.isfile(self.paths.desk_request))

    def test_over_stdio(self):
        self.write_messages("hello.\n")
        env = dict(os.environ, SANDUHR_SUPPORT_DIR=self.fx.support)
        import subprocess
        msgs = [{"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "get_desk_messages"}},
                {"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                 "params": {"name": "propose_desk_messages", "arguments": {"lines": ["{nope} x"]}}}]
        out = subprocess.run([sys.executable, SERVER], input="\n".join(json.dumps(m) for m in msgs) + "\n",
                             capture_output=True, text=True, env=env, timeout=30)
        self.assertEqual(out.stderr, "")
        frames = [json.loads(x) for x in out.stdout.splitlines()]
        got = json.loads(frames[0]["result"]["content"][0]["text"])
        self.assertEqual(got["lines"], ["hello."])
        refused = json.loads(frames[1]["result"]["content"][0]["text"])
        self.assertEqual(refused["status"], "rejected")
        self.assertFalse(os.path.exists(self.paths.desk_request))

    def test_descriptions_teach_the_syntax_and_effects(self):
        tools = {t["name"]: t["description"] for t in mcp.TOOLS}
        for name in ("get_desk_messages", "propose_desk_messages"):
            for word in ("Mon:", "MM-DD", "# ", "rotate", "pinned", "{ink:", "{glow}", "{noglow}", "{size:",
                         "{write}", "{shimmer}", "40 characters", "{sweep}", "{sweep:20}", "2 to 3600",
                         "{font:small-caps}", "bold-italic", "double-struck", "fraktur", "Unicode",
                         "skips {shimmer} and {sweep}"):
                self.assertIn(word, tools[name], (name, word))
        self.assertIn("never writes messages.txt", tools["propose_desk_messages"])
        # Item 69: special days add to the day; Claude learns the birthday form.
        for name in ("get_desk_messages", "propose_desk_messages"):
            for word in ("birthdays, anniversaries and holidays are date lines", "they add to it",
                         "two birthdays on one date both show", "happy birthday, Sam.", "mix_daily",
                         "date lines still stack above the pinned line"):
                self.assertIn(word, tools[name], (name, word))
        self.assertIn("today_special", tools["get_desk_messages"])
        self.assertIn("use mode add with one date line each", tools["propose_desk_messages"])


BUILTIN_THEMES = os.path.join(FIXTURES, "theme-builtins.json")


def clean_theme():
    """A clean theme (Obsidian's values), as the Windows ThemeLintTests' Clean()."""
    return {
        "name": "Test",
        "bg": "#0d0d0d", "glass": "#1c1c1c", "glass_on_mica": "#1a1a1c",
        "title_bg": "#161616", "border": "#333333", "footer_bg": "#111111", "bar_bg": "#2a2a2a",
        "text": "#e8e4dc", "text_secondary": "#b8b4ac", "text_dim": "#777777", "text_muted": "#555555",
        "accent": "#6c63ff", "pace_marker": "#ff6b6b", "sparkline": "#6c63ff",
        "glass_alpha": 0.85, "border_alpha": 0.30,
    }


def fields(findings, level):
    return [f["field"] for f in findings if f["level"] == level]


class ThemeLint(unittest.TestCase):
    """The Windows ThemeLintTests' cases, against the Python port (item 55)."""

    def lint(self, theme):
        return mcp.lint_theme(theme)

    def test_every_built_in_lints_with_no_findings(self):
        with open(BUILTIN_THEMES, encoding="utf-8") as f:
            builtins = json.load(f)
        keys = [k for k in builtins if not k.startswith("_")]
        self.assertEqual(sorted(keys), sorted(k for k in mcp.BUILT_IN_THEME_IDS if k != "match-desk"))
        for key in keys:
            self.assertEqual(self.lint(builtins[key]), [], key)

    def test_clean_theme_is_ok(self):
        self.assertEqual(self.lint(clean_theme()), [])

    def test_not_an_object(self):
        r = self.lint([1, 2])
        self.assertEqual([f["field"] for f in r], ["json"])

    def test_missing_required_color(self):
        j = clean_theme()
        del j["pace_marker"]
        self.assertIn("pace_marker", fields(self.lint(j), "error"))

    def test_malformed_hex_names_the_accepted_form(self):
        for bad in ("#fff", "#ff00ff80", "ff00ff", "#gg0000", "red"):
            j = clean_theme()
            j["accent"] = bad
            errors = [f for f in self.lint(j) if f["level"] == "error"]
            self.assertEqual([f["field"] for f in errors], ["accent"], bad)
            self.assertIn("#rrggbb", errors[0]["message"])

    def test_non_string_color_is_an_error(self):
        j = clean_theme()
        j["bg"] = 12
        self.assertIn("bg", fields(self.lint(j), "error"))

    def test_dial_out_of_range(self):
        for field, value in (("glass_alpha", 1.5), ("border_alpha", -0.1), ("card_corner_radius", 99),
                             ("breath_period_ms", 10), ("ghost_alpha", 2), ("glass_alpha", True), ("glass_alpha", "0.8")):
            j = clean_theme()
            j[field] = value
            self.assertIn(field, fields(self.lint(j), "error"), (field, value))

    def test_nested_dials_use_dotted_names(self):
        j = clean_theme()
        j["accent_bloom"] = {"blur": 40, "alpha": 0.5}
        j["inner_highlight"] = {"color": "nope", "alpha": 0.2}
        errors = fields(self.lint(j), "error")
        self.assertIn("accent_bloom.blur", errors)
        self.assertIn("inner_highlight.color", errors)
        j = clean_theme()
        j["accent_bloom"] = 3
        j["inner_highlight"] = {"alpha": 0.2}
        errors = fields(self.lint(j), "error")
        self.assertIn("accent_bloom", errors)
        self.assertIn("inner_highlight.color", errors)

    def test_name_rules(self):
        j = clean_theme()
        for name in ("", "   ", "x" * 25, None, 7, "two\nlines"):
            j["name"] = name
            self.assertIn("name", fields(self.lint(j), "error"), name)
        del j["name"]
        self.assertIn("name", fields(self.lint(j), "error"))
        j["name"] = "x" * 24
        self.assertEqual(self.lint(j), [])

    def test_present_nulls_are_fine(self):
        j = clean_theme()
        j.update({"border_tint": None, "inner_highlight": None, "accent_bloom": None, "description": None,
                  "monospace_font": None})
        self.assertEqual(self.lint(j), [])

    def test_mac_fields(self):
        j = clean_theme()
        j.update({"description": "Night sea glass.", "ghost_alpha": 0.6, "monospace_font": "SF Mono"})
        self.assertEqual(self.lint(j), [])
        for field, value in (("description", "x" * 201), ("description", "a\nb"), ("description", 3),
                             ("monospace_font", True), ("opts_out_of_mica", "yes")):
            j = clean_theme()
            j[field] = value
            self.assertIn(field, fields(self.lint(j), "error"), (field, value))

    def test_light_base_warns_and_still_passes(self):
        j = clean_theme()
        j["glass_on_mica"] = "#f0f0f0"
        j["glass"] = "#f0f0f0"
        r = self.lint(j)
        self.assertEqual(fields(r, "error"), [])
        self.assertIn("glass_on_mica", fields(r, "warning"))
        self.assertIn("glass", fields(r, "warning"))
        self.assertNotIn("bg", fields(r, "warning"))

    def test_low_text_contrast_names_the_ratio(self):
        j = clean_theme()
        j["text"] = "#5a5a5a"
        text = [f for f in self.lint(j) if f["field"] == "text"]
        self.assertEqual(len(text), 1)
        self.assertIn(":1", text[0]["message"])
        self.assertIn("4.5", text[0]["message"])

    def test_text_ramp_descends_and_shares_a_hue(self):
        j = clean_theme()
        j["text_dim"] = "#ffffff"
        self.assertIn("text_dim", fields(self.lint(j), "warning"))
        j = clean_theme()
        j.update({"text": "#ffb0b0", "text_secondary": "#b0ffb0", "text_dim": "#802020", "text_muted": "#501010"})
        self.assertIn("text_secondary", fields(self.lint(j), "warning"))
        g = clean_theme()
        g.update({"text": "#eeeeee", "text_secondary": "#bbbbbb", "text_dim": "#777777", "text_muted": "#555555"})
        self.assertNotIn("text_secondary", fields(self.lint(g), "warning"))

    def test_pace_marker_on_the_green_fill(self):
        j = clean_theme()
        j["pace_marker"] = "#4ade80"
        self.assertIn("pace_marker", fields(self.lint(j), "warning"))
        j["pace_marker"] = "#fbbf24"
        self.assertNotIn("pace_marker", fields(self.lint(j), "warning"))

    def test_sparkline_and_border_tint_share_the_accent_hue(self):
        j = clean_theme()
        j["sparkline"] = "#ff8800"
        j["border_tint"] = "#00ff88"
        r = fields(self.lint(j), "warning")
        self.assertIn("sparkline", r)
        self.assertIn("border_tint", r)

    def test_mica_opt_out_with_translucent_glass(self):
        j = clean_theme()
        j["opts_out_of_mica"] = True
        self.assertIn("glass_alpha", fields(self.lint(j), "warning"))
        j["glass_alpha"] = 1.0
        self.assertNotIn("glass_alpha", fields(self.lint(j), "warning"))

    def test_finding_shape(self):
        j = clean_theme()
        del j["bg"]
        one = self.lint(j)[0]
        self.assertEqual((one["level"], one["field"]), ("error", "bg"))
        self.assertTrue(one["message"])

    def test_color_math(self):
        white, black = mcp.theme_hex("#ffffff"), mcp.theme_hex("#000000")
        red, green = mcp.theme_hex("#ff0000"), mcp.theme_hex("#00ff00")
        self.assertAlmostEqual(mcp.luminance(white), 1.0, 3)
        self.assertAlmostEqual(mcp.contrast(mcp.luminance(white), mcp.luminance(black)), 21.0, 1)
        self.assertAlmostEqual(mcp.hue(red), 0, 1)
        self.assertAlmostEqual(mcp.hue(green), 120, 1)
        self.assertAlmostEqual(mcp.hue_distance(red, green), 120, 1)
        self.assertIsNone(mcp.hue_distance(red, black))
        self.assertAlmostEqual(mcp.composite(black, 0.5, white)[0], 0.5, 3)

    def test_slug(self):
        self.assertEqual(mcp.theme_slug("  Sunset Neon! "), "sunset-neon")
        self.assertEqual(mcp.theme_slug("Café Noir"), "caf-noir")
        self.assertEqual(mcp.theme_slug("!!!"), "theme")
        self.assertEqual(len(mcp.theme_slug("a" * 50)), 40)


class Watchers(Base):
    """watch_start, watch_update, watch_end (item 66): checked here, refused while Sanduhr's switch
    is off, handed over as request files the app reads and deletes."""

    def setUp(self):
        super().setUp()
        mcp.WATCH_STARTED.clear()
        mcp.WATCH_ENDED.clear()
        self.env = {"CLAUDE_CONFIG_DIR": os.path.join(self.fx.dir, ".claude-work")}

    def switch(self, agents=True, background=False):
        self.fx.write_json("watchers.json", {"agents": agents, "background": background, "schema_version": 1})

    def requests(self):
        names = sorted(n for n in os.listdir(self.fx.support) if n.startswith("watch-request-"))
        out = []
        for n in names:
            self.assertTrue(n.endswith(".json"))
            path = os.path.join(self.fx.support, n)
            self.assertEqual(os.stat(path).st_mode & 0o777, 0o600)
            with open(path, encoding="utf-8") as f:
                out.append(json.load(f))
        return out

    def start(self, args):
        return mcp.build_watch_start(args, now=NOW, paths=self.paths, env=self.env)

    def test_file_names_match_the_app(self):
        self.assertEqual(mcp.WATCH_SWITCH_FILE, "watchers.json")
        self.assertEqual(mcp.WATCH_REQUEST_PREFIX, "watch-request-")
        self.assertEqual((mcp.WATCH_MAX_TITLE, mcp.WATCH_MAX_NOTE), (80, 140))

    def test_refused_while_the_switch_is_off_and_nothing_is_written(self):
        before = sorted(os.listdir(self.fx.support))
        for switch in (None, {"agents": False, "background": True, "schema_version": 1},
                       {"agents": True, "schema_version": 2}, {"agents": "yes", "schema_version": 1}):
            if switch is not None:
                self.fx.write_json("watchers.json", switch)
                before = sorted(os.listdir(self.fx.support))
            r = self.start({"title": "CI on main"})
            self.assertEqual((r["status"], r["reason"]), ("rejected", "watchers_off"))
            self.assertIn("Let agents show watchers", r["remedy"])
            self.assertEqual(sorted(os.listdir(self.fx.support)), before)
        self.assertFalse(mcp.build_ping(now=NOW, paths=self.paths)["watchers_allowed"])

    def test_start_update_end_hand_over_requests_in_order(self):
        self.switch()
        self.assertTrue(mcp.build_ping(now=NOW, paths=self.paths)["watchers_allowed"])
        r = self.start({"title": "  CI on main ", "link": "https://github.com/o/r/actions/runs/1", "total": 12})
        self.assertEqual(r["status"], "ok")
        wid = r["id"]
        self.assertRegex(wid, r"^w[0-9a-f]{12}$")
        u = mcp.build_watch_update({"id": wid, "done": 3, "note": "lint passed", "state": "waiting"},
                                   now=NOW, paths=self.paths)
        self.assertEqual(u, {"status": "ok", "id": wid})
        e = mcp.build_watch_end({"id": wid, "result": "failed", "note": "2 checks failed"}, now=NOW, paths=self.paths)
        self.assertEqual(e, {"status": "ok", "id": wid})
        reqs = self.requests()
        self.assertEqual([q["op"] for q in reqs], ["start", "update", "end"])
        self.assertEqual(reqs[0], {"schema_version": 1, "op": "start", "id": wid, "requested_at": mcp.iso_o(NOW),
                                   "title": "CI on main", "link": "https://github.com/o/r/actions/runs/1",
                                   "total": 12, "work": False, "folder": self.env["CLAUDE_CONFIG_DIR"]})
        self.assertEqual((reqs[1]["done"], reqs[1]["note"], reqs[1]["state"]), (3, "lint passed", "waiting"))
        self.assertEqual((reqs[2]["result"], reqs[2]["note"]), ("failed", "2 checks failed"))
        # No temporary file is left behind.
        self.assertEqual([n for n in os.listdir(self.fx.support) if n.endswith(".tmp")], [])
        # An ended watcher takes no more updates.
        again = mcp.build_watch_update({"id": wid, "done": 4}, now=NOW, paths=self.paths)
        self.assertEqual(again["reason"], "ended")

    def test_short_is_carried_and_checked(self):
        self.switch()
        r = self.start({"title": "PR 140 CI: combine statuslines", "short": " PR 140 ", "total": 12})
        self.assertEqual(r["status"], "ok")
        self.assertEqual(self.requests()[0]["short"], "PR 140")
        self.assertIn("short", mcp.WATCH_GUIDE_START)
        before = sorted(os.listdir(self.fx.support))
        for bad in ("x" * 13, "two\nlines", 7):
            r = self.start({"title": "ok", "short": bad})
            self.assertEqual(r["reason"], "invalid_params", bad)
        self.assertEqual(sorted(os.listdir(self.fx.support)), before)
        r = self.start({"title": "no short"})
        self.assertNotIn("short", self.requests()[-1])

    def test_work_flag_and_default_folder(self):
        self.switch()
        r = mcp.build_watch_start({"title": "Deploy", "work": True}, now=NOW, paths=self.paths, env={})
        self.assertEqual(r["status"], "ok")
        req = self.requests()[0]
        self.assertTrue(req["work"])
        self.assertEqual(req["folder"], os.path.expanduser("~/.claude"))
        self.assertNotIn("link", req)
        self.assertNotIn("total", req)

    def test_bad_arguments_are_refused_and_write_nothing(self):
        self.switch()
        before = sorted(os.listdir(self.fx.support))
        for args in ({}, None, {"title": ""}, {"title": "   "}, {"title": "x" * 81}, {"title": "two\nlines"},
                     {"title": 5}, {"title": "ok", "link": "http://example.com"},
                     {"title": "ok", "link": "javascript:alert(1)"}, {"title": "ok", "link": "https://u:p@example.com"},
                     {"title": "ok", "link": "https://" + "a" * 2048}, {"title": "ok", "total": 0},
                     {"title": "ok", "total": 1000001}, {"title": "ok", "total": True}, {"title": "ok", "total": 2.5},
                     {"title": "ok", "work": "yes"}, {"title": "ok", "path": "/etc"}):
            r = self.start(args)
            self.assertEqual((r["status"], r["reason"]), ("rejected", "invalid_params"), args)
        self.assertEqual(sorted(os.listdir(self.fx.support)), before)

    def test_update_and_end_need_a_started_id(self):
        self.switch()
        wid = self.start({"title": "Build", "total": 4})["id"]
        n = len(self.requests())
        for args, reason in (({"done": 1}, "invalid_params"), ({"id": "nope"}, "invalid_params"),
                             ({"id": "w000000000000"}, "unknown_id"),
                             ({"id": wid, "state": "done"}, "invalid_params"),
                             ({"id": wid, "done": -1}, "invalid_params"),
                             ({"id": wid, "note": "y" * 141}, "invalid_params"),
                             ({"id": wid, "extra": 1}, "invalid_params")):
            r = mcp.build_watch_update(args, now=NOW, paths=self.paths)
            self.assertEqual(r["reason"], reason, args)
        for args in ({"id": wid}, {"id": wid, "result": "finished"}, {"id": wid, "result": "passed", "note": 3}):
            self.assertEqual(mcp.build_watch_end(args, now=NOW, paths=self.paths)["reason"], "invalid_params")
        self.assertEqual(len(self.requests()), n)

    def test_turning_the_switch_off_refuses_updates_too(self):
        self.switch()
        wid = self.start({"title": "Build"})["id"]
        self.switch(agents=False)
        r = mcp.build_watch_update({"id": wid, "done": 1}, now=NOW, paths=self.paths)
        self.assertEqual(r["reason"], "watchers_off")

    def test_over_stdio(self):
        self.switch()
        env = dict(os.environ, SANDUHR_SUPPORT_DIR=self.fx.support, CLAUDE_CONFIG_DIR=self.env["CLAUDE_CONFIG_DIR"])
        import subprocess
        msgs = [{"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                 "params": {"name": "watch_start", "arguments": {"title": "CI", "total": 2}}}]
        out = subprocess.run([sys.executable, SERVER], input="\n".join(json.dumps(m) for m in msgs) + "\n",
                             capture_output=True, text=True, env=env, timeout=30)
        self.assertEqual(out.stderr, "")
        res = json.loads(json.loads(out.stdout.splitlines()[0])["result"]["content"][0]["text"])
        self.assertEqual(res["status"], "ok")
        self.assertEqual(self.requests()[0]["folder"], self.env["CLAUDE_CONFIG_DIR"])


class ProposeTheme(Base):
    """propose_theme (item 55): the Windows ToolLogicThemeTests' cases on a temp folder; the app's
    answer is played by a fake sleep."""

    def propose(self, args, answer=None, wait=1.0):
        ticks = [0.0]

        def clock():
            return ticks[0]

        def sleep(seconds):
            ticks[0] += seconds
            if answer is None or not os.path.exists(self.paths.theme_request):
                return
            with open(self.paths.theme_request, encoding="utf-8") as f:
                req = json.load(f)
            res = answer(req)
            if res is not None:
                self.fx.write_json(mcp.THEME_RESULT_FILE, {"id": req["id"], "completed_at": iso(NOW), "result": res})

        return mcp.build_propose_theme(args, now=NOW, paths=self.paths, wait=wait, poll=0.25, sleep=sleep, clock=clock)

    def test_paths_stay_in_the_test_folder(self):
        self.assertEqual(self.paths.theme_request, os.path.join(self.fx.support, "theme-request.json"))
        self.assertEqual(self.paths.theme_result, os.path.join(self.fx.support, "theme-result.json"))

    def test_broken_theme_is_rejected_with_findings_and_writes_nothing(self):
        before = sorted(os.listdir(self.fx.support))
        bad = clean_theme()
        del bad["accent"]
        r = self.propose({"theme": bad})
        self.assertEqual((r["status"], r["reason"]), ("rejected", "invalid_theme"))
        self.assertIn("accent", fields(r["findings"], "error"))
        self.assertEqual(sorted(os.listdir(self.fx.support)), before)

    def test_bad_arguments_are_typed_invalid_params(self):
        before = sorted(os.listdir(self.fx.support))
        for args in ({}, None, {"theme": "x"}, {"theme": clean_theme(), "save_as": "Bad Key"},
                     {"theme": clean_theme(), "save_as": "-x"}, {"theme": clean_theme(), "save_as": "a" * 41},
                     {"theme": clean_theme(), "apply": "yes"}, {"theme": clean_theme(), "path": "/etc"},
                     {"theme": dict(clean_theme(), padding="x" * 20000)}):
            r = self.propose(args)
            self.assertEqual((r["status"], r["reason"]), ("rejected", "invalid_params"), args and list(args))
            self.assertTrue(r["remedy"])
        self.assertEqual(sorted(os.listdir(self.fx.support)), before)

    def test_a_built_in_name_is_reserved(self):
        for args in ({"theme": dict(clean_theme(), name="Obsidian")}, {"theme": dict(clean_theme(), name="Match Desk")},
                     {"theme": clean_theme(), "save_as": "626-labs"}):
            r = self.propose(args)
            self.assertEqual((r["status"], r["reason"]), ("rejected", "reserved_name"))
        self.assertFalse(os.path.exists(self.paths.theme_request))
        # save_as frees a built-in's display name.
        r = self.propose({"theme": dict(clean_theme(), name="Obsidian"), "save_as": "my-obsidian"}, wait=0)
        self.assertEqual(r["status"], "queued")

    def test_the_request_file_and_a_pending_answer(self):
        seen = []

        def app(req):
            seen.append(req)
            return {"status": "pending_approval", "key": "test", "name": "Test"}

        theme = dict(clean_theme(), description="Quiet graphite.")
        r = self.propose({"theme": theme, "save_as": "graphite", "apply": False}, app)
        self.assertEqual(r["status"], "pending_approval")
        self.assertEqual(r["request_id"], seen[0]["id"])
        self.assertEqual(r["findings"], [])   # the server's lint rides along when the app sends none
        req = seen[0]
        self.assertEqual((req["schema_version"], req["save_as"], req["apply"]), (1, "graphite", False))
        self.assertEqual(req["theme"], theme)
        self.assertTrue(mcp.parse(req["requested_at"]))
        self.assertEqual(os.stat(self.paths.theme_request).st_mode & 0o777, 0o600)
        self.assertFalse(os.path.exists(self.paths.theme_request + ".tmp"))
        self.assertFalse(os.path.exists(os.path.join(self.fx.dir, "Sanduhr", "themes")))   # never a theme itself

    def test_applied_answers_pass_through(self):
        r = self.propose({"theme": clean_theme()}, lambda req: {
            "status": "applied", "key": "test-2", "name": "Test", "previous_key": "obsidian",
            "saved_path": "/x/test-2.json", "renamed_from": "test", "findings": [], "extra": "dropped"})
        self.assertEqual((r["status"], r["key"], r["previous_key"], r["renamed_from"]),
                         ("applied", "test-2", "obsidian", "test"))
        self.assertNotIn("extra", r)
        self.assertTrue(self.propose({"theme": clean_theme(), "apply": False},
                                     lambda req: {"status": "saved", "key": "test"})["status"] == "saved")

    def test_warnings_do_not_block_but_travel(self):
        j = clean_theme()
        j["text"] = "#5a5a5a"
        r = self.propose({"theme": j}, wait=0)
        self.assertEqual(r["status"], "queued")
        self.assertIn("text", fields(r["findings"], "warning"))

    def test_silent_app_returns_queued_and_leaves_the_request(self):
        self.fx.write_json(mcp.THEME_RESULT_FILE, {"id": "someone-else", "result": {"status": "applied"}})
        r = self.propose({"theme": clean_theme()}, answer=lambda req: None)
        self.assertEqual((r["status"], r["reason"], r["name"]), ("queued", "app_not_responding", "Test"))
        self.assertTrue(os.path.exists(self.paths.theme_request))

    def test_over_stdio(self):
        env = dict(os.environ, SANDUHR_SUPPORT_DIR=self.fx.support)
        import subprocess
        msgs = [{"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                 "params": {"name": "propose_theme", "arguments": {"theme": {"name": "Broken"}}}}]
        out = subprocess.run([sys.executable, SERVER], input="\n".join(json.dumps(m) for m in msgs) + "\n",
                             capture_output=True, text=True, env=env, timeout=30)
        self.assertEqual(out.stderr, "")
        refused = json.loads(json.loads(out.stdout.splitlines()[0])["result"]["content"][0]["text"])
        self.assertEqual((refused["status"], refused["reason"]), ("rejected", "invalid_theme"))
        self.assertEqual(len(fields(refused["findings"], "error")), 14)
        self.assertFalse(os.path.exists(self.paths.theme_request))

    def test_description_teaches_the_fields_and_the_rules(self):
        d = next(t["description"] for t in mcp.TOOLS if t["name"] == "propose_theme")
        for word in mcp.THEME_COLOR_FIELDS + ["name", "description", "glass_alpha", "border_alpha", "border_tint",
                                             "accent_bloom", "inner_highlight", "#rrggbb", "4.5:1", "pace_marker",
                                             "Match Desk", "Save and Apply", "pending_approval", "applied", "saved",
                                             "rejected", "queued", "renamed_from", "previous_key", "never writes"]:
            self.assertIn(word, d, word)


class ProjectNames(unittest.TestCase):
    """CcLogReader.ProjectDisplayName with the repo probe injected."""

    def test_worktrees_fold_to_the_repo(self):
        self.assertEqual(mcp.project_display_name("/u/p/api/.claude/worktrees/feat", no_git), "api")
        self.assertEqual(mcp.project_display_name("/u/p/api/.worktrees/feat/sub", no_git), "api")

    def test_nearest_enclosing_repo_wins(self):
        repos = {"/u/p/api"}
        self.assertEqual(mcp.project_display_name("/u/p/api/src/x", lambda d: d in repos), "api")

    def test_basename_otherwise(self):
        self.assertEqual(mcp.project_display_name("C:\\p\\new\\", no_git), "new")

    def test_relative_paths_are_never_probed(self):
        self.assertFalse(mcp.holds_git("C:/Users/x"))


if __name__ == "__main__":
    unittest.main()

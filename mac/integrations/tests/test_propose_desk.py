"""get_desk and propose_desk (2.37.0): a port of Windows' ToolLogicDeskDesignTests.cs for the Mac subset, plus the
shared design fixtures (Tests/SanduhrTests/Fixtures/desk-designs/) that the Swift DeskDesign is held to as well.
Temp folders only; the app's answer is played by a fake sleep. Standard library only:

    python3 -m unittest discover -s mac/integrations/tests
"""
import importlib.util
import json
import os
import shutil
import tempfile
import unittest
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = os.path.join(os.path.dirname(HERE), "sanduhr_mcp.py")
DESIGNS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Tests", "SanduhrTests", "Fixtures", "desk-designs")

spec = importlib.util.spec_from_file_location("sanduhr_mcp_desk", SERVER)
mcp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mcp)

NOW = datetime(2026, 7, 26, 12, 0, 0, tzinfo=timezone.utc)


def state_doc(edit=None):
    doc = {
        "schema_version": 1,
        "writer_version": "2.37.0",
        "design": {"layout": {"clock": {"anchor": "bl", "size": 1, "hidden": False, "tilt": 0},
                              "claude": {"anchor": "bl", "size": 1, "hidden": False, "tilt": 0, "style": "bar"},
                              "meters": {"hidden": True}},
                   "font": "EsteFont Pro", "ink": "#ffffff", "plate": False, "shadow": True},
        "pieces": list(mcp.DESIGN_PIECES),
        "anchors": list(mcp.DESIGN_ANCHORS),
        "fonts": {"desk": "EsteFont Pro", "message": "", "installed": ["Helvetica Neue", "Caveat"],
                  "bundled": list(mcp.DESIGN_BUNDLED_FONTS)},
        "shown": ["clock", "claude"],
        "message_color": "9ad7ff",
        "notch_text_color": "ffffff",
        "limits": {"piece_tilt": {"min": -20, "max": 20},
                   "piece_style": {"pieces": ["claude", "meters", "telemetry"], "values": ["bar", "slim", "ring", "ticks"], "default": "bar"}},
        "secret": "not passed on",
    }
    if edit:
        edit(doc)
    return doc


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="sanduhr-desk-test-")
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.support = os.path.join(self.tmp, "Sanduhr")
        self.paths = mcp.Paths(support_dir=self.support)

    def write_state(self, edit=None, text=None):
        os.makedirs(self.support, exist_ok=True)
        with open(self.paths.design_state, "w", encoding="utf-8") as f:
            f.write(text if text is not None else json.dumps(state_doc(edit)))

    def propose(self, design, answer=None, wait=1.0, extra=None):
        args = {"design": design}
        args.update(extra or {})
        return self.propose_args(args, answer, wait)

    def propose_args(self, args, answer=None, wait=1.0):
        ticks = [0.0]

        def clock():
            return ticks[0]

        def sleep(seconds):
            ticks[0] += seconds
            if answer is None or not os.path.exists(self.paths.design_request):
                return
            req = self.request()
            os.makedirs(self.support, exist_ok=True)
            with open(self.paths.design_result, "w", encoding="utf-8") as f:
                json.dump({"id": req["id"], "completed_at": "2026-07-26T12:00:00.100Z", "result": answer(req)}, f)

        return mcp.build_propose_desk(args, now=NOW, paths=self.paths, wait=wait, poll=0.25, sleep=sleep, clock=clock)

    def request(self):
        with open(self.paths.design_request, encoding="utf-8") as f:
            return json.load(f)

    def assert_no_request(self):
        self.assertFalse(os.path.exists(self.paths.design_request), "a refused design must leave no request file")


class SharedFixtures(unittest.TestCase):
    """The same files and expected.json the Swift DeskDesignFixtureTests runs."""

    def test_the_corpus_is_all_there(self):
        names = sorted(n for n in os.listdir(DESIGNS) if n != "expected.json")
        with open(os.path.join(DESIGNS, "expected.json"), encoding="utf-8") as f:
            expected = json.load(f)
        self.assertEqual(names, sorted(expected["cases"]))
        self.assertGreaterEqual(len(names), 12)

    def test_every_design_is_judged_as_expected(self):
        with open(os.path.join(DESIGNS, "expected.json"), encoding="utf-8") as f:
            expected = json.load(f)
        for name, want in sorted(expected["cases"].items()):
            with self.subTest(name=name):
                with open(os.path.join(DESIGNS, name), encoding="utf-8") as f:
                    design = json.load(f)
                v = mcp.design_validate(design, expected["installed_fonts"])
                self.assertEqual(v["status"], want["status"])
                self.assertEqual(v["reasons"], want["reasons"])
                self.assertEqual(v["fonts"], want["fonts"])
                if "design" in want:
                    self.assertEqual(mcp.design_to_json(v["design"]), want["design"])
                else:
                    self.assertIsNone(v["design"])


class GetDesk(Base):
    def test_it_returns_the_weather_nickname_and_the_new_pieces(self):
        def edit(doc):
            doc["design"]["weather"] = {"nickname": "Funky Town"}
            doc["design"]["layout"]["telemetry"] = {"anchor": "tr", "size": 1, "hidden": False, "tilt": 0, "style": "ring"}
            doc["design"]["layout"]["weather"] = {"anchor": "mr", "size": 1, "hidden": False, "tilt": 0}
        self.write_state(edit)
        r = mcp.build_get_desk(now=NOW, paths=self.paths)
        self.assertEqual(r["design"]["weather"]["nickname"], "Funky Town")
        self.assertEqual(r["design"]["layout"]["telemetry"]["style"], "ring")
        self.assertIn("telemetry", r["pieces"])
        self.assertIn("weather", r["pieces"])

    def test_without_a_state_file_it_says_to_start_sanduhr(self):
        r = mcp.build_get_desk(now=NOW, paths=self.paths)
        self.assertEqual((r["status"], r["reason"]), ("no_data", "missing"))
        self.assertIn("Start Sanduhr", r["remedy"])
        self.assertNotIn("design", r)

    def test_it_returns_the_apps_state_and_nothing_it_did_not_promise(self):
        self.write_state()
        t = NOW.timestamp() - 42
        os.utime(self.paths.design_state, (t, t))
        r = mcp.build_get_desk(now=NOW, paths=self.paths)
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["state_age_seconds"], 42)
        self.assertEqual(r["writer_version"], "2.37.0")
        self.assertEqual(r["design"]["font"], "EsteFont Pro")
        self.assertEqual(r["design"]["layout"]["clock"]["anchor"], "bl")
        self.assertEqual(len(r["pieces"]), 10)
        self.assertEqual(len(r["anchors"]), 8)
        self.assertEqual(r["fonts"]["installed"][1], "Caveat")
        self.assertEqual(r["shown"], ["clock", "claude"])
        self.assertEqual(r["message_color"], "9ad7ff")
        self.assertNotIn("secret", r)
        for windows_only in ("monitors", "wallpaper", "designs", "direct", "start_menu_apps"):
            self.assertNotIn(windows_only, r)

    def test_it_passes_the_tilts_and_the_styles_and_their_limits_through(self):
        def edit(doc):
            doc["design"]["layout"]["clock"]["tilt"] = 12
            doc["design"]["layout"]["claude"]["style"] = "ring"
        self.write_state(edit)
        r = mcp.build_get_desk(now=NOW, paths=self.paths)
        self.assertEqual(r["design"]["layout"]["clock"]["tilt"], 12)
        self.assertEqual(r["design"]["layout"]["claude"]["style"], "ring")
        self.assertEqual(len(r["limits"]["piece_style"]["values"]), 4)
        self.assertEqual(r["limits"]["piece_tilt"], {"min": -20, "max": 20})

    def test_it_names_what_is_wrong_with_a_state_it_cannot_use(self):
        for text, reason in (("not json", "malformed"), ("[]", "malformed"),
                             (json.dumps({"schema_version": 2}), "schema_unsupported"),
                             (json.dumps({"schema_version": True}), "schema_unsupported"),
                             (json.dumps({"design": {}}), "schema_unsupported")):
            with self.subTest(reason=reason, text=text):
                self.write_state(text=text)
                r = mcp.build_get_desk(now=NOW, paths=self.paths)
                self.assertEqual((r["status"], r["reason"]), ("no_data", reason))

    def test_it_will_not_read_a_state_over_a_megabyte(self):
        self.write_state(text=json.dumps({"schema_version": 1, "pad": "x" * (mcp.DESIGN_MAX_STATE_BYTES + 10)}))
        self.assertEqual(mcp.build_get_desk(now=NOW, paths=self.paths)["reason"], "too_large")

    def test_it_writes_nothing(self):
        self.write_state()
        before = sorted(os.listdir(self.support))
        mcp.build_get_desk(now=NOW, paths=self.paths)
        self.assertEqual(sorted(os.listdir(self.support)), before)


class ProposeRefusals(Base):
    def test_the_handoff_names_and_limits_match_the_app(self):
        self.assertEqual((mcp.DESIGN_REQUEST_FILE, mcp.DESIGN_RESULT_FILE, mcp.DESIGN_STATE_FILE),
                         ("desk-request.json", "desk-result.json", "desk-state.json"))
        self.assertEqual(mcp.DESIGN_MAX_JSON_BYTES, 128 * 1024)
        self.assertEqual(mcp.DESIGN_ANSWER_STATUSES, ("previewing", "applied", "rejected", "needs_font"))
        self.assertEqual(self.paths.design_request, os.path.join(self.support, "desk-request.json"))
        self.assertEqual(self.paths.design_result, os.path.join(self.support, "desk-result.json"))
        self.assertEqual(self.paths.design_state, os.path.join(self.support, "desk-state.json"))

    def test_an_unknown_argument_is_refused_without_a_request_file(self):
        r = self.propose_args({"design": {}, "apply": True})
        self.assertEqual(r["status"], "rejected")
        self.assertEqual(r["reasons"][0]["field"], "arguments")
        self.assert_no_request()

    def test_a_missing_or_misshapen_design_is_refused(self):
        for args in ({}, {"design": "dusk"}, {"design": []}, {"design": None}):
            with self.subTest(args=args):
                r = self.propose_args(args)
                self.assertEqual(r["status"], "rejected")
                self.assertEqual(r["reasons"][0]["field"], "design")
                self.assert_no_request()

    def test_a_null_argument_set_is_refused_not_crashed(self):
        r = mcp.build_propose_desk(None, now=NOW, paths=self.paths)
        self.assertEqual(r["status"], "rejected")

    def test_a_bad_design_is_refused_with_its_field_and_leaves_no_request_file(self):
        cases = (
            ({"plate": "yes"}, "plate"),
            ({"ink": "red"}, "ink"),
            ({"layout": {"toaster": {"anchor": "bl"}}}, "layout.toaster"),
            ({"layout": {"clock": {"anchor": "middle"}}}, "layout.clock.anchor"),
            ({"layout": {"clock": {"size": 9}}}, "layout.clock.size"),
            ({"font": "/Library/Fonts/evil.ttf"}, "font"),
            ({"name": "line one\nline two"}, "name"),
            ({"command": "open -a Calculator"}, "design"),
            ({"layout": {"clock": {"tilt": 40}}}, "layout.clock.tilt"),
            ({"layout": {"clock": {"anchor": "bl", "style": "ring"}}}, "layout.clock.style"),
            ({"layout": {"meters": {"style": "gauge"}}}, "layout.meters.style"),
            ({"layout": {"claude": {"style": True}}}, "layout.claude.style"),
        )
        for design, field in cases:
            with self.subTest(design=design):
                r = self.propose(design)
                self.assertEqual(r["status"], "rejected")
                self.assertIn(field, [x["field"] for x in r["reasons"]])
                self.assertTrue(all(x["message"] for x in r["reasons"]))
                self.assertNotIn("request_id", r)
                self.assert_no_request()

    def test_what_only_windows_has_is_refused_with_a_reason_never_dropped(self):
        cases = (
            ({"launcher": {"buttons": []}}, "launcher", "Windows only"),
            ({"messages": {"lines": ["Hello."]}}, "messages", "propose_desk_messages"),
            ({"looks": []}, "looks", "propose_now_playing_looks"),
            ({"theme": {"name": "x"}}, "theme", "propose_theme"),
            ({"layout": {"clock": {"anchor": "bl", "monitor": "main"}}}, "layout.clock", "monitor is Windows only"),
            ({"layout": {"system": {"anchor": "tr"}}}, "layout.system", "not a Desk piece"),
            ({"layout": {"launcher": {"anchor": "tr"}}}, "layout.launcher", "no Launcher"),
            ({"layout": {"glow": {}}}, "layout.glow", "no glow piece"),
            ({"ink": "auto"}, "ink", "Windows ink word"),
            ({"ink": "Theme"}, "ink", "Windows ink word"),
        )
        for design, field, needle in cases:
            with self.subTest(design=design):
                r = self.propose(design)
                self.assertEqual(r["status"], "rejected")
                hit = [x for x in r["reasons"] if x["field"] == field]
                self.assertTrue(hit, r)
                self.assertIn(needle, hit[0]["message"])
                self.assert_no_request()

    def test_two_pieces_of_one_stack_with_different_tilts_are_refused(self):
        r = self.propose({"layout": {"clock": {"anchor": "ml", "tilt": -10}, "claude": {"anchor": "ml", "tilt": 12}}})
        self.assertEqual(r["status"], "rejected")
        self.assertEqual(r["reasons"][0]["field"], "layout.clock.tilt")
        self.assertEqual(r["reasons"][0]["message"], "the pieces at ml share one slant; give clock and claude the same tilt")
        self.assert_no_request()

    def test_a_runaway_design_is_refused_without_a_request_file(self):
        r = mcp.build_propose_desk({"design": {"note": "x" * 500000}}, now=NOW, paths=self.paths)
        self.assertEqual(r["status"], "rejected")
        self.assertIn("128 KB", r["reasons"][0]["message"])
        self.assert_no_request()

    def test_the_calls_name_and_note_win_over_the_designs(self):
        r = self.propose({"name": "Inner", "note": "Inner note.", "plate": True}, extra={"name": "Outer", "note": "Outer note."})
        self.assertEqual(r["status"], "queued")
        design = self.request()["design"]
        self.assertEqual((design["name"], design["note"]), ("Outer", "Outer note."))


class ProposeWeather(Base):
    def test_telemetry_and_weather_pieces_are_accepted(self):
        r = self.propose({"layout": {"telemetry": {"anchor": "tr", "style": "ring"}, "weather": {"anchor": "mr"}}})
        self.assertEqual(r["status"], "queued")
        layout = self.request()["design"]["layout"]
        self.assertEqual(layout["telemetry"]["style"], "ring")
        self.assertEqual(layout["weather"]["anchor"], "mr")

    def test_a_nickname_is_trimmed_and_queued_and_empty_goes_back_to_the_place(self):
        self.propose({"weather": {"nickname": "  Funky Town "}})
        self.assertEqual(self.request()["design"]["weather"]["nickname"], "Funky Town")
        self.propose({"weather": {"nickname": ""}})
        self.assertEqual(self.request()["design"]["weather"]["nickname"], "")

    def test_a_bad_nickname_is_refused_without_a_request(self):
        cases = (
            ({"weather": {"nickname": "Dallas Fort Worth Metroplex Area"}}, "weather.nickname"),
            ({"weather": {"nickname": "abcdefghijklmnopqrstuvwxy"}}, "weather.nickname"),
            ({"weather": {"nickname": "two\nlines"}}, "weather.nickname"),
            ({"weather": {"city": "Paris"}}, "weather"),
            ({"weather": "Funky Town"}, "weather"),
            ({"weather": {}}, "weather.nickname"),
            ({"layout": {"weather": {"style": "slim"}}}, "layout.weather.style"),
        )
        for design, field in cases:
            with self.subTest(design=design):
                r = self.propose(design)
                self.assertEqual(r["status"], "rejected")
                self.assertIn(field, [x["field"] for x in r["reasons"]])
                self.assert_no_request()

    def test_the_guide_names_the_nickname(self):
        propose = [t for t in mcp.TOOLS if t["name"] == "propose_desk"][0]
        self.assertIn("weather {nickname}", propose["description"])
        self.assertIn("nickname", propose["inputSchema"]["properties"]["design"]["properties"]["weather"]["properties"])


class ProposeHandoff(Base):
    def test_a_good_design_is_queued_normalized_and_the_apps_answer_comes_back(self):
        seen = {}

        def answer(req):
            seen.update(req)
            return {"status": "previewing", "changes": ["Ink: white to #ffaa00", "Clock: size 100% to 120%"]}

        r = self.propose({"name": "  Dusk ", "ink": "FA0", "layout": {"clock": {"anchor": "bl", "size": 1.23}}},
                         answer=answer, extra={"note": "Warm and low."})
        self.assertEqual(r["status"], "previewing")
        self.assertRegex(r["request_id"], r"^[0-9a-f]{32}$")
        self.assertEqual(r["changes"][0], "Ink: white to #ffaa00")
        self.assertEqual(seen["schema_version"], 1)
        self.assertEqual(seen["id"], r["request_id"])
        self.assertEqual(seen["requested_at"], "2026-07-26T12:00:00.000Z")
        design = seen["design"]
        self.assertEqual(design["name"], "Dusk")
        self.assertEqual(design["note"], "Warm and low.")
        self.assertEqual(design["ink"], "#ffaa00")
        self.assertEqual(design["layout"]["clock"]["size"], 1.2)
        self.assertNotIn("plate", design)   # the server marks nothing the user did not ask for

    def test_the_request_file_is_owner_only_and_leaves_no_temp_file(self):
        self.propose({"plate": True})
        self.assertEqual(os.stat(self.paths.design_request).st_mode & 0o777, 0o600)
        self.assertEqual(os.listdir(self.support), ["desk-request.json"])

    def test_each_status_the_app_may_give_is_passed_on_with_its_details(self):
        for status in ("applied", "rejected", "needs_font"):
            with self.subTest(status=status):
                r = self.propose({"plate": True}, answer=lambda req, s=status: {
                    "status": s, "reasons": [{"field": "ink", "message": "no"}],
                    "fonts": [{"family": "Caveat", "link": "https://example.com/c"}], "private": "dropped"})
                self.assertEqual(r["status"], status)
                self.assertEqual(r["reasons"][0]["field"], "ink")
                self.assertEqual(r["fonts"][0]["family"], "Caveat")
                self.assertNotIn("private", r)

    def test_a_status_the_server_does_not_know_is_not_passed_on_as_success(self):
        r = self.propose({"plate": True}, answer=lambda req: {"status": "applied-ish"})
        self.assertEqual(r["status"], "rejected")
        self.assertEqual(r["reason"], "unexpected_answer")

    def test_the_not_ready_app_answers_and_the_reason_arrives(self):
        r = self.propose({"plate": True}, answer=lambda req: {
            "status": "rejected", "reason": "not_ready",
            "reasons": [{"field": "design", "message": "not_ready: this version of Sanduhr checks designs but cannot preview them yet"}]})
        self.assertEqual(r["status"], "rejected")
        self.assertEqual(r["reason"], "not_ready")
        self.assertEqual(r["reasons"][0]["field"], "design")

    def test_a_silent_app_leaves_the_request_queued_and_says_so(self):
        r = self.propose({"plate": True})
        self.assertEqual(r["status"], "queued")
        self.assertEqual(r["reason"], "widget_not_responding")
        self.assertRegex(r["request_id"], r"^[0-9a-f]{32}$")
        self.assertIn("ten minutes", r["remedy"])
        self.assertTrue(os.path.exists(self.paths.design_request))

    def test_a_queued_design_that_needs_a_font_still_lists_it(self):
        self.write_state()
        r = self.propose({"font": {"family": "Pacifico", "link": "https://fonts.google.com/specimen/Pacifico"}})
        self.assertEqual(r["status"], "queued")
        self.assertEqual(r["fonts"][0], {"family": "Pacifico", "link": "https://fonts.google.com/specimen/Pacifico"})
        self.assertTrue(os.path.exists(self.paths.design_request))   # valid: the review shows the link and waits

    def test_an_installed_or_bundled_font_is_not_a_need(self):
        self.write_state()
        self.assertNotIn("fonts", self.propose({"font": "caveat"}))
        os.remove(self.paths.design_request)
        self.assertNotIn("fonts", self.propose({"font": "EsteFont 26"}))

    def test_the_apps_own_font_list_fills_in_when_the_app_gave_none(self):
        self.write_state()
        r = self.propose({"font": "Pacifico"}, answer=lambda req: {"status": "needs_font"})
        self.assertEqual(r["status"], "needs_font")
        self.assertEqual(r["fonts"], [{"family": "Pacifico", "link": None}])

    def test_another_requests_answer_is_not_taken(self):
        os.makedirs(self.support, exist_ok=True)
        with open(self.paths.design_result, "w", encoding="utf-8") as f:
            json.dump({"id": "someone-elses-id", "result": {"status": "applied"}}, f)
        self.assertEqual(self.propose({"plate": True})["status"], "queued")

    def test_without_a_state_file_the_font_rule_is_left_to_the_app_but_the_shape_rules_still_hold(self):
        self.assertEqual(self.propose({"font": "Anything", "ink": "#fff"})["status"], "queued")
        os.remove(self.paths.design_request)
        self.assertEqual(self.propose({"ink": "nope"})["status"], "rejected")
        self.assert_no_request()

    def test_two_requests_in_a_row_leave_one_request_file(self):
        self.propose({"plate": True})
        self.propose({"plate": False})
        self.assertEqual(os.listdir(self.support), ["desk-request.json"])
        self.assertIs(self.request()["design"]["plate"], False)

    def test_an_unwritable_folder_is_a_typed_error(self):
        blocked = os.path.join(self.tmp, "file")
        open(blocked, "w").close()
        paths = mcp.Paths(support_dir=os.path.join(blocked, "inside"))
        r = mcp.build_propose_desk({"design": {"plate": True}}, now=NOW, paths=paths, wait=0.0)
        self.assertEqual((r["status"], r["reason"]), ("error", "request_write_failed"))


class Catalog(unittest.TestCase):
    def test_the_two_tools_are_listed_with_closed_shapes(self):
        self.assertIn("get_desk", mcp.TOOL_NAMES)
        self.assertIn("propose_desk", mcp.TOOL_NAMES)
        get = [t for t in mcp.TOOLS if t["name"] == "get_desk"][0]
        self.assertTrue(get["annotations"]["readOnlyHint"])
        self.assertEqual(get["inputSchema"]["properties"], {})
        propose = [t for t in mcp.TOOLS if t["name"] == "propose_desk"][0]
        self.assertFalse(propose["annotations"]["readOnlyHint"])
        self.assertFalse(propose["annotations"]["destructiveHint"])
        self.assertEqual(sorted(propose["inputSchema"]["properties"]), ["design", "name", "note"])
        self.assertEqual(propose["inputSchema"]["required"], ["design"])
        self.assertFalse(propose["inputSchema"]["additionalProperties"])
        self.assertFalse(propose["inputSchema"]["properties"]["design"]["additionalProperties"])
        self.assertIn("tilt: -20 to 20 degrees", json.dumps(propose["inputSchema"]))
        self.assertIn("style: bar|slim|ring|ticks, only on claude, meters and telemetry", json.dumps(propose["inputSchema"]))
        self.assertIn("ticks (ten segmented cells, the OLED-friendly one)", propose["description"])

    def test_call_tool_routes_both(self):
        self.assertEqual(mcp.call_tool("propose_desk", {})["status"], "rejected")


if __name__ == "__main__":
    unittest.main()

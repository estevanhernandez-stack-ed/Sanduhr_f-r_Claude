"""propose_widget (2.36.0): a port of Windows' ToolLogicWidgetTests.cs, plus the shared proposal fixtures
(Tests/SanduhrTests/Fixtures/widget-proposals/) that the Swift WidgetProposal is held to as well. Temp
folders only; the app's answer is played by a fake sleep. Standard library only:

    python3 -m unittest discover -s mac/integrations/tests
"""
import copy
import hashlib
import importlib.util
import json
import os
import re
import shutil
import tempfile
import unittest
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = os.path.join(os.path.dirname(HERE), "sanduhr_mcp.py")
PROPOSALS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Tests", "SanduhrTests", "Fixtures", "widget-proposals")

spec = importlib.util.spec_from_file_location("sanduhr_mcp_widget", SERVER)
mcp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mcp)

# The Windows tests' clock, and the Swift fixture test's (the LICENSE year and the digests follow it).
NOW = datetime(2026, 7, 26, 12, 0, 0, tzinfo=timezone.utc)
FIXTURE_NOW = datetime(2026, 10, 22, 18, 0, 0, tzinfo=timezone.utc)

FRAME = ["..0000..", ".011110.", "01122110", "01122110", "01111110", ".011110.", "..0..0..", ".00..00."]


def sprite_args(edit=None):
    args = {
        "kind": "sprite",
        "pack": {"id": "blob", "name": "Blob", "description": "A small blob."},
        "sprite": {
            "schema_version": 2, "frame_w": 8, "frame_h": 8, "scale": 2, "fps": 8, "sheet": "sheet.json",
            "animations": {"walk": [0, 1], "sit": [0], "nap": [1], "pop": [0], "cheer": [1], "worry": [0],
                           "focus": [1], "sleep": [0], "celebrate": [1]},
            "moods": {"content": "sit", "focused": "focus", "worried": "worry", "exhausted": "sleep", "celebrate": "celebrate"},
            "triggers": {"needs_you": "pop", "job_passed": "cheer", "near_limit": "worry", "idle": "nap"},
            "roam": {"edges": ["bottom"], "speed_px_s": 20},
        },
        "sheets": {"sheet.json": {"palette": ["#22d3ee", "#a855f7", "#ec4899"], "frame_w": 8, "frame_h": 8,
                                  "frames": [list(FRAME), list(FRAME)]}},
        "note": "A small friend.",
    }
    if edit:
        edit(args)
    return args


def messages(r):
    return " | ".join(x["message"] for x in r.get("reasons", []))


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="sanduhr-widget-test-")
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.support = os.path.join(self.tmp, "Sanduhr")
        self.paths = mcp.Paths(support_dir=self.support)

    def propose(self, args, answer=None, wait=1.0):
        """build_propose_widget with a fake clock; answer(request) is what the app writes back, once."""
        ticks = [0.0]

        def clock():
            return ticks[0]

        def sleep(seconds):
            ticks[0] += seconds
            if answer is None or not os.path.exists(self.paths.widget_request):
                return
            with open(self.paths.widget_request, encoding="utf-8") as f:
                req = json.load(f)
            os.makedirs(self.support, exist_ok=True)
            with open(self.paths.widget_result, "w", encoding="utf-8") as f:
                json.dump({"id": req["id"], "completed_at": "2026-07-26T12:00:00.100Z", "result": answer(req)}, f)

        return mcp.build_propose_widget(args, now=NOW, paths=self.paths, wait=wait, poll=0.25, sleep=sleep, clock=clock)

    def request(self):
        with open(self.paths.widget_request, encoding="utf-8") as f:
            return json.load(f)


class ProposeWidget(Base):
    def test_the_handoff_names_and_limits_match_the_app(self):
        self.assertEqual((mcp.WIDGET_REQUEST_FILE, mcp.WIDGET_RESULT_FILE), ("widget-request.json", "widget-result.json"))
        self.assertEqual(mcp.WIDGET_MAX_BYTES, 4 * 1024 * 1024)
        self.assertEqual(mcp.WIDGET_SANDUHR_MIN, "3.8.0")
        self.assertEqual(mcp.WIDGET_KINDS, ("sprite", "palette", "effects"))
        self.assertEqual(mcp.WIDGET_LICENSES, ("CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause", "ISC"))
        self.assertEqual(mcp.WIDGET_STATUSES, ("previewing", "applied", "rejected"))
        self.assertEqual(self.paths.widget_request, os.path.join(self.support, "widget-request.json"))
        self.assertEqual(self.paths.widget_result, os.path.join(self.support, "widget-result.json"))

    def test_a_pack_that_breaks_a_rule_is_rejected_with_reasons_and_nothing_is_written(self):
        def set_name(a): a["pack"]["name"] = "Claude blob"
        def png_sheet(a): a["sprite"]["sheet"] = "sprite.png"
        def white(a): a["sheets"]["sheet.json"]["palette"][0] = "#ffffff"
        def theme(a): a["kind"] = "theme"
        def font(a): a["kind"] = "font"
        def png_arg(a): a["png"] = "AAAA"
        for edit, needle in ((set_name, "trademark"), (png_sheet, "never supplies an image"), (white, "pure white"),
                             (theme, "propose_theme"), (font, "fonts are binaries"), (png_arg, "unknown argument")):
            with self.subTest(needle=needle):
                r = self.propose(sprite_args(edit))
                self.assertEqual(r["status"], "rejected")
                self.assertIn(needle, messages(r))
        self.assertFalse(os.path.exists(self.paths.widget_request))
        self.assertFalse(os.path.exists(self.support))   # not even the folder

    def test_a_good_pack_is_queued_namespaced_and_the_apps_answer_comes_back(self):
        seen = {}

        def answer(req):
            seen.update(req)
            return {"status": "previewing", "pack_id": "my-blob"}

        r = self.propose(sprite_args(), answer=answer)
        self.assertEqual(r["status"], "previewing")
        self.assertEqual(r["pack_id"], "my-blob")
        self.assertRegex(r["request_id"], r"^[0-9a-f]{32}$")
        self.assertEqual(r["request_id"], seen["id"])
        self.assertEqual(seen["schema_version"], 1)
        self.assertEqual(seen["pack_id"], "my-blob")
        self.assertEqual(seen["note"], "A small friend.")
        self.assertEqual(seen["requested_at"], "2026-07-26T12:00:00.000Z")
        self.assertEqual(list(seen), ["schema_version", "id", "requested_at", "pack_id", "note", "files"])
        self.assertEqual(sorted(seen["files"]), ["LICENSE", "pack.json", "sheet.json", "sprite.json"])
        self.assertTrue(all(isinstance(v, str) for v in seen["files"].values()))   # text only: data, never bytes
        self.assertEqual(os.stat(self.paths.widget_request).st_mode & 0o777, 0o600)
        self.assertFalse(os.path.exists(self.paths.widget_request + ".tmp"))

    def test_the_pack_json_is_generated_not_given(self):
        r = self.propose(sprite_args())
        self.assertEqual(r["status"], "queued")
        files = self.request()["files"]
        manifest = json.loads(files["pack.json"])
        self.assertEqual(list(manifest), ["schema_version", "id", "name", "version", "author", "license", "kind",
                                          "sanduhr_min", "description", "files"])
        self.assertEqual((manifest["id"], manifest["version"], manifest["author"], manifest["license"], manifest["kind"],
                          manifest["sanduhr_min"]), ("my-blob", "1.0.0", "Made here", "CC0-1.0", "sprite", "3.8.0"))
        self.assertEqual(manifest["files"], ["sheet.json", "sprite.json"])
        self.assertTrue(files["pack.json"].endswith("}\n"))
        self.assertIn("\n  \"id\": \"my-blob\",\n", files["pack.json"])   # two-space indent
        self.assertTrue(files["LICENSE"].startswith("CC0 1.0 Universal\n\nCopyright (c) 2026 Made here\n"))

    def test_an_answer_outside_the_apps_statuses_is_passed_on_as_rejected(self):
        r = self.propose(sprite_args(), answer=lambda req: {"status": "installed_without_asking"})
        self.assertEqual((r["status"], r["reason"]), ("rejected", "unexpected_answer"))
        r = self.propose(sprite_args(), answer=lambda req: {"status": 7})
        self.assertEqual((r["status"], r["reason"]), ("rejected", "unexpected_answer"))
        r = self.propose(sprite_args(), answer=lambda req: {})
        self.assertEqual((r["status"], r["reason"]), ("rejected", "unexpected_answer"))

    def test_the_apps_reasons_companion_and_status_pass_through_and_nothing_else(self):
        r = self.propose(sprite_args(), answer=lambda req: {"status": "applied", "companion": True, "pack_id": "my-other",
                                                            "secret": "x"})
        self.assertEqual(r, {"status": "applied", "companion": True, "pack_id": "my-blob", "request_id": r["request_id"]})
        reasons = [{"field": "widget", "message": "dismissed: the user did not apply it"}]
        r = self.propose(sprite_args(), answer=lambda req: {"status": "rejected", "reason": "dismissed", "reasons": reasons})
        self.assertEqual((r["status"], r["reason"], r["reasons"]), ("rejected", "dismissed", reasons))
        r = self.propose(sprite_args(), answer=lambda req: {"status": "rejected", "reasons": reasons})
        self.assertNotIn("reason", r)   # reasons alone is an answer; unexpected_answer is only for none at all

    def test_an_answer_for_another_request_is_not_this_ones(self):
        os.makedirs(self.support)
        with open(self.paths.widget_result, "w", encoding="utf-8") as f:
            json.dump({"id": "0" * 32, "result": {"status": "applied"}}, f)
        r = self.propose(sprite_args())
        self.assertEqual((r["status"], r["reason"]), ("queued", "widget_not_responding"))

    def test_with_no_app_answering_the_request_waits_as_queued(self):
        r = self.propose(sprite_args())
        self.assertEqual((r["status"], r["reason"], r["pack_id"]), ("queued", "widget_not_responding", "my-blob"))
        self.assertRegex(r["request_id"], r"^[0-9a-f]{32}$")
        self.assertIn("did not answer within 1 seconds", r["remedy"])
        self.assertIn("Start Sanduhr", r["remedy"])
        self.assertTrue(os.path.exists(self.paths.widget_request))   # it stays for the app's next look
        self.assertEqual(self.request()["id"], r["request_id"])

    def test_a_request_over_four_megabytes_is_refused_and_not_written(self):
        old = mcp.WIDGET_MAX_BYTES
        mcp.WIDGET_MAX_BYTES = 1000
        try:
            r = self.propose(sprite_args())
        finally:
            mcp.WIDGET_MAX_BYTES = old
        self.assertEqual(r["status"], "rejected")
        self.assertEqual(r["reasons"][0]["field"], "pack")
        self.assertIn("too large", r["reasons"][0]["message"])
        self.assertFalse(os.path.exists(self.paths.widget_request))

    def test_an_unwritable_folder_is_a_typed_error(self):
        os.makedirs(self.tmp, exist_ok=True)
        blocker = os.path.join(self.tmp, "file")
        open(blocker, "w").close()
        self.paths = mcp.Paths(support_dir=os.path.join(blocker, "Sanduhr"))
        r = self.propose(sprite_args())
        self.assertEqual((r["status"], r["reason"]), ("error", "request_write_failed"))

    def test_a_copy_without_the_validator_says_so_and_writes_nothing(self):
        real = mcp.widget_validator

        def missing():
            raise mcp.WidgetValidatorMissing("FileNotFoundError")
        mcp.widget_validator = missing
        try:
            r = self.propose(sprite_args())
        finally:
            mcp.widget_validator = real
        self.assertEqual((r["status"], r["reason"]), ("error", "validator_missing"))
        self.assertFalse(os.path.exists(self.paths.widget_request))

    def test_the_server_is_called_through_the_protocol_like_the_other_tools(self):
        os.environ["SANDUHR_SUPPORT_DIR"] = self.support
        try:
            r = mcp.call_tool("propose_widget", {"kind": "font", "pack": {"id": "x", "name": "x", "description": "x"}})
        finally:
            del os.environ["SANDUHR_SUPPORT_DIR"]
        self.assertEqual(r["status"], "rejected")
        self.assertIn("propose_widget", mcp.TOOL_NAMES)

    def test_the_tool_has_windows_schema_and_guide(self):
        tool = [t for t in mcp.TOOLS if t["name"] == "propose_widget"][0]
        self.assertEqual(sorted(tool["inputSchema"]["properties"]), ["effects", "kind", "note", "pack", "palettes", "sheets", "sprite"])
        self.assertEqual(tool["inputSchema"]["required"], ["kind", "pack"])
        self.assertFalse(tool["inputSchema"]["additionalProperties"])
        self.assertEqual(tool["inputSchema"]["properties"]["kind"]["enum"], ["sprite", "palette", "effects"])
        pack = tool["inputSchema"]["properties"]["pack"]
        self.assertEqual(sorted(pack["properties"]), ["author", "description", "id", "license", "name", "version"])
        self.assertEqual(pack["required"], ["id", "name", "description"])
        self.assertFalse(pack["additionalProperties"])
        self.assertEqual(pack["properties"]["license"]["enum"], list(mcp.WIDGET_LICENSES))
        self.assertEqual(tool["inputSchema"]["properties"]["note"]["maxLength"], 200)
        self.assertFalse(tool["annotations"]["readOnlyHint"])
        self.assertFalse(tool["annotations"]["destructiveHint"])
        self.assertFalse(tool["annotations"]["openWorldHint"])
        self.assertTrue(tool["description"].isascii())
        self.assertIn("a new pack always waits for Apply", tool["description"])
        self.assertIn("no 'opus', 'sonnet', 'haiku' or 'fable' as words", tool["description"])

    def test_the_guide_is_windows_word_for_word(self):
        cs = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(HERE))), "windows-dotnet", "src", "Sanduhr.Mcp", "ToolCatalog.cs")
        if not os.path.exists(cs):
            self.skipTest("windows-dotnet is not in this checkout")
        with open(cs, encoding="utf-8") as f:
            source = f.read()
        body = re.search(r"private const string WidgetGuide =\n(.*?);\n", source, re.S).group(1)
        windows = "".join(re.findall(r'^\s*"(.*)"\s*\+?\s*$', body, re.M))
        # The one platform word: Windows 4.0 files widgets under Mods & Widgets, Installed; the Mac page is Widgets.
        windows = windows.replace("Settings, Mods & Widgets, Installed", "Settings, Widgets")
        self.assertEqual(mcp.WIDGET_GUIDE, windows)


class ValidatorLayout(Base):
    """propose_widget finds widgets/validate.py beside the script's real path, in every layout the app
    and install.sh make: the folder itself, and a link to it (the flat copy and `current`)."""

    def test_the_validator_is_found_beside_the_script_and_through_a_link(self):
        src = os.path.dirname(SERVER)
        for layout in ("copy", "link"):
            with self.subTest(layout=layout):
                dest = os.path.join(self.tmp, layout)
                os.makedirs(os.path.join(dest, "widgets"))
                shutil.copy(SERVER, os.path.join(dest, "stamp_mcp.py"))
                shutil.copy(os.path.join(src, "widgets", "validate.py"), os.path.join(dest, "widgets", "validate.py"))
                script = os.path.join(dest, "stamp_mcp.py")
                if layout == "link":
                    flat = os.path.join(self.tmp, "flat")
                    os.makedirs(flat)
                    os.symlink(script, os.path.join(flat, "sanduhr_mcp.py"))
                    script = os.path.join(flat, "sanduhr_mcp.py")
                spec2 = importlib.util.spec_from_file_location("sanduhr_mcp_layout_" + layout, script)
                module = importlib.util.module_from_spec(spec2)
                spec2.loader.exec_module(module)
                self.assertTrue(hasattr(module.widget_validator(), "validate_pack"))

    def test_a_copy_without_widgets_answers_validator_missing(self):
        dest = os.path.join(self.tmp, "bare")
        os.makedirs(dest)
        shutil.copy(SERVER, os.path.join(dest, "sanduhr_mcp.py"))
        spec2 = importlib.util.spec_from_file_location("sanduhr_mcp_bare", os.path.join(dest, "sanduhr_mcp.py"))
        module = importlib.util.module_from_spec(spec2)
        spec2.loader.exec_module(module)
        r = module.build_propose_widget(sprite_args(), now=NOW, paths=module.Paths(support_dir=self.support))
        self.assertEqual((r["status"], r["reason"]), ("error", "validator_missing"))
        self.assertFalse(os.path.exists(self.support))

    def test_install_sh_and_build_sh_ship_it(self):
        root = os.path.dirname(os.path.dirname(HERE))
        with open(os.path.join(root, "integrations", "install.sh"), encoding="utf-8") as f:
            self.assertIn('"$DEST/widgets/validate.py"', f.read())
        with open(os.path.join(root, "build.sh"), encoding="utf-8") as f:
            self.assertIn("integrations/widgets/validate.py", f.read())


class BuildRules(Base):
    """WidgetProposal.Build, rule for rule (the Swift WidgetProposalTests are the other half)."""

    def build(self, args):
        return mcp.build_widget(args, NOW)

    def fields(self, r):
        return [x["field"] for x in r["reasons"]]

    def test_defaults_and_the_id_prefix(self):
        r = self.build(sprite_args())
        self.assertIsNotNone(r["pack"], r["reasons"])
        self.assertEqual((r["pack"]["id"], r["pack"]["kind"], r["pack"]["name"]), ("my-blob", "sprite", "Blob"))
        again = self.build(sprite_args(lambda a: a["pack"].update(id="my-blob")))
        self.assertEqual(again["pack"]["id"], "my-blob")   # prefixed once

    def test_every_license_writes_a_license_the_validator_accepts(self):
        for lic in mcp.WIDGET_LICENSES:
            with self.subTest(license=lic):
                r = self.build(sprite_args(lambda a: a["pack"].update(license=lic, author="Sam Lee")))
                self.assertIsNotNone(r["pack"], r["reasons"])
                self.assertIn("Copyright (c) 2026 Sam Lee", r["pack"]["files"]["LICENSE"].decode())
                self.assertEqual(json.loads(r["pack"]["files"]["pack.json"])["license"], lic)

    def test_the_kind_rules(self):
        self.assertEqual(self.fields(self.build({})), ["kind", "pack"])
        self.assertEqual(self.fields(self.build(sprite_args(lambda a: a.update(kind=7)))), ["kind"])
        self.assertEqual(self.fields(self.build(sprite_args(lambda a: a.update(kind="sprites")))), ["kind"])
        self.assertIn("propose_theme", messages(self.build(sprite_args(lambda a: a.update(kind="theme")))))
        self.assertIn("fonts are binaries", messages(self.build(sprite_args(lambda a: a.update(kind="font")))))

    def test_the_pack_object_rules(self):
        r = self.build(sprite_args(lambda a: a.update(pack={"id": "x"})))
        self.assertEqual(self.fields(r), ["pack.name", "pack.description"])
        r = self.build(sprite_args(lambda a: a["pack"].update(files=["x"], kind="sprite", sanduhr_min="1.0.0")))
        self.assertEqual(self.fields(r), ["pack"])
        self.assertIn("unknown key(s): files, kind, sanduhr_min", messages(r))
        r = self.build(sprite_args(lambda a: a.update(pack="blob")))
        self.assertEqual(self.fields(r), ["pack"])
        r = self.build(sprite_args(lambda a: a["pack"].update(version=1, author=2, license="GPL-3.0")))
        self.assertEqual(self.fields(r), ["pack.version", "pack.author", "pack.license"])
        self.assertEqual(self.fields(self.build(sprite_args(lambda a: a["pack"].update(id=None)))), ["pack.id"])

    def test_the_note_rules(self):
        for note in ("x" * 201, "two\nlines", 5):
            with self.subTest(note=repr(note)[:20]):
                self.assertEqual(self.fields(self.build(sprite_args(lambda a: a.update(note=note)))), ["note"])
        self.assertIsNotNone(self.build(sprite_args(lambda a: a.update(note="x" * 200)))["pack"])
        self.assertIsNotNone(self.build(sprite_args(lambda a: a.update(note=None)))["pack"])   # null is no note

    def test_files_of_another_kind_do_not_belong(self):
        r = self.build(sprite_args(lambda a: a.update(palettes=[], effects=[])))
        self.assertEqual(self.fields(r), ["palettes", "effects"])
        self.assertIn("does not belong to a sprite pack", messages(r))

    def test_sheet_rules(self):
        def many(a): a["sheets"] = {"s%d.json" % i: a["sheets"]["sheet.json"] for i in range(7)}
        self.assertIn("at most 6 pixel sheets", messages(self.build(sprite_args(many))))
        self.assertIn("must be an object of pixel sheets", messages(self.build(sprite_args(lambda a: a.update(sheets={})))))
        for bad in ("sheet.png", "../x.json", "sprite.json", "a b.json"):
            with self.subTest(path=bad):
                r = self.build(sprite_args(lambda a: a["sheets"].update({bad: a["sheets"]["sheet.json"]})))
                self.assertIn("a sheet is a .json pixel sheet with a safe relative path", messages(r))
        r = self.build(sprite_args(lambda a: a["sheets"].update({"other.json": 5})))
        self.assertIn("must be an object {palette, frame_w, frame_h, frames}", messages(r))

    def test_a_nested_sheet_path_and_stages_build(self):
        def stages(a):
            sheet = a["sheets"].pop("sheet.json")
            a["sheets"]["sprites/egg.json"] = sheet
            a["sprite"].pop("sheet")
            a["sprite"]["stages"] = [{"id": "egg", "name": "Egg", "min_growth": 0, "sheet": "sprites/egg.json", "frame_w": 8, "frame_h": 8}]
        r = self.build(sprite_args(stages))
        self.assertIsNotNone(r["pack"], r["reasons"])
        self.assertEqual(sorted(r["pack"]["files"]), ["LICENSE", "pack.json", "sprite.json", "sprites/egg.json"])

    def test_palettes_and_effects_take_the_list_alone_or_the_whole_file(self):
        base = {"pack": {"id": "dusk", "name": "Dusk", "description": "Gradients."}}
        palettes = [{"name": "dusk", "colors": ["#ff2a6d", "#05d9e8"]}]
        r = self.build(dict(base, kind="palette", palettes=palettes))
        self.assertEqual(json.loads(r["pack"]["files"]["palettes.json"]), {"schema_version": 1, "palettes": palettes})
        r = self.build(dict(base, kind="palette", palettes={"palettes": palettes}))
        self.assertEqual(json.loads(r["pack"]["files"]["palettes.json"]), {"palettes": palettes, "schema_version": 1})
        r = self.build(dict(base, kind="palette"))
        self.assertEqual(self.fields(r), ["palettes"])
        self.assertIn("must be palettes.json's object", messages(r))
        r = self.build(dict(base, kind="effects", effects="x"))
        self.assertEqual(self.fields(r), ["effects"])

    def test_the_trademark_rule_reaches_the_arts_json_too(self):
        r = self.build(sprite_args(lambda a: a["sprite"]["animations"].update(sonnet_dance=[0])))
        self.assertIsNone(r["pack"])
        self.assertIn("trademark", messages(r))

    def test_a_pixel_sheet_error_is_the_validators_sentence(self):
        r = self.build(sprite_args(lambda a: a["sheets"]["sheet.json"]["frames"][0].pop()))
        self.assertEqual(self.fields(r), ["pack"])
        self.assertIn("sheet.json:", messages(r))

    def test_the_json_writer_escapes_as_the_default_encoder_does(self):
        self.assertEqual(mcp.widget_json({"a": "Café <b> & 'q' + `t` \"x\" \\ \t\u007f \U0001F600"}),
                         '{"a":"Caf\\u00E9 \\u003Cb\\u003E \\u0026 \\u0027q\\u0027 \\u002B \\u0060t\\u0060 \\u0022x\\u0022 \\\\ \\t\\u007F \\uD83D\\uDE00"}')
        self.assertEqual(mcp.widget_json({"a": [], "b": {}, "c": [1, {"d": None}], "e": True}, "  "),
                         '{\n  "a": [],\n  "b": {},\n  "c": [\n    1,\n    {\n      "d": null\n    }\n  ],\n  "e": true\n}')
        with self.assertRaises(ValueError):
            mcp.widget_json(float("inf"))

    def test_a_lone_surrogate_becomes_the_replacement_character(self):
        self.assertEqual(mcp.widget_utf8("a\ud800b"), "a�b".encode("utf-8"))

    def test_clip_counts_utf16_units_like_the_csharp(self):
        self.assertEqual(mcp.clip16("abcdef", 4), "abcd...")
        self.assertEqual(mcp.clip16("abcd", 4), "abcd")
        self.assertEqual(mcp.u16len("a\U0001F600"), 3)

    def test_clashing_file_paths_are_refused_not_crashed(self):
        r = self.build(sprite_args(lambda a: a["sheets"].update({"a.json": a["sheets"]["sheet.json"],
                                                                 "a.json/b.json": a["sheets"]["sheet.json"]})))
        self.assertIsNone(r["pack"])
        self.assertEqual(self.fields(r)[0], "files")


class SharedFixtures(Base):
    """Tests/SanduhrTests/Fixtures/widget-proposals/: the arguments and expected.json that
    WidgetProposalFixtureTests.swift runs build over too."""

    def expected(self):
        with open(os.path.join(PROPOSALS, "expected.json"), encoding="utf-8") as f:
            return json.load(f)

    def test_the_corpus_is_all_there(self):
        names = sorted(n for n in os.listdir(PROPOSALS) if n != "expected.json")
        self.assertEqual(names, sorted(self.expected()))
        self.assertGreaterEqual(len(names), 12)

    def test_every_proposal_is_built_or_refused_as_expected(self):
        for name, want in sorted(self.expected().items()):
            with self.subTest(proposal=name):
                with open(os.path.join(PROPOSALS, name), encoding="utf-8") as f:
                    args = json.load(f)
                r = mcp.build_widget(args, FIXTURE_NOW)
                self.assertEqual(r["pack"] is not None, want["ok"], r["reasons"])
                self.assertEqual([x["field"] for x in r["reasons"]], want["reasons"])
                if want["ok"]:
                    self.assertEqual(r["pack"]["id"], want["id"])
                    digests = {p: hashlib.sha256(b).hexdigest() for p, b in r["pack"]["files"].items()}
                    self.assertEqual(digests, want["sha256"])


if __name__ == "__main__":
    unittest.main()

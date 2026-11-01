"""propose_sprite_motion (2.39.0): a made-here sprite's motion, mapped from its frame size and cell count alone.
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

spec = importlib.util.spec_from_file_location("sanduhr_mcp_motion", SERVER)
mcp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mcp)

NOW = datetime(2026, 10, 30, 12, 0, 0, tzinfo=timezone.utc)

SPRITES = [
    {"id": "my-blob", "name": "Blob", "frame_w": 16, "frame_h": 16, "cells": 6},
    {"id": "my-dot", "name": "Dot", "frame_w": 8, "frame_h": 8, "cells": 1},
]


def motion_args(edit=None):
    args = {
        "sprite_id": "my-blob",
        "animations": {"walk": [0, 1], "sit": [2], "nap": [3], "pop": [4], "cheer": [5], "worry": [2],
                       "focus": [2], "sleep": [3], "celebrate": [5]},
        "moods": {"content": "sit", "focused": "focus", "worried": "worry", "exhausted": "sleep", "celebrate": "celebrate"},
        "triggers": {"needs_you": "pop", "job_passed": "cheer", "near_limit": "worry", "idle": "nap"},
        "fps": 10,
        "note": "Slower nap.",
    }
    if edit:
        edit(args)
    return args


def messages(r):
    return " | ".join(x["message"] for x in r.get("reasons", []))


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="sanduhr-motion-test-")
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.support = os.path.join(self.tmp, "Sanduhr")
        self.paths = mcp.Paths(support_dir=self.support)

    def write_state(self, sprites=SPRITES):
        os.makedirs(self.support, exist_ok=True)
        with open(self.paths.design_state, "w", encoding="utf-8") as f:
            json.dump({"schema_version": 1, "writer_version": "2.39.0", "sprites": sprites}, f)

    def propose(self, args, answer=None, wait=1.0):
        ticks = [0.0]

        def clock():
            return ticks[0]

        def sleep(seconds):
            ticks[0] += seconds
            if answer is None or not os.path.exists(self.paths.motion_request):
                return
            with open(self.paths.motion_request, encoding="utf-8") as f:
                req = json.load(f)
            with open(self.paths.motion_result, "w", encoding="utf-8") as f:
                json.dump({"id": req["id"], "completed_at": "2026-10-30T12:00:00.100Z", "result": answer(req)}, f)

        return mcp.build_propose_sprite_motion(args, now=NOW, paths=self.paths, wait=wait, poll=0.25, sleep=sleep, clock=clock)


class ProposeSpriteMotion(Base):
    def test_the_handoff_names_match_the_app(self):
        self.assertEqual((mcp.MOTION_REQUEST_FILE, mcp.MOTION_RESULT_FILE),
                         ("sprite-motion-request.json", "sprite-motion-result.json"))
        self.assertEqual(mcp.MOTION_MAX_BYTES, 256 * 1024)
        self.assertEqual(mcp.MOTION_FIELDS, ("animations", "fps", "scale", "moods", "triggers", "roam"))

    def test_the_tool_is_listed_with_a_closed_shape_and_no_image_field(self):
        tool = next(t for t in mcp.TOOLS if t["name"] == "propose_sprite_motion")
        props = tool["inputSchema"]["properties"]
        self.assertEqual(sorted(props), ["animations", "fps", "moods", "note", "roam", "scale", "sprite_id", "triggers"])
        self.assertFalse(tool["inputSchema"]["additionalProperties"])
        self.assertFalse(tool["annotations"]["readOnlyHint"])
        self.assertFalse(tool["annotations"]["destructiveHint"])
        for banned in ("image", "pixels", "sheet", "stages", "files", "png"):
            self.assertNotIn(banned, props)

    def test_image_pixel_and_other_foreign_fields_are_refused_and_nothing_is_written(self):
        self.write_state()
        for key in ("image", "png", "pixels", "sheet", "sheets", "stages", "files", "frame_w", "name", "pack"):
            with self.subTest(key=key):
                r = self.propose(motion_args(lambda a: a.update({key: "AAAA"})))
                self.assertEqual(r["status"], "rejected")
                self.assertIn("unknown argument", messages(r))
                self.assertIn("never supplies or sees an image", messages(r))
        self.assertFalse(os.path.exists(self.paths.motion_request))

    def test_unknown_sprite_ids_and_catalog_sprites_are_refused(self):
        self.write_state()
        for sid in ("my-nope", "hourglass", "", "../etc"):
            with self.subTest(sid=sid):
                r = self.propose(motion_args(lambda a: a.update({"sprite_id": sid})))
                self.assertEqual(r["status"], "rejected")
                self.assertIn("not a sprite made here", messages(r))
        r = self.propose(motion_args(lambda a: a.pop("sprite_id")))
        self.assertEqual(r["status"], "rejected")
        self.assertIn("must be the id of a sprite made here", messages(r))
        self.assertFalse(os.path.exists(self.paths.motion_request))

    def test_no_state_file_is_no_data_not_a_guess(self):
        r = self.propose(motion_args())
        self.assertEqual((r["status"], r["reason"]), ("no_data", "missing"))
        self.assertFalse(os.path.exists(self.paths.motion_request))

    def test_out_of_range_cell_indexes_are_refused_against_the_sprites_cell_count(self):
        self.write_state()
        r = self.propose(motion_args(lambda a: a["animations"].update({"walk": [0, 6]})))
        self.assertEqual(r["status"], "rejected")
        self.assertIn("cell 6", messages(r))
        self.assertIn("6 cells", messages(r))
        # The one-cell sprite has only cell 0: this table (cells up to 5) cannot apply to it.
        r = self.propose(motion_args(lambda a: a.update({"sprite_id": "my-dot"})))
        self.assertEqual(r["status"], "rejected")
        self.assertIn("1 cell", messages(r))
        self.assertFalse(os.path.exists(self.paths.motion_request))

    def test_the_validators_sprite_rules_apply(self):
        self.write_state()
        cases = (
            (lambda a: a.update({"fps": 99}), "fps"),
            (lambda a: a.update({"scale": 9}), "scale"),
            (lambda a: a["animations"].pop("sit"), "required animation 'sit'"),
            (lambda a: a["moods"].update({"content": "missing"}), "not defined"),
            (lambda a: a.update({"roam": {"edges": ["top"], "speed_px_s": 30}}), "roam.edges"),
            (lambda a: a["animations"].update({"walk": [-1, 0]}), "non-negative"),
            (lambda a: a["animations"].update({"claude_walk": [0]}), "trademark"),
            (lambda a: a.update({"extra_table": {}}), "unknown argument"),
        )
        for edit, needle in cases:
            with self.subTest(needle=needle):
                r = self.propose(motion_args(edit))
                self.assertEqual(r["status"], "rejected")
                self.assertIn(needle, messages(r))
        self.assertFalse(os.path.exists(self.paths.motion_request))

    def test_the_three_tables_are_required(self):
        self.write_state()
        for key in ("animations", "moods", "triggers"):
            with self.subTest(key=key):
                r = self.propose(motion_args(lambda a: a.pop(key)))
                self.assertEqual(r["status"], "rejected")
                self.assertIn("is required", messages(r))
                self.assertEqual(r["reasons"][0]["field"], key)

    def test_a_good_proposal_writes_the_request_and_the_apps_answer_comes_back(self):
        self.write_state()
        seen = {}

        def answer(req):
            seen.update(req)
            return {"status": "previewing", "pack_id": "my-blob"}

        r = self.propose(motion_args(), answer=answer)
        self.assertEqual(r["status"], "previewing")
        self.assertEqual(r["pack_id"], "my-blob")
        self.assertRegex(r["request_id"], r"^[0-9a-f]{32}$")
        self.assertEqual(r["request_id"], seen["id"])
        self.assertEqual(list(seen), ["schema_version", "id", "requested_at", "sprite_id", "note", "sprite"])
        self.assertEqual(seen["schema_version"], 1)
        self.assertEqual(seen["requested_at"], "2026-10-30T12:00:00.000Z")
        self.assertEqual(seen["sprite_id"], "my-blob")
        self.assertEqual(seen["note"], "Slower nap.")
        self.assertEqual(sorted(seen["sprite"]), ["animations", "fps", "moods", "triggers"])
        self.assertEqual(seen["sprite"]["fps"], 10)
        self.assertNotIn("scale", seen["sprite"])   # left out stays as it is
        self.assertEqual(os.stat(self.paths.motion_request).st_mode & 0o777, 0o600)
        self.assertFalse(os.path.exists(self.paths.motion_request + ".tmp"))

    def test_the_apps_statuses_pass_through_and_an_odd_one_is_rejected(self):
        self.write_state()
        for status in ("applied", "rejected"):
            with self.subTest(status=status):
                r = self.propose(motion_args(), answer=lambda req, s=status: {"status": s, "reason": "dismissed"})
                self.assertEqual(r["status"], status)
        r = self.propose(motion_args(), answer=lambda req: {"status": "needs_font"})
        self.assertEqual((r["status"], r["reason"]), ("rejected", "unexpected_answer"))

    def test_no_answer_is_queued_and_the_request_stays(self):
        self.write_state()
        r = self.propose(motion_args())
        self.assertEqual(r["status"], "queued")
        self.assertEqual(r["reason"], "widget_not_responding")
        self.assertTrue(os.path.exists(self.paths.motion_request))

    def test_get_desk_returns_the_sprites_and_nothing_pixel_shaped(self):
        self.write_state()
        r = mcp.build_get_desk(now=NOW, paths=self.paths)
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["sprites"], SPRITES)
        for row in r["sprites"]:
            self.assertEqual(sorted(row), ["cells", "frame_h", "frame_w", "id", "name"])


if __name__ == "__main__":
    unittest.main()

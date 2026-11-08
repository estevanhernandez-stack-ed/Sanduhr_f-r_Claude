"""The vendored widget pack validator (mac/integrations/widgets/validate.py, from sanduhr-widgets at
d68a788) run on the Mac fixture corpus: every finding must match python-expected.tsv, the file the
Swift PackValidator tests are held to; and its zip mode, which must refuse a zip in the same words as
WidgetZip.swift. Standard library only:

    python3 -m unittest discover -s mac/integrations/tests
"""
import importlib.util
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
INTEGRATIONS = os.path.dirname(HERE)
VALIDATE = os.path.join(INTEGRATIONS, "widgets", "validate.py")
FIXTURES = os.path.join(os.path.dirname(INTEGRATIONS), "Tests", "SanduhrTests", "Fixtures", "widget-packs")

spec = importlib.util.spec_from_file_location("widget_validate", VALIDATE)
validate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validate)

GROUPS = ("good", "bad", "packs")


def pack_keys():
    keys = []
    for group in GROUPS:
        for name in sorted(os.listdir(os.path.join(FIXTURES, group))):
            if os.path.isdir(os.path.join(FIXTURES, group, name)):
                keys.append((group, name))
    return keys


def expected_rows():
    rows = {}
    with open(os.path.join(FIXTURES, "python-expected.tsv"), encoding="utf-8", newline="") as f:
        for line in f.read().split("\n"):
            if not line:
                continue
            key, level, message = line.split("\t", 2)
            rows.setdefault(key, []).append((level, message))
    return rows


def findings(group, name):
    rep = validate.validate_pack(os.path.join(FIXTURES, group, name))
    return [("ERROR", m) for m in rep.errors] + [("WARN", m) for m in rep.warnings]


class FixtureCorpus(unittest.TestCase):
    def test_the_corpus_is_all_there(self):
        keys = pack_keys()
        self.assertEqual(len([k for k in keys if k[0] == "good"]), 8)
        self.assertEqual(len([k for k in keys if k[0] == "bad"]), 42)
        self.assertEqual([k[1] for k in keys if k[0] == "packs"],
                         ["hourglass", "oled-neon", "oled-neon-effects", "oled-neon-palettes"])

    def test_every_pack_matches_python_expected(self):
        expected = expected_rows()
        for group, name in pack_keys():
            with self.subTest(pack=f"{group}/{name}"):
                self.assertEqual(findings(group, name), expected.get(f"{group}/{name}", []))

    def test_expected_names_no_pack_that_is_missing(self):
        have = {f"{g}/{n}" for g, n in pack_keys()}
        self.assertLessEqual(set(expected_rows()), have)

    def test_good_and_first_party_packs_are_strict_clean(self):
        for group, name in pack_keys():
            if group != "bad":
                with self.subTest(pack=f"{group}/{name}"):
                    self.assertEqual(findings(group, name), [])


if __name__ == "__main__":
    unittest.main()


class ZipModeMatchesTheAppsReader(unittest.TestCase):
    """validate.py on a .zip says what WidgetZip.swift says (Sources/Sanduhr/Widgets/WidgetZip.swift).
    The smoke fixtures are zips the app installs (good-sprite) or refuses as a pack (bad-no-license)."""

    SMOKE = os.path.join(os.path.dirname(INTEGRATIONS), "smoke", "fixtures", "widget-packs")
    SWIFT = os.path.join(os.path.dirname(INTEGRATIONS), "Sources", "Sanduhr", "Widgets", "WidgetZip.swift")

    def test_the_smoke_zips_read_as_the_app_reads_them(self):
        self.assertEqual(validate.validate_zip(os.path.join(self.SMOKE, "good-sprite.zip")).errors, [])
        errs = validate.validate_zip(os.path.join(self.SMOKE, "bad-no-license.zip")).errors
        self.assertTrue(errs and not any(e.startswith("zip:") for e in errs), errs)
        self.assertTrue(any("LICENSE is missing" in e for e in errs), errs)

    def test_every_refusal_sentence_is_one_the_swift_reader_has(self):
        # The fixed parts of each Python message must appear in WidgetZip.swift, so the two can't drift apart.
        with open(VALIDATE, encoding="utf-8") as f:
            py = f.read()
        with open(self.SWIFT, encoding="utf-8") as f:
            swift = f.read()
        import re
        fixed = set(re.findall(r'ZipRefusal\("(?:%s: )?([^"%]+)"', py))
        self.assertGreater(len(fixed), 15)
        missing = sorted(m for m in fixed if m not in swift)
        self.assertEqual(missing, [], "validate.py zip messages not in WidgetZip.swift")

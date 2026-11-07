#!/usr/bin/env python3
"""Builds the two zips scenarios/widget-from-file.yaml installs, from the validator's fixture packs.

    python3 smoke/fixtures/widget-packs/build.py

good-sprite.zip is Tests/SanduhrTests/Fixtures/widget-packs/good/good-sprite (a sprite, invented
name, 1x1 art). bad-no-license.zip is bad/bad-no-license (a palette without its LICENSE, which the
validator refuses). Stored entries in sorted order, fixed timestamps and modes, so a rebuild is
byte-identical and the committed zips only change when a fixture pack does.
"""
import os
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
PACKS = os.path.join(HERE, "..", "..", "..", "Tests", "SanduhrTests", "Fixtures", "widget-packs")
WANT = {"good-sprite": "good", "bad-no-license": "bad"}


def build(name, group):
    folder = os.path.join(PACKS, group, name)
    out = os.path.join(HERE, name + ".zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_STORED) as z:
        for rel in sorted(os.listdir(folder)):
            path = os.path.join(folder, rel)
            if not os.path.isfile(path):
                continue
            info = zipfile.ZipInfo(rel, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_STORED
            info.external_attr = 0o100644 << 16
            with open(path, "rb") as f:
                z.writestr(info, f.read())
    return out


if __name__ == "__main__":
    for n, g in WANT.items():
        p = build(n, g)
        print("%s  %d bytes" % (os.path.relpath(p), os.path.getsize(p)), file=sys.stderr)

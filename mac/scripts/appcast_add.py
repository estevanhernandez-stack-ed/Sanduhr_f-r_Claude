#!/usr/bin/env python3
"""Add one release to docs/appcast.xml, newest first, keeping every earlier item.

generate_appcast rebuilds the feed from a folder holding every past DMG; CI has only the new
one, so this adds the item by hand in the same shape generate_appcast writes.

Usage:
  appcast_add.py <appcast.xml> --version 2.1.0 --build 3 --url <dmg url> \
      --length <bytes> --signature <EdDSA signature> [--min-system 14.0] [--date <RFC 2822>]

Refuses a build number that is not above every build already in the feed: Sparkle orders
updates by it, so a repeat or lower number is never offered.
"""
import argparse
import re
import sys
from email.utils import formatdate
from xml.sax.saxutils import escape, quoteattr


def add_item(xml: str, version: str, build: str, url: str, length: str, signature: str,
             min_system: str, date: str) -> str:
    builds = [int(b) for b in re.findall(r"<sparkle:version>(\d+)</sparkle:version>", xml)]
    if builds and int(build) <= max(builds):
        raise ValueError(f"build {build} is not above the feed's highest build {max(builds)}")
    if f"<sparkle:shortVersionString>{version}</sparkle:shortVersionString>" in xml:
        raise ValueError(f"version {version} is already in the feed")

    item = (
        "        <item>\n"
        f"            <title>{escape(version)}</title>\n"
        f"            <pubDate>{escape(date)}</pubDate>\n"
        f"            <sparkle:version>{escape(build)}</sparkle:version>\n"
        f"            <sparkle:shortVersionString>{escape(version)}</sparkle:shortVersionString>\n"
        f"            <sparkle:minimumSystemVersion>{escape(min_system)}</sparkle:minimumSystemVersion>\n"
        f"            <enclosure url={quoteattr(url)} length={quoteattr(length)} "
        f"type=\"application/octet-stream\" sparkle:edSignature={quoteattr(signature)}/>\n"
        "        </item>\n"
    )
    # Newest first: before the first existing item, or before </channel> in an empty feed.
    for anchor in ("        <item>", "    </channel>"):
        at = xml.find(anchor)
        if at != -1:
            return xml[:at] + item + xml[at:]
    raise ValueError("no <item> or </channel> found; is this an appcast?")


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("appcast")
    p.add_argument("--version", required=True)
    p.add_argument("--build", required=True)
    p.add_argument("--url", required=True)
    p.add_argument("--length", required=True)
    p.add_argument("--signature", required=True)
    p.add_argument("--min-system", default="14.0")
    p.add_argument("--date", default=formatdate(localtime=False))
    a = p.parse_args()
    with open(a.appcast, encoding="utf-8") as f:
        xml = f.read()
    try:
        out = add_item(xml, a.version, a.build, a.url, a.length, a.signature, a.min_system, a.date)
    except ValueError as e:
        print(f"appcast_add: {e}", file=sys.stderr)
        return 1
    with open(a.appcast, "w", encoding="utf-8") as f:
        f.write(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

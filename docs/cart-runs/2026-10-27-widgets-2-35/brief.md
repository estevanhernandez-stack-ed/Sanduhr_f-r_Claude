# Run Brief: widgets-2-35

**Asked (verbatim):** "merge it and start 2.35.0", then "yes run it unattended".

**Source:** `docs/mac-widgets-spec.md` (merged in #370), sections 1 to 5 and 16 to 17 (Release 2.35.0).
The owner's calls are in the spec's "Settled" list and the dashboard decisions of 2026-10-27 on project
Sanduhr (macOS).

**Intent:** The Mac installs signed packs from the `sanduhr-widgets` catalog under the same trust rules
as Windows 3.8, draws the hourglass companion on the Desk, and gains OLED care.

**Done when:** All eight items are merged on `cart/widgets-2-35`, `cd mac && ./test.sh` and
`ruby smoke/selftest.rb` pass there, and `python3 -m pytest -q integrations/tests` passes. The owner
then runs the live Desk checks (items 4, 5 and 7), and the 2.35.0 ship commit comes after.

**Out of scope:** `mac/Info.plist`, the release, any push or PR, anything under `windows-dotnet/` (read
it, never edit it), `propose_widget` and own art (2.36.0), the fold, meter styles and font packs
(2.37.0), any live network request in tests.

**Inferences:**
- Windows' C# under `windows-dotnet/src/Sanduhr.Core/` is the line-by-line guide for every port, and
  its tests under `windows-dotnet/tests/Sanduhr.Tests/` are the case list. Where the spec and the
  Windows code disagree, the spec wins on Mac surfaces (paths, `PackFormat`) and Windows wins on rules.
- The fixture corpus is copied from `windows-dotnet/tests/Sanduhr.Tests/fixtures/widget-packs` and
  `widget-zips`, not fetched; Windows pinned it to `sanduhr-widgets` at 61b4247.
- `validate.py` (and any helper it imports) is vendored from `sanduhr-widgets` at 61b4247, read with
  `gh api`, a read-only probe.
- What's New cards are added with version "2.35.0" before Info.plist moves, as in the plan-label run.

**Mode:** unattended. The owner confirmed the recommendation (8 items); auto mode is on.

# Run Brief: widgets-2-36

**Asked (verbatim):** "update the tasks and start 2.36.0", then "yes run it unattended".

**Source:** `docs/mac-widgets-spec.md`, sections 6 (Claude-made widgets) and 7 (Your own art), and the
2.36.0 rows of section 16. Windows reference: `docs/widgets-3.8-spec.md` sections 5 and 6, as built in
`windows-dotnet/` (read it, never edit it).

**Intent:** Claude Code can propose a sprite, palette or effects pack through the Mac MCP; the app
re-checks it, previews it on the Desk and installs it only on the user's Apply. A sprite made here can
take the user's own PNG art per stage.

**Done when:** All five items are merged on `cart/widgets-2-36`, and `cd mac && ./test.sh`,
`ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there. The owner then runs
the live checks (items 3 and 4), and the 2.36.0 ship commit comes after.

**Out of scope:** `mac/Info.plist`, the release, any push or PR, anything under `windows-dotnet/`
(read it, never edit it), 2.37.0 and later (get_desk/propose_desk, the fold, meter styles, font packs),
any live network request in tests.

**Inferences:**
- Windows' `WidgetProposal.cs`, `WidgetOwnArt.cs`, `WidgetReviewService.cs`,
  `ToolLogic.Widgets.cs` and the `propose_widget` entry in `ToolCatalog.cs` are the line-by-line guide;
  their tests are the case list. The spec wins on Mac surfaces (paths, handoff folder, HandoffWatch),
  Windows wins on rules and messages.
- The MCP is Python, so it mirrors `WidgetProposal.Build` and validates with the vendored
  `mac/integrations/widgets/validate.py` in strict mode (dashboard decision bUwM3IetTods9yxWe0ev). A
  shared fixture set keeps the Python and Swift halves agreeing.
- The 2.35.0 store already has `WidgetSource.claude` and `.local` and `replaceMadeHere`; items reuse them.
- What's New cards are added with version "2.36.0" before Info.plist moves, as in the 2.35.0 run.

**Mode:** unattended. The owner confirmed the recommendation (5 items, 08:36); auto mode is on.

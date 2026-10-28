<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: widgets-2-36

## D1
- **Decision:** All five items are `go`.
- **Why:** Nothing touches money, auth, access gating or permissions. The new handoff files sit beside the existing proposal files and no new host is contacted; PRIVACY covers them in item 5.
- **Alternative not taken:** Holding item 3 (the app installs Claude-made packs) for a walkthrough.
- **Item:** all
- **How to reverse:** Leave item 3's merge out of the push and review it alone.

## D2
- **Decision:** The Python MCP mirrors `WidgetProposal.Build` and validates with the vendored validate.py (strict); the Swift app re-checks with a port of `Decode`. A shared fixture set guards drift.
- **Why:** Windows shares one C# file between MCP and app; on the Mac the MCP is Python and the app never runs Python (settled call 2). Dashboard decision bUwM3IetTods9yxWe0ev.
- **Alternative not taken:** The MCP shelling out to a Swift helper.
- **Item:** 1, 2
- **How to reverse:** n/a

## D3
- **Decision:** Windows' C# is the line-by-line guide and its tests the case list; the spec wins on Mac surfaces (handoff folder, HandoffWatch, source names), Windows wins on rules and messages.
- **Why:** Same rule as the 2.35.0 run (its D3); parity is the point.
- **Alternative not taken:** Porting from the spec text alone.
- **Item:** all
- **How to reverse:** n/a

## D4
- **Decision:** Accepted item 1's new `DeskReason` (field, message) in `WidgetProposal.swift` and a small `WidgetJson` writer over `PackValue`/`PackObject`; generated JSON uses `\n` line ends and an escape set modelled on System.Text.Json's default encoder.
- **Why:** The Mac had no reason type and no JSON writer for pack values. Byte-equality with Windows' generated files is not required: each side validates what it reads, and the shared fixtures (item 2) compare verdicts and reasons, not bytes.
- **Alternative not taken:** JSONSerialization, which reorders keys and escapes `/`.
- **Item:** 1
- **How to reverse:** Swap `WidgetJson` for another writer; the tests pin the exact text.

## D5
- **Decision:** Item 2 validates a proposed pack in a `tempfile.TemporaryDirectory` (removed after the check), because the vendored validate.py only reads a folder. validate.py stays unmodified.
- **Why:** Patching the vendored file would break its pin to `sanduhr-widgets` 61b4247. The temp folder is local and transient; item 5's PRIVACY text must say so.
- **Alternative not taken:** An in-memory adapter that rewrites validate.py's file reads.
- **Item:** 2, 5
- **How to reverse:** Teach a future vendored validate.py an in-memory entry point and drop the temp folder.

## D6
- **Decision:** Kept item 2 open to ship `widgets/validate.py` beside the MCP script (build.sh, IntegrationScripts copy and stamp, install.sh), found by the worker: the shipped app would have answered `validator_missing`.
- **Why:** propose_widget must work in an installed build, not just from the repo; the fix is the same item's surface.
- **Alternative not taken:** A separate item, or deferring it to the ship commit.
- **Item:** 2
- **How to reverse:** Revert the `fix(mac): ship the pack validator` commit.

## D7
- **Decision:** Accepted item 3's Mac departures: the Desk preview starts only on "Preview on the Desk" (Windows previews on arrival), `busy` means the Desk is being arranged (the Mac has no Desk-design review until 2.37.0), the preview lives in memory (no `widgets/.preview` staging), and the debug snapshot carries only a `pending_suggestions.widget` flag.
- **Why:** The Mac spec's banner has an explicit Preview button, and swapping the user's companion without a click is the surprise the owner pushed back on in 2.35.0. Flags-only matches the existing snapshot rule that Claude's content never lands in state.yaml.
- **Alternative not taken:** Auto-preview on arrival, as Windows does.
- **Item:** 3
- **How to reverse:** Call `previewOnDesk()` from the arrival path in `WidgetProposalHandoff`.

## D8
- **Decision:** Item 4 also wires Settings ▸ Widgets to `.widgetPacksDidChange`, found by the item 3 worker: Settings builds its own `WidgetsModel` and wouldn't show a pack installed from the review flow until reopened.
- **Why:** Same class of bug the owner caught in 2.35.0 (stale Installed state); the banner item owns that page.
- **Alternative not taken:** Leaving it for the live check to catch.
- **Item:** 4
- **How to reverse:** n/a

## D9
- **Decision:** The suggestion banner shows only while a proposal waits, with no search-index entry; "Use my own art…" is indexed with its anchor on the first eligible row only.
- **Why:** The owner keeps Settings lean (2.35.0 removed a redundant toggle and status lines); a permanent "nothing waiting" row would be noise, and the notification already opens Settings ▸ Widgets.
- **Alternative not taken:** An always-present suggestion section so search has a landing spot.
- **Item:** 4
- **How to reverse:** Revert 3372f7c7.

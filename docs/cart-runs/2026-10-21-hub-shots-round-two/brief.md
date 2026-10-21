<!-- Written once at intake, before any item is planned. Keep the Ask
     verbatim. Every Inferred line is also a ledger entry in decisions.md. -->

# Run Brief: hub-shots-round-two

## Ask
"start it unattended and send the old mac the #290 check", where "it" is the pick offered just before: "#290 confirm plus #291 fix plus #292 shots as one pass, which ends with the hub page current."

## Source
- **Kind:** open GitHub issues and a backlog leftover
- **Location:** issues #291 (the session-start card cut by the fold), #292 (hub screenshots round two: Export Config, Compare on import, Accounts), #290 (sent to the old Mac's session, not an item here); `docs/BACKLOG.md` #7's "still worth a look" line

## Intent
The hub page's next screenshots can be taken from a build where every Mods & Config card ends on a boundary, where the two sheets they need open from the harness with made-up data, and where the last measurable idle cost in the log walk is gone.

## Done when
- The "What a new session starts with" card sits above the Catalog, By Folder and Compare tabs, so it reads as a page-level card and the tab bar is the fold's natural boundary (#291).
- The bulk log walk and the reader sort paths by their UTF-8 bytes.
- `smoke/smoke do mods-config export` opens the Export Config sheet and a hook opens Compare on import over a made-up pack, both pinned in DebugTests and the smoke README.
- `docs/screenshots/mac/` holds the three #292 shots, taken in demo mode, checked for home paths and names.
- `cd mac && ./build.sh --debug`, `./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.

## Out of scope
- #290: verified on the old Mac by its own session.
- A release: these ride with the next minor.
- The hub page itself (the owner's other machine places the shots).

## Inferred
- #291 is fixed by moving the card, the issue's second suggestion, not by a taller window: a taller default changes every page and still cuts wherever the content ends (D2).
- "Compare on import" in #292 is the import sheet with a checkbox per difference, not the Compare tab; it needs a made-up pack to show without a real one (D3).
- The shots are taken by the orchestrator at close from the integration build's dev app in demo mode, since a worker in a worktree cannot drive the screen (D4).

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so ("start it unattended").

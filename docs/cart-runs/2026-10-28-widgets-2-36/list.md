# Run List: widgets-2-36

- [ ] **1. WidgetProposal and WidgetOwnArt in Swift**
  Spec ref: `docs/mac-widgets-spec.md > 6. Claude-made widgets; 7. Your own art`
  What to build: Port `windows-dotnet/src/Sanduhr.Core/WidgetProposal.cs` to `mac/Sources/Sanduhr/Widgets/WidgetProposal.swift`: `Build` (arguments to a whole in-memory pack: generated pack.json with the `my-` id, LICENSE text per license, the kind's JSON files, pixel sheets; strict `PackValidator` incl. trademark), `RequestJson`, `Decode` (re-checks everything, MaxAge 600 s, 4 MB cap, Discard/Refused/Proposal), `WidgetAnswer` and its statuses, `SanduhrMin = "3.8.0"` (the pack-format level, never the app version). Port `WidgetOwnArt.cs` to `WidgetOwnArt.swift` (CheckImage messages byte-equal, Apply, BumpPatch). Reuse the 2.35.0 types (PackValidator, PackManifest, CompanionSprite, DeskReason or its Mac equivalent). Port `WidgetProposalTests.cs` and `WidgetOwnArtTests.cs` case for case. Pure: no disk, no network. Size M.
  Acceptance: Done when the ported tests pass, including a refused theme/font kind, the `my-` namespacing, the trademark refusal, an expired request and an over-cap request.
  Verify: `cd mac && ./test.sh --filter "WidgetProposal|WidgetOwnArt"`
  Stop class: go

- [ ] **2. propose_widget in the Mac MCP**
  Spec ref: `docs/mac-widgets-spec.md > 6. Claude-made widgets` (the MCP bullets)
  What to build: In `mac/integrations/sanduhr_mcp.py`, a `propose_widget` tool with the same input schema and guide text as Windows' `ToolCatalog.cs` (`propose_widget`, `WidgetGuide`). A Python `build_widget` mirroring `WidgetProposal.Build`, validated with the vendored `integrations/widgets/validate.py` in strict mode plus its trademark rule; validate in memory if validate.py's functions allow, else in a private temp dir removed afterwards (ledger it). Refusals return `rejected` with reasons and write nothing. Otherwise write `widget-request.json` atomically to the same handoff folder the other proposals use, poll `widget-result.json` like the other proposals, pass on only `previewing|applied|rejected`, else `queued` with `widget_not_responding`. Update the module header's list of files it writes. Port `ToolLogicWidgetTests.cs` to pytest. Add a shared fixture set (`mac/Tests/SanduhrTests/fixtures/widget-proposals/`: argument JSON files plus expected accept/refuse) that a pytest and a Swift test both run, so the two halves can't drift. Size M.
  Acceptance: Done when the pytest port passes, the shared fixtures agree in Python and Swift, and a refused proposal leaves no request file.
  Verify: `cd mac && python3 -m pytest -q integrations/tests && ./test.sh --filter WidgetProposal`
  Stop class: go

- [ ] **3. The app's review flow**
  Spec ref: `docs/mac-widgets-spec.md > 6. Claude-made widgets` (The app bullets)
  What to build: Port `windows-dotnet/src/Sanduhr.App/Desk/WidgetReviewService.cs` as a Mac `WidgetProposalHandoff`/review model, following `ThemeProposalHandoff` and `HandoffWatch`. Re-validate with `WidgetProposal.Decode`, hold in memory only (a newer request answers the older `replaced`; `busy` and `not_ready` per Windows), post the existing suggestion notification. Preview: a sprite becomes the companion temporarily, a palette becomes the Desk ink temporarily; Dismiss and a newer request restore what was there. Apply installs via `WidgetStore.install(..., source: .claude)` (`install_failed` on error); Make it the companion sets `CompanionSettings.pack`. Write `widget-result.json` with `previewing`, `applied` (with `companion`), `rejected` (reasons dismissed, replaced, busy, install_failed, not_ready). No direct switch covers widgets. Debug snapshot / `state.yaml` gains the pending proposal's id and kind only if the smoke harness pins keys (update the selftest fixture then). Port `WidgetReviewServiceTests.cs`. Size L.
  Acceptance: Done when the ported tests pass (preview, apply, companion, dismiss, replaced, expired) and `ruby smoke/selftest.rb` passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **4. WidgetSuggestionBanner and "Use my own art…"**
  Spec ref: `docs/mac-widgets-spec.md > 6 (banner); 7. Your own art`
  What to build: `WidgetSuggestionBanner` in Settings ▸ Widgets, modelled on `ThemeSuggestionBanner`: name, Claude's note, "What this widget is", files and their license, "made here, on this Mac only"; buttons Preview on the Desk, Make it the companion (sprites), Apply, Dismiss. On an installed made-here sprite row, "Use my own art…": an `NSOpenPanel` per stage (PNG only), `WidgetOwnArt.CheckImage` messages shown inline, then `Apply`, reinstall as `.local` with `replaceMadeHere`. Split SwiftUI bodies so CI's type-checker doesn't time out. SettingsIndex entries for the new controls. Size M.
  Acceptance: Done when the page builds, a view-model test covers the own-art path (check failure message, success reinstalls as local with the bumped version), and the full suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. Docs, the 2.36.0 What's New card, CHANGELOG**
  Spec ref: `docs/mac-widgets-spec.md > 16` (2.36.0 rows)
  What to build: docs/PRIVACY.md Mac section: `widget-request.json` and `widget-result.json` (where, what they hold, when deleted), the in-memory hold, and that a user's own art is copied into the pack folder and never sent anywhere. Every claim exactly true to the code as built. mac/README.md: Claude-made widgets and own art. docs/mac-smoke-test.md: new numbered sections. WhatsNewCards: 2.36.0 card(s) with destinations, WhatsNewTests updated (count, ids, destinations, an `updatingToTwoThirtySix` test). CHANGELOG: `## Unreleased (mac)` inserted right after `# Changelog`. Do NOT touch mac/Info.plist. Size S.
  Acceptance: Done when WhatsNew tests pass and the docs describe only what items 1-4 built.
  Verify: `cd mac && ./test.sh`
  Stop class: go

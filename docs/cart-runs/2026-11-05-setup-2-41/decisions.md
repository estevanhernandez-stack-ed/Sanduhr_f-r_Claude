# Decisions: setup-2-41

## D1
- **Decision:** No item is `hold`. Usage off (item 2) is `go`.
- **Why:** Usage off stops fetching and deletes `snapshot.json`, which sign-out already does. It changes no credential, auth flow, access gate or permission, and the account and Keychain item stay. The Desktop permission that Tidy my files leads to shipped in 2.40.0.
- **Alternative not taken:** Hold item 2 as "usage gating". Items 4, 5, 7, 8 and 9 would then stack on a hold branch, and the owner walks the whole release live before it ships anyway.
- **Item:** 2
- **How to reverse:** Treat item 2's branch as held at review and keep it out of the push.

## D2
- **Decision:** `setup` is a standard-defaults key holding a dictionary in Windows' JSON shape: `{desktop, usage: {claude}}`.
- **Why:** The Mac keeps its settings in UserDefaults, and Windows' shape leaves room for more usage sources.
- **Alternative not taken:** A flat pair of keys (`setupDesktop`, `setupUsageClaude`).
- **Item:** 1
- **How to reverse:** Change the key mapping in `SetupSettings`; nothing else reads the raw key.

## D3
- **Decision:** On the Mac, "A nicer desktop" off hides the Desk's pieces and leaves the drawer panels showing. The saved layout is filtered at draw time and never rewritten.
- **Why:** Drawer panels are their own windows on the Mac, not layout words as on Windows, so Windows' layout filter (`SetupDesk.Layout`) has nothing to remove.
- **Alternative not taken:** Turn the whole Desk off and keep the drawer panels alive on their own. Drawers already turn the Desk on with them, so this would split the Desk's lifecycle.
- **Item:** 3
- **How to reverse:** Gate the drawer panels on the Desk switch alone.

## D4
- **Decision:** 2.41.0 is built but not shipped in this run.
- **Why:** The owner's call 1 in the spec: Drawers runs on real Desktops for a few days before Set up starts turning them on for new users.
- **Alternative not taken:** Ship when the run closes.
- **Item:** all
- **How to reverse:** The owner says ship.

## D5
- **Decision:** The migration runs once, recorded by the standard-defaults key `setupMigrated`. It is set on the first call even when there is nothing to migrate, and a stored `setup` is never overwritten. Setup values decode only as real Bools; anything else keeps the fallback.
- **Why:** Windows' rule, ported as is. A new install records the flag too, so its first setup is never treated as a later upgrade.
- **Alternative not taken:** Infer "migrated" from the presence of `setup`. That breaks for an upgrader who closes the screen without choosing.
- **Item:** 1
- **How to reverse:** Remove `setupMigrated` and run the migration whenever `setup` is absent.

## D6
- **Decision:** Item 2's switch rules, as built:
  - an absent setup, or one with no Claude entry, reads as usage on;
  - turning usage on while it is already on saves nothing, so the setup screen can still open;
  - launch with usage off deletes a stale `snapshot.json`;
  - sign-out, an account switch and `refresh` keep `usageOff`, write no snapshot and fetch nothing;
  - the follower and seat meters pause;
  - a sign-in, an added account or a saved key turns usage on.
- **Why:** Spec 11 and Windows' sign-in rule. An upgrader with an account must never come out with usage off.
- **Alternative not taken:** Keep usage off across a sign-in.
- **Follow-up for the review pass:** `saveCredentials` on the active account calls `credentialsChanged`, so a Save that only clears the key also turns usage on. Turn usage on only when a non-empty key is saved.
- **Item:** 2
- **How to reverse:** `UsageSwitch` and the guards in `UsageViewModel`.

## D7
- **Decision:** Desktop off is all-or-nothing on the Mac.
  - Every Desk piece is a layout piece, so `SetupDesk.shows` is just `desktop`.
  - The filter runs at draw time and in the hit-test layout.
  - `arrangeDesk()` refuses while desktop is off.
  - The Desk reloads on `.sanduhrSetupDidChange`.
- **Why:** D3. Drawer panels are separate windows and keep showing.
- **Follow-up for the review pass:** while desktop is off, the weather and telemetry timers key off the saved layout and may keep polling, so a weather request can go out with nothing drawn. Gate them on the filtered layout. PRIVACY must match whichever behavior ships.
- **Item:** 3
- **How to reverse:** Remove the `SetupDesk` filter calls in `DeskView` and `DeskModel`.

## D8
- **Decision:** The setup service ports Windows' order exactly. A new persisted `drawers.everSorted` key is set when a rules review is applied. The host also counts a rules run in the journal as "ever sorted", so someone who sorted on 2.40.0 gets no surprise review. `hasAccount` reads the account labels, never the Keychain.
- **Why:** Spec 10, `SetupService.cs`. The Mac had no "ever sorted" flag.
- **Alternative not taken:** Infer it from the journal alone. The journal trims to 50 runs and 30 days of hand moves.
- **Item:** 4
- **How to reverse:** Drop `everSorted` and use the journal check alone.

## D9
- **Decision:** Item 5's worker fixed an item 4 bug. The service now reads `usageOn` before saving, and the host runs `UsageSwitch.applySaved()`.
- **Why:** The real host reads the stored setup, so after the save usage looked unchanged and the switch never ran. The fake host was independent of storage and hid the bug. `SetupWindowTests` now has a host backed by storage.
- **Alternative not taken:** Have the service save usage last. That splits the screen's one save into two.
- **Item:** 4, 5
- **How to reverse:** n/a (bug fix).

## D10
- **Decision:** Item 5's shape:
  - The What's New card uses `opensSetup` with destination `.general`, not a new `SettingsSection`, which would have broken the 20-page pins.
  - The widget is held hidden while no setup exists, and the window opens 0.5 s after launch.
  - `OnboardingSheet` stays as the sign-in sheet, without Just the Desk, and shows only when usage is ticked and there is no key.
  - The tour's `justTheDesk` becomes `usageOff`, with a new last card, "Usage when you want it", that opens Set up.
  - The dead Just the Desk paths are deleted, and `JustTheDesk` keeps only the migration's reads.
  - The `just-the-desk on|off` smoke hook now applies setup with usage off or on.
- **Why:** Spec 10, Windows items 7 and 8, with the Mac's existing sheet reused for sign-in.
- **Follow-up:** After Continue with usage ticked and no key, the sign-in sheet shows only at the next launch. Until then, the widget's "Not signed in" line is the way in. Worth opening it right after Continue. Review pass.
- **Item:** 5
- **How to reverse:** Item 5's commit.

## D11
- **Decision:** Mac and Windows vocabulary in the chat prompt and reader.
  - The prompt teaches the rule kind `shortcuts`, which maps to the Mac's `links`; the reader accepts both.
  - Corners `tl`/`tr`/`bl`/`br` map to the Mac's corner cases.
  - The reader keeps the `desk` half raw for the Desk validator, which refuses Windows-only fields by name.
  - The reader forgives trailing commas and comments outside strings.
  - The first clean block wins, and one error in a block clears its rules, look and arrange.
  - The prompt takes no state, so it drops Windows' monitor-names line and carries no `/`, `~`, `%` or `@`.
- **Why:** One answer has to read on both platforms (spec 12), and the prompt must not leak the person's state.
- **Alternative not taken:** Check the desk keys in the reader. That duplicates the validator.
- **Item:** 6
- **How to reverse:** `PastedSetup.swift` and `SetupPrompt.swift`.

## D12
- **Decision:** A pasted design rides the shared `DeskDesignProposalHandoff` with a `pasted` flag.
  - It gets a neutral heading, "An AI chat suggests a Desk design".
  - It is held in memory only, writes no result file, raises no notification and is never shown to the MCP.
  - Preview opens Settings, Desk, where the review banner lives.
  - "Use these drawer settings" has its own Undo in the window, restoring look, opacity, Tidy corner and flow, and every place.
  - The window clears the paste on close.
- **Why:** One review path keeps the validator and banner identical to `propose_desk` (spec 12).
- **Follow-up for the review pass:** Windows refuses "Use these drawer rules" while a drawers review is pending; the Mac does not. Port that check.
- **Item:** 7
- **How to reverse:** Remove the `pasted` flag and give pasted designs their own review type.

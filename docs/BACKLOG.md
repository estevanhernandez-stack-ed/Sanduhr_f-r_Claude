# Backlog

## #1 — Sign Out must actually purge stored credentials, both platforms (shipped)

**Shipped.** Mac: Sign Out in Settings purges the account's Keychain items and the legacy file (2.3.3, PR #106; per-account slots in 2.4.0; a dev build never touches release items, `ace2f57`). Windows: Sign Out already removed the account's Credential Manager targets through `AccountStore.RemoveAccount`. Kept below for the record.

Uninstall cannot do this on either channel — this is architectural, not a gap
that gets fixed by adding an uninstall hook:

- **Windows:** Credential Manager (`advapi32.dll` CredWriteW / CRED_PERSIST_ENTERPRISE)
  is an OS-level per-user vault, not package-scoped storage. MSIX package removal
  never reaches it — `Package.appxmanifest` runs the app outside the MSIX sandbox
  specifically so it can read/write `%APPDATA%\Sanduhr\` directly, and Desktop
  Bridge full-trust packaging doesn't virtualize the Credential Manager API either
  way. The Velopack GitHub/.exe channel's only uninstall hook
  (`windows-dotnet/src/Sanduhr.App/Program.cs:27-34`, `OnBeforeUninstallFastCallback`)
  unregisters toast notifications only — no credential deletion call exists in it.
  Net: every `{slot}@com.626labs.sanduhr` compound target (one pair per saved
  account) survives uninstall on both channels.
- **macOS:** no uninstall hook exists at all — `mac/Sources/Sanduhr/AppDelegate.swift`
  has no `applicationWillTerminate` cleanup. Dragging to Trash never touches
  `~/Library/Application Support/Sanduhr/` (outside the `.app` bundle), so
  `credentials.json` survives indefinitely.

**The only correct fix on either platform is the in-app Sign Out path already wired
correctly today** — `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs:719-755`
→ `AccountStore.RemoveAccount` (`windows-dotnet/src/Sanduhr.Core/AccountStore.cs:96-113`)
already deletes the right Credential Manager targets on Windows. Work needed:

1. Confirm Sign Out reliably purges every credential slot it should, on every
   code path (including the "last account" case that should also clear the
   shared WebView2 profile — `WidgetViewModel.cs` handles this per the privacy
   sweep, but wasn't independently re-verified this session).
2. macOS has no Sign Out credential-purge path documented as existing yet —
   build one that deletes `~/Library/Application Support/Sanduhr/credentials.json`
   (see `mac/Sources/Sanduhr/Services/KeychainStore.swift`).
3. Make Sign Out (not uninstall) the one documented, tested, reliable way to
   clear credentials on both platforms — it already is in the docs (this session
   corrected README.md, SECURITY.md, docs/PRIVACY.md, docs/index.html,
   mac/README.md, docs/store/product-features.md to say so honestly); now the
   code needs the same bar macOS is missing.

## #2 — macOS: migrate credential storage from plaintext JSON to the system Keychain (shipped)

**Shipped in 2.3.2** (PR #100): credentials live in the Keychain under `com.626labs.sanduhr`, the plaintext file migrates in and is removed; docs updated in `259c20b`. Kept below for the record.

`mac/Sources/Sanduhr/Services/KeychainStore.swift:1-87` (type name is a holdover —
it does not call any Keychain API; grepped `SecItemAdd|SecItemCopyMatching|SecItemDelete|import Security`,
the only hit is the file's own doc comment referencing `SecItemAdd` as considered-and-abandoned).
Stores `sessionKey` + `cf_clearance` as plaintext JSON at
`~/Library/Application Support/Sanduhr/credentials.json`, protected only by
POSIX `0600` (`KeychainStore.swift:56-86`).

**Why now:** the file's own doc comment (`KeychainStore.swift:11-26`) states this
was a deliberate interim tradeoff — default Keychain ACLs bind to the exact code
signature that wrote an item, so unstable ad-hoc signing during iteration
triggered a Keychain prompt on every rebuild. `mac/release.sh:9-10` (prerequisite:
Developer ID Application cert) plus the signed+notarized build steps confirm
**the stated blocker is resolved** — the app ships with a stable signature today.
The precondition to revert to real Keychain has been met; the code hasn't been
changed back.

## On ship (both items above)

This repo's docs were updated with 2.3.2 (`259c20b`). Still to confirm: the Sanduhr subsection of the 626labs.dev privacy page describes the fixed behavior.

Update, in the same session the fix lands:

- This repo's docs: `README.md`, `SECURITY.md`, `docs/PRIVACY.md`, `docs/index.html`,
  `mac/README.md`, `docs/store/product-features.md` — all corrected 2026-08-03 to
  describe today's *actual* (broken) behavior; once Sign Out reliably purges
  credentials and macOS moves to Keychain, these need a second pass to describe
  the *fixed* behavior.
- `https://626labs.dev/privacy.html#privacy-sanduhr` — the canonical, cross-app
  privacy policy. Its Sanduhr subsection currently documents the un-fixed
  behavior honestly (uninstall doesn't clear credentials on either platform;
  macOS is plaintext, not Keychain). Update it once the fix ships.

## Evidence pointers (for the next session)

- `C:\Users\estev\Projects\626labs-hub\.superpowers\sdd\privacy-findings-2026-08-03.md`
  — §2 (Sanduhr), full six-app privacy sweep.
- `C:\Users\estev\Projects\626labs-hub\.superpowers\sdd\sanduhr-uninstall-rororo-signing-2026-08-03.md`
  — §Q1, exact file:line evidence for both platforms + drafted honest policy sentences.
- `windows-dotnet/src/Sanduhr.App/Program.cs:27-34` — the one Windows uninstall
  hook that exists, and what it actually does (not credential cleanup).
- `windows-dotnet/src/Sanduhr.Core/WindowsCredentialManager.cs:66-213` — the real
  Credential Manager P/Invoke layer (`CredReadW`/`CredWriteW`/`CredDeleteW`).
- `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs:719-755` — the
  working Sign Out → credential-delete path to build on.
- `mac/Sources/Sanduhr/Services/KeychainStore.swift:1-87` — the plaintext store
  masquerading under a Keychain-shaped name; doc comment explains the original
  tradeoff and its now-met precondition to revert.
- `mac/release.sh:9-10,47-54` — confirms Developer ID signing + notarization are
  live today, closing the stated blocker to real Keychain.

## #3 — New user walkthrough (shipped as the welcome tour)

**Shipped as the welcome tour in 2.7.0** (checklist item 61, `docs/welcome-tour-spec.md`), with a Desk-only variant for Just the Desk in 2.29.0 (`docs/cart-runs/2026-10-19-just-the-desk/`). Spec'd 2026-10-05 as a shared tour on the What's New cards. The sketch below is kept for the record.

Raised 2026-10-05. A new install today gets the Welcome sheet (paste the `sessionKey`) and then a
widget, and nothing points at the rest: the Desk, the notch wings and glow, the menu bar, accounts,
the Claude Usage page, the integrations, themes. What's New (2.6.0) covers what changed for people
who updated; nothing covers what exists for people who just arrived.

Shape to spec:

- After the first successful sign-in, a short skippable walkthrough: four to six steps, each
  pointing at the real thing on screen (the widget, a Desk corner, the notch, the menu bar icon)
  with a sentence and a Show me / Next, plus "Set this up later" that leaves everything off.
- Choices made along the way are real settings: Desk on or off, the notch wings, the menu bar
  readout, a theme or Match Desk. Defaults stay what they are when skipped.
- Reuse What's New's card table and Show me routing where it fits; the walkthrough is data, so
  later features add a step without code.
- Reopen any time: Settings, About and the Help menu ("Take the Tour…").
- Never during the Welcome sheet; never again after it finishes or is skipped (a defaults flag),
  and updates never show it (What's New handles those).
- Accessibility: keyboard through every step, VoiceOver labels, Reduce Motion respected.
- Windows parity as its own item once the Mac version settles.

Open questions: whether the walkthrough also offers the MCP integrations (likely a last, optional
step), and whether a demo mode (made-up numbers until real usage arrives) helps the first minute.

## #4 — "Just the Desk": Sanduhr without a Claude account (Mac complete; Store copy and Windows parity with the Windows agent)

**First slice built 2026-10-11 for 2.15.0** (`JustTheDesk`, smoke-test section 45): the welcome sheet's **Just the Desk**, the widget following its choice without waiting for sign-in, no sign-in line on the Desk or notch, What's New still showing after updates, and a sign-in ending it (the reminder back on if it turned it off). Alerts and Claude Code now open with a plain line that they start once you sign in (Usage already said "Add an account in Accounts first"). An empty notch wing collapsing shipped with checklist item 72 slice 4 (noted 2026-10-17). Completed on the Mac 2026-10-19 (run `docs/cart-runs/2026-10-19-just-the-desk/`): a Desk-only tour offered after Just the Desk, and the landing page and README no longer requiring a subscription. Still elsewhere: the Store copy and Windows parity (the Windows agent), and the hub page at 626labs.dev/sanduhr (the owner's other machine).

Raised 2026-10-10 by the owner: "The Desk portion of our app is strong enough that some folks who
don't have Claude might like it." Today a first launch assumes a Claude subscription: the welcome
sheet only offers sign-in, the widget shows "Not signed in", and the Claude pieces (meters, the
Claude line, the notch's meters wing) sit empty or remind. 2.14.0 adds the first piece, a switch
for the sign-in reminders on the Desk and notch (Settings, Alerts).

Shape to spec:

- The welcome sheet gets **Just the Desk** beside Sign In: it turns the sign-in reminders off,
  hides the widget, and leaves the Desk with the clock, meetings, message, now playing and
  watchers; the Claude pieces stay placed but hidden while there is no account.
- Signing in later (Accounts) brings every Claude piece back without a relaunch; nothing a
  Desk-only user set is lost.
- The menu bar item has something to show without numbers (the time, or the hourglass alone).
- Usage, Alerts' limit sections and the Claude Code page say plainly that they need an account,
  instead of looking broken.
- Store and landing copy: describe the Desk on its own, with the trademark disclaimer kept on any
  surface naming Claude (the 10.1.4.4 bar).
- Windows parity as its own item.

Open questions, answered 2026-10-19: the Keychain prompt path stays (the launch's one
`KeychainStore.exists` lookup gets no prompt from a signed release build; the dev build's prompt is
its partition list, see `CLAUDE.md`), and the tour has a Desk-only variant (`WelcomeTour.deskOnly`,
three steps, offered once after the choice).

## #5 — Windows Store build: AppData writes are invisible outside the package (fixed in 3.5.0)

**Fixed in 3.5.0, without the capability.** Microsoft denied the `rescap:unvirtualizedResources`
request on 2026-10-20, so 3.4.3 (fix candidate 1) never shipped to the Store. 3.5.0 declares only
`runFullTrust`, withdraws the request, takes MinVersion back to 10.0.17763.0, and takes fix
candidate 2 instead: every file a process outside the package reads or writes lives in
`%USERPROFILE%\.sanduhr`, outside AppData, where nothing is redirected. Private data (settings,
history, themes, receipts, the vault) stays in AppData and goes with a Store uninstall. Existing
registrations move on first launch. See "Plan B" in `windows-dotnet/docs/spec.md`.

Historical note on the 3.4.3 attempt (PR #192, merged 2026-10-11; Store submission
1152921505702097635; denied before release). Fix candidate 1 below was built: `desktop6:FileSystemWriteVirtualization`
`disabled` with `rescap:unvirtualizedResources`, scoped on Windows 11 (build 20348+) to
`%APPDATA%\Sanduhr` and `%LOCALAPPDATA%\Sanduhr` through `virtualization:ExcludedDirectories`;
MinVersion raised to 10.0.18362.0; a one-time migration (`PackagedDataMigration`) copies older
Store data out of the package LocalCache. Verified on Neb: an in-place upgrade from the Store 3.4.2
(8/8 steps, 8,204 files migrated, no fresh sign-in) and a parked-folder run in which both real
folders were created and LocalCache got no new writes. A shipping app without the opt-out (RoRoRo)
measured the opposite on the same machine, which is the negative control. Fix candidate 3 (a ping
that tells "no snapshot" from "snapshot I can't see") was not built. Neb's hand-made junction,
symlinks and resolver script were set aside during the smoke test. Kept below for the record.

Raised 2026-10-11 from failures on Neb, which runs the Store (MSIX) build. Tracked as the critical
dashboard task "MCP launcher points at a path Claude Code can't see on Store (MSIX) installs".

`Package.appxmanifest` declares only `rescap:runFullTrust`, and its comment assumes that lets the
app read and write `%APPDATA%\Sanduhr` directly. It doesn't: full trust leaves MSIX AppData write
redirection on. New folders and files the packaged app creates under AppData land in
`%LOCALAPPDATA%\Packages\626LabsLLC.SanduhrfrClaude_wz1chhb2h2v4a\LocalCache\Roaming\Sanduhr`.
A folder that already existed in the real AppData is written in place. The result is a split
install:

- `bin\sanduhr-mcp.cmd` lands in the real `bin\` and names `%APPDATA%\Sanduhr\mcp\v...\sanduhr-mcp.exe`,
  but `mcp\` was redirected, so Claude Code (outside the package) finds no exe: "The system cannot
  find the path specified", then CONNECTION_CLOSED.
- On 2026-10-07 the same split hid `snapshot.json`, history and settings from `sanduhr-mcp`:
  `ping` reported `no_data` with `cc_roots_consented=[]`, which reads as a consent problem
  rather than a path problem.
- Neb was patched by hand on the way: a resolver launcher (`sanduhr-mcp-resolve.ps1`, a cache under
  `%LOCALAPPDATA%\Sanduhr\mcp-cache`, a temp file that races when spawns overlap), four symlinks, and
  a junction for `mcp\`. None of it is app code. Remove it once the fix ships.

Velopack installs aren't packaged and never see this, which is why Dunder is clean.

Fix candidates (verify against Microsoft docs before choosing):

1. **Recommended:** turn off write virtualization in the manifest: the
   `desktop6:FileSystemWriteVirtualization` property set to `disabled`, with the
   `rescap:unvirtualizedResources` capability. Fixes every path at once. Costs a second rescap
   justification in the reviewer letter and an OS-floor check.
2. Have the packaged app write everything an outside process reads (snapshot, history, `mcp\`,
   `bin\`) to the real path explicitly. Brittle: every new file has to remember.
3. Have `sanduhr-mcp` also probe the LocalCache path. Needs a `TrustBoundaryTests` allowlist change,
   and fixes only the reader side; the launcher's exe path is still wrong.

Also: `ping` should tell "no snapshot anywhere" apart from "snapshot at a path I can't see", and
the manifest comment should be corrected. Verify with a clean Store install on a machine with no
existing `%APPDATA%\Sanduhr`.

## #6 — No-notch island: what still needs the old Mac

**Raised 2026-10-18 from the no-notch island run (issue #119, smoke-test section 51).** The pill
shipped on a notched MacBook through the debug override; every rule that needs a screen without a
notch, or hardware this Mac does not have, stays open here. The issue keeps the hardware check. Seen live 2026-10-19 on the old Mac (maccy-lives, a 2560 by 1600 built-in display with no notch, 2.28.0): the pill draws and reads; screenshots in `docs/screenshots/mac/notch-pill-*.png`. The measurements below are briefed to that Mac's session; their note lands at `docs/notes/no-notch-measurements.md`.

- **Measured 2026-10-19** on the old Mac (`docs/notes/no-notch-measurements.md`): Xcode's menus end at 737 pt, the pill sat at 753 to 926, the leftmost status item at 1099 on a 1680-pt screen; the hide is what keeps a wide app menu reachable, and the cap went from 0.36 to 0.30 so the pill always clears the status items. Yielding to the app menu would need Accessibility menu frames and is not done. The bullet as raised: the menu-collision rule, measured on real no-notch hardware. The rule was conservative: the
  pill is never wider than 0.36 of the screen (`NotchPill.maxWidthFraction`) and hides while the
  pointer is in the menu bar, so whatever sits under it can always be reached. On the old Mac,
  with a wide app menu (Xcode, Final Cut) and a full row of status items, measure where the two
  actually meet and decide whether the cap, the hide, or a layout that yields to the app menu is
  the rule.
- **Fixed 2026-10-19, verified 2026-10-21 on the old Mac in 2.31.0** (run `docs/cart-runs/2026-10-19-pill-badge-and-splits/`; `av_indicators.shown` beside_right, the dot inline in the pill, `docs/screenshots/mac/old-mac-2.31/pill-camera-dot-beside.png`; the 0.30 cap kept the pill at 750 to 930 pt with Xcode frontmost, clear of the status items at 1099): the camera and mic badge on a no-notch Mac placed itself at the top center, under the pill.
  `AVIndicatorPlacement.spot` ties `islandUp` to the wings window (`AVIndicatorViews.update`), so
  with the pill up and no wings window the indicators fall back to the tab. The pill view already
  carries the beside-wing slot; the placement needs to count the pill as the island.
- **Measured 2026-10-19** (`docs/notes/no-notch-measurements.md` section 2): on the old Mac's 24-pt built-in bar the pill's type is 10.5 pt, digits 7 to 8.5 pt tall; a 12-pt floor would widen the steady pill from 173 to about 198 pt, past what clears Xcode's menus. The floor is the owner's call from the 2x crop in the note. As raised: a floor for the pill's text size on a 24-pt external-display menu bar. The pill is as tall as
  the bar and sizes its text to it; a 24-pt bar on an external display is tighter than a notched
  MacBook's 37-pt one. Read it at arm's length on the old Mac's display and set a floor if it
  needs one.
- **Findings 2026-10-19** (note, section 5): the old Mac has a Touch Bar; DFRFoundation is cache-only but its exports list the Control Strip presence call; the AppKit side is not verifiable from the cache. No app code. As raised: the Touch Bar spike (issue #119, Proposal B): needs the Touch Bar Mac and a probe against the
  private DFRFoundation APIs. A findings note, not app code.
- **Measured 2026-10-19** (note, section 4): the Desk, the pill and a watcher cost about 8 MB of RSS and no measurable CPU over an hour on the 2019 i5. The finding is a sustained baseline of about 9% of one core with the Desk off, with the debug hooks on in both runs; still open: the same hour with the hooks off, to separate the harness from the app. As raised: CPU and memory with the Desk on, on the old Mac (issue #119, "Also measure"): Activity Monitor
  over an hour with the Desk, the pill and the watchers on, against the same hour with the Desk
  off.

## #7 — Idle CPU: the app should idle under 1 percent of a core (met in 2.31.0)

**Raised 2026-10-19 from the no-notch measurements** (`docs/notes/no-notch-measurements.md`
section 4). Steady-state CPU with nothing happening, measured as CPU seconds per minute (`ps -o time`
deltas; `ps %cpu` is a lifetime average and misleads):

| Mac | Build | Hooks | Steady |
|---|---|---|---|
| 2019 i5 (no notch) | 2.28.0 | on | about 9 percent, both with the Desk on and off |
| newer Mac (notch) | 2.28.0 | off | 6.1 percent (3.66 CPU seconds in 60 s) |

So it is the app, not the Desk and not the smoke harness.

**Lead, from a 10 s `sample` of the release app:** the hottest frames are the local Claude Code
burn scan, `UsageViewModel.refreshLocalBurn` to `LocalBurnSource.scan` to
`CCLogReader.discoverLogFiles` to `LocalCCLogFileSystem.jsonlFiles(under:)`. Every 30 s (the
countdown tick) and on every refresh it walks the shown seat's whole `projects` folder with a
`FileManager` enumerator, stats every `.jsonl` and sorts the list before reading anything. On the
newer Mac that is 933 files over 1.5 GB across the two seats. The file set changes rarely. SwiftUI
graph updates (`AttributeGraph`) also appear in the sample.

Plan:

1. Profile first: Instruments' Time Profiler or `sample Sanduhr 30` with the hooks off, on both
   Macs, so the fix answers the measurement and not the guess.
2. Cache the log file list: re-walk a project folder only when its directory modification time
   changes (or watch `projects` with FSEvents), and never more than once a minute; keep the per-file
   stat-and-offset cache that already avoids re-reading.
3. Look at what the countdown tick redraws every 30 s, and at any other timer that wakes the
   process when nothing changed.
4. Re-measure by CPU seconds per minute on both Macs. Done when the newer Mac idles under 1 percent
   and the 2019 i5 under 2, with the Desk, the pill and a watcher on.

**Found 2026-10-21, after 2.30.0 shipped the bulk walks and measured flat (6.4 percent):** the cost
was the widget's breathing glass. `ProgressBarView` drew the pulse through a
`TimelineView(.animation(minimumInterval: 0.066))` on every filled bar, fifteen frames a second,
forever, and a hidden widget window still ran it. A 60 s sample put two thirds of the main thread's
busy time in CoreAnimation commits with no app frame above them, and a dev build in demo mode
measured 4.45 percent before and 0.59 percent after moving the pulse to a Core Animation layer
(`BreathLayerView`, the render server animates opacity; Reduce Motion holds the rest opacity).
Still worth a look: the bulk walk sorts paths with Unicode comparison (about 0.2 percent); the
Desk's per-second watcher rows and the menu bar rotation are the next timers.

**Met 2026-10-21 on the 2.31.0 release app**, signed in, hooks off, five minutes after launch, CPU
seconds per minute:

| Mac | Before | 2.31.0 | Target |
|---|---|---|---|
| newer Mac (notch) | 6.1 percent (2.28.0) | 1.18, 0.92, 0.83 percent | under 1 |
| 2019 i5 (no notch) | about 9 percent (2.28.0) | 0.40, 0.43, 0.45 percent | under 2 |

`brew upgrade` replaced the bundle but left the old process running on the old Mac; quit and
relaunch before measuring an update.

## #8 — Set up your Desk with Claude, down to the widgets and mods you make for yourself

**Raised 2026-10-22 by Este**, after moving his own desktop from Rainmeter to the Desk in one
sitting with Claude Code: *plan the layout and widgets with Claude so well that people end up
making widgets and mods for themselves while they set up their Desk.* Setting up the Desk should
be a conversation, and the end of it can be something that didn't exist before: a sprite, an
effect, a Claude Code band built for that person.

**What already exists (3.7.1 / sanduhr-widgets / sanduhr-mods):**

- The MCP server reads the Desk (`get_desk`: pieces, anchors, fonts, monitors, a wallpaper
  summary, saved designs, Start-menu apps) and proposes changes through `propose_desk`,
  `propose_theme`, `propose_desk_messages` and `propose_now_playing_looks`. Every proposal previews
  live and waits for Apply, unless the user turns on direct changes; every applied look is saved
  and Put my Desk back undoes it.
- `sanduhr-widgets` defines packs (`pack`, `sprite`, `palette`, `effects` schemas) with a
  validator, a deterministic catalog and signing. The hourglass sprite and the OLED Neon packs
  are built. **Nothing in the app renders a pack yet** (the 3.8 Widgets engine), and the catalog
  signing key isn't generated.
- `sanduhr-mods` ships Claude Code mods (now-playing, finish-line, page-pane, decision-logger,
  agent-comms), and its README carries mods-to-build-yourself as prompts (`d60bb40`).
  now-playing 0.3.0 already shares per-song looks with the Desk, so a mod and the Desk can work
  as one design.

**The gaps, in the order they unblock the vision:**

1. **The Widgets engine (3.8).** Render installed packs on the Desk, sprite first (the
   hourglass), with OLED care's drift applied to them. Without it, nothing a user makes can show.
2. **`propose_widget`.** Claude authors a pack in the conversation: a sprite's stages and moods,
   a palette, an effect. It's validated against the same schemas, previewed on the Desk, and kept
   only on Apply. A user-made pack installs locally, unsigned, with the user's consent; the signed
   catalog stays for shared packs.
3. **Art without breaking the image boundary.** Today Claude cannot supply images (a Launcher
   image proposal is refused by design). Keep that rule. Let packs carry art only in forms the
   validator can fully check (pixel sprite sheets as palette-indexed data, vector shapes), or art
   the user adds themselves. Decide this before gap 2 ships.
4. **A layout language that matches the ambition.** Eight anchors with one stack each is the
   ceiling today. The fold or perspective, livelier bars and two-face stacks are in #314. Free
   placement for widgets, and a weather nickname (#315), are the small first steps.
5. **The design session itself.** A Claude Code skill or prompt in `sanduhr-mods` that runs the
   setup as an interview: read the Desk and the wallpaper, ask what the person wants to see and
   where, propose, iterate on the live preview, then offer the widget or mod that would finish it.
   It ends with the person's own pack or mod, installed.
6. **Mods from the same conversation.** When the Desk wants something in Claude Code (a band, a
   statusline, a hook), the session writes it with the plugin-authoring flow and
   `claude plugin test`, the way the now-playing bridge was built.
7. **Sharing.** Export a design, a pack or a mod; submit packs to the catalog through the
   existing validator and CI.

**Boundaries that don't move:** the MCP process stays network-free and propose-only (`TrustBoundaryTests`); nothing
the user didn't Apply persists; a pack is data, never code; mods follow Claude Code's own install
and consent. The Store's privacy page and the reviewer letter get any new file before the
submission that carries it.

**Mac parity:** the Mac Desk takes the same proposals; the pack engine lands on both platforms or
says which one it is on.

**Done when** a new user, starting from a blank Desk, finishes a single Claude Code session with
a Desk they designed and at least one widget or mod that Claude made for them, without editing a
file by hand.

## #9 — Sprite sheets, later (Mac first)

**Raised 2026-10-28 by Este**, after the 2.36.0 own-art check. A new sprite from your own art and
Claude-proposed motion are planned as 2.39.0 (`docs/mac-widgets-spec.md` sections 18 and 19). These
three wait:

1. **Import an animated GIF or an Aseprite file.** Sanduhr unpacks the frames into a sheet on the Mac
   (ImageIO reads a GIF's frames; Aseprite's exported sheet and its JSON tags map almost straight onto
   `sprite.json`). Local only.
2. **Art per growth stage.** Own art already works per stage; what's missing is a page that shows
   one column per stage, so the companion's growth is visible while you pick.
3. **Export a pack.** "Export…" on a pack made here writes a zip the user can carry to Windows or
   submit to the catalog by hand. The app never pushes to the catalog (spec, Scope).

Not planned: turning a user's PNG into a pixel sheet Claude can read or edit inside Sanduhr (that
would send the art to Claude), and any upload host. A user who wants Claude's help making a sheet
from their own art does that with Claude outside Sanduhr; Sanduhr just uses the finished sheet.


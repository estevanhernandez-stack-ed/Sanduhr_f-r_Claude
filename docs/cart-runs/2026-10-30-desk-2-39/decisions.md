# Decisions: desk-2-39

## D1
- **Decision:** Seven items: sprite rules, the new-sprite sheet, `propose_sprite_motion`, motion review, bar motion rules, the audio tap (hold), docs. Items run in list order, one at a time.
- **Why:** Spec sections 18, 19 and 14 split along testable pure rules first, surfaces after, as in the 2.38.0 run.
- **Alternative not taken:** Leaving the tap to its own session after the prompt is seen.
- **Item:** all
- **How to reverse:** Drop item 6 from the integration plan; its branch stays separate either way.

## D2
- **Decision:** The audio tap (item 6) is `hold`, and the owner's walkthrough of its branch is the spec's "first, see the prompt" check. Its PRIVACY, README and Settings copy stays on that branch as a draft until the real prompt text is recorded.
- **Why:** A new TCC permission is a permissions surface; the spec writes the copy from the real prompt, which only a live Mac shows.
- **Alternative not taken:** A separate throwaway build before the run; the hold branch is that build.
- **Item:** 6, 7
- **How to reverse:** Merge item 6 on the owner's say-so after the copy is final.

## D3
- **Decision:** Only item 6 may touch `mac/Info.plist`, to add `NSAudioCaptureUsageDescription`; the version keys stay for the ship commit.
- **Why:** The tap cannot ship without the usage string, and the hold keeps it under the owner's eye.
- **Alternative not taken:** Owner adds the key by hand at merge.
- **Item:** 6
- **How to reverse:** Drop that hunk from the item 6 branch.

## D4
- **Decision:** A new sprite needs `sit` tagged (one cell or more) and `walk` with two or more; `nap`, `cheer` and `worry` fall back to `sit`. The five tags fill the format's nine animations the hourglass way (`pop` and `celebrate` play cheer, `focus` plays sit, `sleep` plays nap). Cell width and height are each 8 to 64; scale 1 to 4, default 2.
- **Why:** The spec's fallback is to `sit`, so `sit` must exist; the format requires nine animations.
- **Alternative not taken:** Letting `sit` fall back to the first walk cell.
- **Item:** 1
- **How to reverse:** `WidgetNewSprite.animations`.
- **Note:** The worker's hand-back came back empty; the session read the commit (d396eb92) and re-ran `./test.sh` (2617 passed) before merging.

## D5
- **Decision:** Claude learns about made-here sprites from a `sprites` list in `desk-state.json` (`id`, `name`, `frame_w`, `frame_h`, `cells`), read by the existing `get_desk`. No new tool, no new file.
- **Why:** The spec allows a field on an existing read tool; the app already rewrites that file owner-only on every pack change.
- **Alternative not taken:** A `get_widgets` tool.
- **Item:** 3
- **How to reverse:** Drop the `sprites` key and add a read tool.

## D6
- **Decision:** `propose_sprite_motion` takes `animations`, `moods` and `triggers` whole; `fps`, `scale`, `roam` are optional and unchanged when absent. Size cap 256 KB (a motion has no files). The app answers `not_ready` until item 4 wires the review.
- **Why:** The state exposes only frame size and cell count, so the server can't validate a partial table against the installed one.
- **Alternative not taken:** Partial tables merged in the app.
- **Item:** 3, 4
- **How to reverse:** `SpriteMotionProposal` and `propose_sprite_motion` argument rules.

## D7
- **Decision:** A motion Apply reinstalls the sprite from the source it already had (a Claude-made sprite stays `claude`, own art stays `local`). The worker's version turned every applied sprite into `local`; the session changed it at review (7bd8da99) and added a test.
- **Why:** `WidgetStore`'s rule is that a pack keeps its source; a motion changes how a sprite moves, not whose it is. Spec 19 says only "reinstalls".
- **Alternative not taken:** `.local` always, as own art does.
- **Item:** 4
- **How to reverse:** `WidgetStore.reinstall` in `SpriteMotionReview.swift`.

## D8
- **Decision:** Motion proposals have no `expired` answer: a request older than ten minutes is dropped unanswered, as widget proposals are. Decline answers `rejected`/`dismissed`; a newer request answers the older `rejected`/`replaced`. Preview on the Desk needs the Desk on; the button is disabled with a hint otherwise.
- **Why:** Same statuses as `propose_widget`, which the server already parses.
- **Alternative not taken:** A new `expired` status on both sides.
- **Item:** 4
- **How to reverse:** `SpriteMotionHandoff`.
- **Known gap:** a widget preview started while a motion preview shows may leave the motion banner saying "End preview" (one shared Desk companion slot). Cosmetic; live check.

## D9
- **Decision:** Item 6's keys and rules: switch `nowPlaying.barsLevel` and `nowPlaying.barsGradient` (both off, desk suite); denied after 3 s of running output with every peak exactly zero; "Allow in System Settings" opens `x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture` (Screen & System Audio Recording; no dedicated audio-capture anchor confirmed); the real tap is gated to macOS 14.2+, the switch disabled below with a note.
- **Why:** The spike's suggested names; the public Settings URL closest to the pane that holds the audio-only list.
- **Alternative not taken:** A private TCC preflight (excluded by the spec).
- **Item:** 6
- **How to reverse:** `AudioLevelTap.swift`, `AudioDeniedInference.deniedAfter`.

## D10
- **Decision:** Item 6 deviations accepted for the walkthrough: bars are 16 individual layers over a replicator of resting dots (a replicator cannot vary per-copy height); bars sit above the position bar instead of replacing it; the Desk's denied line is text with a two-finger menu item (the Desk has no SwiftUI buttons), Settings has the button; the prompt appears at the first tap after the switch is on (needs something playing), not at the toggle; a browser whose sound comes from a helper process may leave the bars still.
- **Why:** Each is a platform fact or keeps the current layout; none widens what the tap reads.
- **Alternative not taken:** Replacing the position bar; tapping every process of a browser.
- **Item:** 6
- **How to reverse:** Owner calls at the walkthrough.

# Run Brief: desk-2-39

**Asked (verbatim):** "start 2.39.0", then "Unattended (Recommended)" on the proposed seven-item list.

**Source:** `docs/mac-widgets-spec.md`, sections 18 (a new sprite from your own art), 19 (Claude maps the
motion, never sees the art), 14 (livelier now-playing bars, decided 2026-10-30: opt-in in 2.39.0) and the
2.39.0 rows of section 16. Spike: `docs/spikes/2026-10-30-audio-tap-bars.md`. Windows reference, read only:
`windows-dotnet/src/Sanduhr.Core/AudioBars.cs` and `windows-dotnet/tests/Sanduhr.Tests/AudioBarsTests.cs`.
The sprite work has no Windows counterpart; the Mac's own-art and `propose_widget` code is its reference
(`mac/Sources/Sanduhr/Widgets/WidgetOwnArt.swift`, `WidgetProposal.swift`, `WidgetReview.swift`,
`Services/WidgetProposalHandoff.swift`) and the hourglass pack is the mapping reference
(`mac/Tests/SanduhrTests/Fixtures/widget-packs/packs/hourglass/sprite.json`).

**Intent:** A user can make a companion sprite from one PNG sheet of their own, tag its cells to actions,
and let Claude propose the motion without Claude ever seeing the image. The now-playing bars move from a real
audio level, behind an off-by-default switch that is the only place the System Audio Recording prompt can come from.

**Done when:** Items 1 to 5 and 7 are merged on `cart/desk-2-39`, item 6 is built and checked on its own
branch and held, and `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests`
pass on the integration branch. The owner then walks item 6, sees the prompt once (that is the spec's
"first, see the prompt" check), and the tap's PRIVACY and Settings copy is finalised from the real text.

**Out of scope:** the release (Info.plist version, appcast, cask), any push or PR, anything under
`windows-dotnet/`, art per growth stage, GIF/Aseprite import and pack export (backlog #9), triggering the
TCC prompt during the run, any live network in tests.

**Inferences:**
- Item 6 is `hold` (permissions surface). It alone may add `NSAudioCaptureUsageDescription` to
  `mac/Info.plist`, on its hold branch, for the owner's review; no other item touches Info.plist.
- The tap's PRIVACY, README and Settings copy lives on item 6's branch, not in item 7, because the spec
  writes it from the real prompt text.
- Where section 19 leaves "get_widgets or a field on an existing read tool" open, the worker picks the
  smaller surface and records it.
- What's New cards are added with version "2.39.0" before Info.plist moves.

**Mode:** unattended, chosen by the owner at 19:16 on the one confirmation. Auto mode is on.

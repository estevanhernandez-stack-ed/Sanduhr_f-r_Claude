# No-notch measurements (BACKLOG #6)

Measured 2026-10-19 on a MacBook Pro 13" 2019 (`MacBookPro15,4`, Intel, Touch Bar), macOS 15.8.1,
built-in display only, no notch. Sanduhr 2.28.0 release build, demo data on for every capture. The
screen runs at 1680 x 1050 pt (2x). Positions are in points from the screen's left edge.

## 1. Menu collision: the cap does not stop it, the hide does

Xcode frontmost (the widest menu row installed here), positions read through Accessibility:

| What | Left | Right | Width |
|---|---|---|---|
| Screen | 0 | 1680 | 1680 |
| Xcode's menu row (Apple through Help) | 10 | 737 | 727 |
| Pill, steady state (`notch_pill.frame`) | 753 | 926 | 173 |
| Pill, first reading after demo on | 709 | 971 | 262 |
| Status items, leftmost readable (Claude) | 1099 | 1680 | 581 |
| Pill at today's cap, 0.36 x 1680 = 605 | 538 | 1142 | 605 |

- **At 173 pt they miss by 16 pt** on the left and 173 pt on the right. Help ends almost at the
  pill's edge (`no-notch-measurements/xcode-menu-and-pill.png`).
- **At 262 pt the pill covers Xcode's Help by 28 pt.** The width follows the wing text, so a longer
  left wing (a meeting title) is enough. The 262 reading came from the first state read right
  after demo on; it settled to 173 and was not reproduced.
- **A centered pill meets an app menu whose right edge passes 840 - width/2**: 753 pt for 173,
  709 pt for 262, 538 pt at the cap. It meets the status items when its right edge passes 1099:
  at any width over 518 pt.
- **At the 0.36 cap the pill would cover 199 pt of Xcode's menus and 43 pt of status items.** The
  cap keeps room on a wide screen, but at 1680 pt it permits both collisions. What keeps everything
  reachable is the hide while the pointer is in the menu bar.
- Accessibility lists the status items owned by apps (Claude, Sanduhr, 1Password, Tailscale,
  Spotlight) but not Control Center's, which sit further right. The row here was not full. A
  fuller row moves 1099 left, about 30 to 40 pt per item.

Widest centered pill that clears, with an 8 pt margin:

| Clears | Width | Fraction of 1680 |
|---|---|---|
| Xcode's menus (737) | 190 | 0.113 |
| Status items (1099) | 502 | 0.30 |

**Recommendation: keep the hide, tighten the cap to 0.30, and yield to the app menu only where
Sanduhr can read its width.** At 0.30 the status side never collides on this screen, and today's
wing text (173 pt) never reaches the cap. On the app-menu side no cap works: 0.113 clears Xcode but
clips a meeting title. Yielding needs the frontmost app's menu frames, and those come only from
Accessibility (the window server draws all menu titles in one window). With Accessibility
trusted: when the app menu's right edge comes within 8 pt of the pill, drop the left wing to the
time alone. Without it: accept the overlap, since the hide already keeps reach.

## 2. Text size on a 24 pt bar: 10.5 pt type, digits about 8 pt tall

- The built-in bar on a no-notch MacBook is the 24 pt case: menu items are 24 pt tall and the
  pill's frame (the `visibleFrame` gap) is 25 pt. No external display was needed.
- Text size is `NotchWingsView.textSize`: `max(10, bar x 0.42)` = **10.5 pt**, half a point above
  the 10 pt floor. A notched MacBook's 37 pt bar gives 15.5 pt, so the pill's text is about
  two-thirds the size of the island's.
- Ink heights in a 2x capture (`no-notch-measurements/pill-text-2x.png`, 400 x 50 px):

| Run | Height (2x px) | Points |
|---|---|---|
| Time digits (`9:21`) | 14 | 7 |
| `42%`, `65%` | 17 | 8.5 |
| `5h` (ascender) | 18 | 9 |
| `wk` (ascender) | 22 | 11 |

The crop is native 2x; whether it reads at arm's length is a judgment for whoever sets the floor.
If it needs one, a 12 pt minimum for the pill alone scales the digits to about 8 to 10 pt and
widens the steady-state pill from 173 to about 198 pt, past the 190 pt that clears Xcode, so a
larger floor makes the app-menu side of section 1 tighter.

## 3. Camera badge: its own tab, drawn over the pill's center

Photo Booth running (macOS's green camera indicator on), pill on, no wings window:

- `av_indicators`: `camera: true`, `shown: badge`, `place: besideRight`.
- **The red dot draws in its own black tab at the top center, on top of the pill,** covering the
  session percentage (`5h 42%` reads `5h ●`). See
  `no-notch-measurements/camera-badge-over-pill.png`.
- Cause, as BACKLOG #6 says: `AVIndicatorViews.update` sets `islandUp` from `wingsWindow != nil`,
  and a pill-only Mac has no wings window, so `AVIndicatorPlacement.spot` returns `.badge` and
  `showBadge` centers the tab on the screen.
- **On this Mac the badge never shows unless a setting is changed.** `avCameraDotMode` unset
  resolves to `never`, and `hiddenLight` does not fire because the built-in camera has a visible
  light. The check set `avCameraDotMode` to `always`, restarted the Desk, and then deleted the
  key, so the setting is unset again. Any re-check after the fix needs the same step (or
  `smoke do av-test camera on` with the mode set).

## 4. CPU and memory: the Desk costs about 8 MB and no measurable CPU

`ps -o %cpu,rss` on the Sanduhr process once a minute. Same apps open in both runs (Xcode, VS Code,
Claude, Photo Booth closed), one made-up watcher (`smoke do watch-test start`), demo data off,
smoke debug hooks on for both. The Desk-off run stopped at 40 minutes, when Ice was installed and
launched, so its numbers cover 40 samples.

| Run | Samples | %CPU avg | %CPU peak | RSS avg | RSS peak | RSS min |
|---|---|---|---|---|---|---|
| Desk, pill and watcher on | 60 | 9.7 | 58.6 | 60 MB | 135 MB | 35 MB |
| Desk off | 40 | 10.5 | 61.3 | 53 MB | 103 MB | 29 MB |
| Desk on, first sample dropped | 59 | 8.9 | 15.6 | 59 MB | 128 MB | |
| Desk off, first sample dropped | 39 | 9.2 | 11.2 | 51 MB | 94 MB | |

- **Both peaks are the first sample**, taken right after a `smoke state` call (which renders the
  snapshot). Without it, the Desk adds about 8 MB of average RSS and no CPU: 8.9% on against 9.2%
  off is inside the minute-to-minute spread.
- **The baseline is the finding: about 9% of one core with the Desk off** on this 2019 quad-core
  i5. On macOS, `ps %cpu` is a decaying average, so this is sustained load, not a spike. The
  debug hooks were on in both runs and may account for part of it; a run with the hooks off
  (Activity Monitor or `top -pid`) would separate that out.
- RSS swings between 29 and 135 MB within an hour in both runs. That looks like memory
  compression and purging under the system's pressure, not growth: neither run trends upward.

## 5. Touch Bar: yes, and DFRFoundation exposes the Control Strip route

This Mac has a Touch Bar (`TouchBarServer` and `ControlStrip` running). Findings only: no app
code, nothing called at runtime.

- `otool -L` cannot open `/System/Library/PrivateFrameworks/DFRFoundation.framework/DFRFoundation`:
  since macOS 11 the binary lives only in the dyld shared cache. `xcrun dyld_info` reads it from
  there.
- Linked libraries: CoreGraphics, IOKit, IOSurface, QuartzCore, Security, ApplicationServices,
  ImageIO, Foundation, CoreFoundation, libobjc, libbsm, libSystem, and the private SkyLight.
- 152 exports. The ones a Control Strip item needs:
  - `DFRElementSetControlStripPresenceForIdentifier` and
    `DFRElementGetControlStripPresenceForIdentifier`: show or hide an item, by identifier, in the
    Control Strip (the always-visible right end).
  - `DFRSystemModalShowsCloseBoxWhenFrontMost`.
  - `DFRElementPresentSystemModal`, `DFRElementMinimizeSystemModal`,
    `DFRElementDismissSystemModal`: a full-width bar that takes over from the frontmost app.
  - `DFRControlStripInitialize`, `DFRControlStripSetExpanded`,
    `DFRControlStripRegisterPresenceHandler`, `DFRControlStripRegisterFocusChangeHandler`, and the
    `DFRPresentationMode*` family (the user's Touch Bar mode and Fn behavior).
- **The public story is `NSTouchBar`, and it only reaches the frontmost app.** A Touch Bar
  attached to a window or responder shows while Sanduhr is active. Sanduhr is a menu-bar app that
  is almost never frontmost, so the public API would rarely show anything.
- **The private route is the known one** from open-source Control Strip tools: AppKit's private
  `+[NSTouchBarItem addSystemTrayItem:]` and
  `+[NSTouchBar presentSystemModalTouchBar:placement:systemTrayItemIdentifier:]`, plus
  `DFRElementSetControlStripPresenceForIdentifier(identifier, true)` from DFRFoundation. Those
  AppKit selectors are not verified here: AppKit is also cache-only, and `dyld_info -objc` did not
  list them.
- What it would take: declare the private symbols (`dlsym` or a bridging header), a Control Strip
  item showing the meters, and handling for presence changes and the Fn modes. The costs: private
  API is grounds for App Store rejection (Sanduhr's Mac app ships outside the store, so that does
  not block it), it can break on any macOS update, and it serves one hardware line that Apple
  stopped making in 2023. A spike would gate it behind a default-off switch and
  `NSClassFromString`/`dlsym` checks so a missing symbol does nothing.

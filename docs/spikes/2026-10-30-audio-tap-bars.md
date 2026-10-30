# Spike: livelier now-playing bars on the Mac (Core Audio process tap)

Written spike only. No tap was created and no permission was requested; nothing here ran audio code.
Settled call 6 in `docs/mac-widgets-spec.md` asks whether a Core Audio process tap needs the System
Audio Recording permission and whether its prompt is acceptable. Section 14 of that spec holds the
two branches. SDK headers read: the Command Line Tools SDK (`/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk`,
`CoreAudio.framework/Headers`).

Verdict: **a tap needs the permission and there is no public per-app level without it. The bars are
buildable, opt-in, at about three to four days, but whether to ship them is the owner's call (last section).**

## What the bars do on Windows

`AudioBars` keeps 16 peak levels (0 to 1) at 20 frames a second: a higher peak is taken as is, a lower
one decays the smoothed value by 0.82, the stored value is its square root, and the bars are idle after
5 seconds under 0.004. `AudioBarMotion` eases each drawn bar (attack 0.55, decay 0.35, drop capped at 0.045
a frame) and rides a peak cap that holds 9 frames, then falls with gravity 0.004. Calm mode (Windows'
animations off) uses attack 0.3, falloff 0.02, no caps, five frames a second. The input is only the
playing app's session peak meter, chosen by `AudioSessionMatch`. Both types are pure and tested in
`AudioBarsTests.cs`, so the port is the logic plus a new level source.

## What the Mac knows today

`NowPlayingInfo` (`mac/Sources/Sanduhr/Services/NowPlaying.swift`, lines 105-106) already carries
`bundleID` and `pid` of the playing app, fed by the MediaRemote adapter (`docs/mac-now-playing-spike.md`).
`DeskNowPlaying.swift` draws a thin position bar, moved once a second by a `TimelineView` that exists
only while playing. So the app to listen to is known; only the level is missing.

## 1. Does a process tap need the permission?

Yes, by every source found, with one gap: the SDK headers say nothing about it.

- Header: `AudioHardwareTapping.h` lines 34-44 declare `AudioHardwareCreateProcessTap(CATapDescription*,
  AudioObjectID*)`, `API_AVAILABLE(macos(14.2))`. `CATapDescription.h` lines 42-97 describe the tap as a mix
  of the chosen processes' output (`initStereoMixdownOfProcesses:`, `initMonoMixdownOfProcesses:`, the
  `bundleIDs` and `processes` properties). A grep of `AudioHardwareTapping.h`, `CATapDescription.h` and
  `AudioHardware.h` for permission, privacy, TCC, authorization, consent and microphone finds nothing. The
  header documents no permission and no Info.plist key. That the tap needs consent is not in the SDK.
- Info.plist key: `NSAudioCaptureUsageDescription`. Source: the AudioCap sample's README (github.com/insidegui/AudioCap),
  which says the prompt text comes from that key and that Xcode's dropdown does not list it, so it is added by hand.
  Apple's article "Capturing system audio with Core Audio taps" could not be read here (the fetch returned
  only the title), so Apple's own wording on this is unconfirmed.
- TCC service: `kTCCServiceAudioCapture`. Source: AudioCap's `AudioRecordingPermission.swift`, which passes that
  string to the private `TCCAccessPreflight` and `TCCAccessRequest`. Confirmed in a third-party sample only.
- There is no public API to check or request this permission. AudioCap uses private TCC SPI behind a
  build flag; without it the prompt comes on first use. Private SPI is not an option for a Developer ID
  app we ship.
- Where it lives in System Settings: third-party write-ups place it under Privacy & Security, in the
  screen and system audio recording area, in a "system audio recording only" list. Exact current label on
  macOS 26 is unconfirmed; check on a live Mac before writing PRIVACY copy.

## 2. How the prompt reads, and what the app sees on deny

- Wording: the body is the app's own `NSAudioCaptureUsageDescription` string, so we write most of it. The
  surrounding system text (believed to be along the lines of "Sanduhr would like to record audio from
  other apps", with Allow and Don't Allow) is unconfirmed; nothing read here quotes it. Treat the exact
  wording as unknown until seen once on a live Mac.
- When it fires: third-party sources (catap docs, a dev.to write-up) say the prompt appears when the aggregate
  device holding the tap starts, not when the tap is created. The tap must sit in an aggregate device to
  deliver buffers at all. Unconfirmed against Apple.
- Once and deny: standard TCC behavior, unconfirmed for this service. A denial is remembered, the app is not
  asked again, and the user turns it on later in System Settings. catap's docs say a restart after granting may
  be needed.
- What the app sees on deny: no error. The same sources say the Core Audio calls return `noErr` and the tap
  delivers zeroed buffers (a Rust wrapper's docs say the same). So denied looks identical to silence. The
  app cannot tell the two apart without the private preflight, which means "permission missing" would
  have to be inferred from "audio is running (see 3) but every buffer is zero for N seconds". A forum
  thread reports all-zero buffers on a macOS 26.5 beta even with audio audible, so that inference can
  also misfire.
- Attribution: the grant belongs to the capturing app, so it is Sanduhr's entry, not the player's.

## 3. A per-app level without the permission

None found. Checked, with the precise status of each:

- `kAudioProcessPropertyIsRunning`, `IsRunningInput`, `IsRunningOutput` (`AudioHardware.h` lines 1957-1969):
  UInt32 booleans, "audio IO in progress". Not levels. Useful for one thing: knowing whether the playing
  app is actually producing output, and so telling a denied tap from a paused player. Also
  `kAudioProcessPropertyPID` and `BundleID` map a process object to the playing app (translate with
  `kAudioHardwarePropertyTranslatePIDToProcessObject`, line 634).
- `kAudioDevicePropertyVolumeScalar` (line 1045): the output device's gain, a user setting. Not the signal.
- MediaRemote (`docs/mac-now-playing-spike.md`): metadata, elapsed time and rate only. No level.
- `AVAudioEngine` output metering: meters the app's own engine. Sanduhr plays nothing.
- Windows' route, the per-app session peak meter, has no Mac public counterpart. That is the spec's premise
  and it holds.
- Not tested: anything private (for example per-app meters inside CoreAudio or `coreaudiod` SPI). Not worth
  a shipping dependency for a decorative feature.

So the only honest level source is the tap itself. Fake motion (bars driven by play state or a seeded
animation) is excluded by spec 14.

## 4. What building it would cost

Rough size: 3 to 4 days of work, about 600 lines of Swift plus 250 of tests, one new settings row.

- Switch: `nowPlaying.barsLevel`-style bool, off by default, under Now playing, with a `SettingsIndex`
  entry and a one-line explanation of the permission. Turning it on is the only moment a prompt can appear.
- Tap: `CATapDescription(stereoMixdownOfProcesses:)` for the playing app's process object only (from
  `NowPlayingInfo.pid`, with a re-tap when the pid changes), wrapped in a private aggregate device, an
  IOProc that computes one peak per callback and posts it to a lock-protected float. No buffer is kept,
  written, or sent. Tear down on pause, on switch off, and when the Desk is not showing.
- Add `NSAudioCaptureUsageDescription` to `mac/Info.plist` (needs the owner; workers are told not to
  touch it). Sparkle updates keep the grant only if the signing identity is stable; confirm.
- Port: `AudioBars` and `AudioBarMotion` map one to one (constants above), plus the 9-frame cap hold, the
  "Gradient bars" setting (`nowPlaying.barsGradient`, off) and a Reduce Motion read from
  `NSWorkspace.accessibilityDisplayShouldReduceMotion` driving the calm path. The Windows tests port to
  swift-testing nearly verbatim (about 25 cases, pure logic, no audio).
- Drawing: a `CAReplicatorLayer` of 16 bar layers, heights set from the stepped values at 20 fps only while
  a level is arriving. At idle (5 s under 0.004, or paused, or switch off) the timer stops and the
  replicator holds still, so the idle-CPU rule holds. Measure with cputime deltas, not `ps %cpu`
  (see the idle CPU note in memory).
- Denied state: show the existing position bar and a quiet "Allow in System Settings" line in the row
  when audio runs (`IsRunningOutput`) but buffers stay zero; never reprompt.
- Docs: a PRIVACY paragraph (the tap reads one app's output, keeps the peak in memory, stores and sends nothing, the
  permission is revocable in System Settings), a CHANGELOG line, and a live check on a Mac for the prompt text
  and Settings label.
- Risk: the denied-versus-silent inference (section 2), tap teardown races on track change, and bluetooth or
  aggregate-device format quirks. None is large; all need a live Mac, not CI.

## 5. Recommendation

Ship it only if the owner accepts a new System Audio Recording entry for Sanduhr. That is the same
Privacy & Security area that holds screen recording, and a tap by definition receives the app's audio,
even though we read one peak value and keep nothing. Because it is off by default and the prompt appears
only when the switch is turned on, the cost to people who never touch it is zero, and the cost to those who do is one
system dialog they chose. The risk is trust, not engineering: a usage-monitoring tool asking to "record
audio" reads badly on its own, and a denial is silent, so the fallback needs care. My lean is to ship
it as an opt-in in 2.38.0 only after the owner has seen the real prompt text once on a live Mac, and
otherwise keep the position bar and leave the backlog item open. Deciding question: is a
system-audio-recording permission, even opt-in and used for a single peak value, an acceptable ask of a
Sanduhr user for bars that are decoration?

Sources: `AudioHardwareTapping.h`, `CATapDescription.h`, `AudioHardware.h` (macOS SDK, CoreAudio headers);
github.com/insidegui/AudioCap (README and `AudioRecordingPermission.swift`); docs.rs `coreaudio` 0.3.x
`tap.rs`; PyPI `catap`; dev.to "2000 buffers of nothing"; Apple Developer Forums, Core Audio tag.

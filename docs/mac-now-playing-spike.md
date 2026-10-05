# Spike: now playing on the Mac (item 52)

Verdict: **ship, opt-in, with the MediaRemote adapter as the source and a quiet fallback.** The
system now-playing data is readable on macOS 26.6.2 (25G83), but only through an Apple-signed host
process. That is the same trick every notch app on 15.4+ uses, it is undocumented, and it can break
on any macOS update. The blast radius is a wing that shows nothing, so the risk is acceptable if
Sanduhr checks itself and falls back without noise.

Probes live in the session scratchpad (`nowplaying-spike/`), never in the app. They were run against
the adapter's silent test client, which publishes a fake track through `MPNowPlayingInfoCenter`; no
audio played.

## Findings per source

| Source | Read | Controls | Prompt | Browsers |
|---|---|---|---|---|
| MediaRemote from Sanduhr's own process | **No** | **Yes** | none | n/a |
| MediaRemote hosted in `/usr/bin/perl` (mediaremote-adapter) | **Yes** | Yes | none | Yes (needs a song to confirm) |
| MediaRemote via JXA in `/usr/bin/osascript` | **Yes** | Yes | none | same as above |
| Music / Spotify distributed notifications | Partial (push only) | No | none | No |
| AppleScript to Music / Spotify | Yes (not run) | Yes | Automation, per app | No |
| Public API (`MPNowPlayingInfoCenter`) | No | No | n/a | n/a |

**1. MediaRemote, direct.** `mr_probe` (ad-hoc signed) calls `MRMediaRemoteGetNowPlayingInfo`,
`...ApplicationPID` and `...ApplicationIsPlaying` while the test client is publishing: `nil`, `0`,
`false`. `mediaremoted` logs the client as `entitlements=0` and answers `Code=35 "Could not find the
specified now playing player"`, the same error it gives when nothing plays, so a denied app cannot
even tell it is denied. A Developer ID signature changes nothing: the gate is an Apple-private
entitlement that a third-party certificate cannot carry. (Not re-run with the team certificate: this
Mac has no signing identity.) Sending commands is not gated: `MRMediaRemoteSendCommand(pause)` from
the same ad-hoc binary paused the test client (rate 1 to 0).

**2. MediaRemote through an Apple-signed host.** The identical calls, compiled into a dylib and
loaded by `/usr/bin/perl`, return the test track: PID, title, artist, duration 600, elapsed, rate.
mediaremote-adapter (BSD-3, built here with clang, commit `2971825`) passed its own `test` command
(exit 0, 0.7 s). Its `stream` command pushes changes over stdout using MediaRemote's change
notifications, so no polling. The JXA route (`MRNowPlayingRequest.localNowPlayingItem` in
`osascript -l JavaScript`) also works, 76 ms per call, but polls and has no stream.
Fragility: the hole is "Apple's own binaries are trusted, and `perl` loads any dylib". Apple can
close it by adding library validation to perl, an entitlement check on the host, or removing perl
(deprecated as a bundled runtime since Catalina; still 5.34.1 here). The adapter's README badge says
tested on macOS 27.0 seed 26A5425a in September 2026, and its `test` command exists precisely so apps
can detect breakage and fall back.

**3. Distributed notifications.** Music posts `com.apple.Music.playerInfo` and Spotify posts
`com.spotify.client.PlaybackStateChanged` with name, artist, duration, position and state on every
change. No Apple Events, so no prompt. `dn_listen` compiles and runs; **needs a song** to show
payloads. Push only: no reading on launch, no controls, no browsers.

**4. AppleScript.** Music's dictionary has `current track`, `player position`, `player state`,
`playpause`, `next track`, `previous track`; Spotify's has the same shape (duration in ms). Probes
`as_music.applescript` / `as_spotify.applescript` are written, guarded with `is running` so they
never launch the player, and **not run**: the first call raises "Sanduhr wants access to control
Music" (one prompt per app, revocable in Privacy & Security, Automation). The app also needs
`NSAppleEventsUsageDescription` and, with hardened runtime, the `automation.apple-events`
entitlement. Spotify is not installed here.

**5. Browsers.** Chrome and Safari publish media-session playback (YouTube Music, Spotify Web) to
MediaRemote, so route 2 should see them exactly as the Windows mod sees the system media session.
**Needs a song** in Chrome to confirm. AppleScript into Chrome needs "Allow JavaScript from Apple
Events" plus scraping the page; not recommended.

**6. Public API.** None. `MPNowPlayingInfoCenter` and `MPRemoteCommandCenter` publish and handle the
calling app's own playback only; macOS 26 adds no reader or extension point for another app's track.

## Prior art

Boring Notch credits mediaremote-adapter for "the Now Playing source in macOS 15.4+"; AloeNotch and
MacNotch vendor it for Music, Spotify and YouTube in browsers; BetterTouchTool and Keyboard Maestro
users lost now playing on 15.4 until the adapter landed. Sources:
[mediaremote-adapter](https://github.com/ungive/mediaremote-adapter),
[Boring Notch](https://github.com/TheBoredTeam/boring.notch),
[AloeNotch](https://github.com/xnucade/AloeNotch),
[MacNotch](https://github.com/codewithkevin/macnotch),
[BTT thread](https://community.folivora.ai/t/now-playing-is-no-longer-working-on-macos-15-4/42802),
[Keyboard Maestro thread](https://forum.keyboardmaestro.com/t/beware-upgrading-to-macos-15-4-if-you-need-now-playing-data/40285),
[JXA gist](https://gist.github.com/SKaplanOfficial/f9f5bdd6455436203d0d318c078358de).

## Risks

- **Breakage on update** (high likelihood over time, low impact): run `test` at launch and after
  each OS update; on failure, fall back and show "Now playing isn't available on this macOS" in
  Settings.
- **Bundling**: ship `MediaRemoteAdapter.framework` and the test client inside `Sanduhr.app`,
  Developer ID signed and notarized; perl loading a Developer ID dylib is unverified here.
- **A long-lived perl child**: supervise it, restart with backoff, kill on quit.
- **14.x and 15.0 to 15.3**: direct MediaRemote works there; the adapter covers them too.

## Recommendation and sketch

Ship as an opt-in Desk feature, off by default:

- **Source**: adapter `stream` (no artwork first). If `test` fails, Music and Spotify notifications
  (no prompt) for the track and play state. AppleScript only if the user turns on "Control Music and
  Spotify when system now playing is unavailable", which is the one place a prompt appears, on that
  toggle, never by surprise.
- **Controls**: `MRMediaRemoteSendCommand` from Sanduhr's own process (works today, no host needed).
- **Notch**: a new `NotchContent.nowPlaying` for either wing: "Title · Artist", a play-state glyph,
  absent when nothing plays so the wing falls back to its other content. Click the wing for
  play/pause, two-finger click for previous/next.
- **Desk**: a small now-playing line under the meters with a thin position bar, updated locally
  from elapsed time and rate (no per-second IPC).
- **Settings, Desk, Now playing**: on/off, which apps (all, music apps only, exclude browsers), hide
  while paused, and the status line from the self-test.

Needs a song to finish: `run_with_song.sh` in the scratchpad runs every reading probe plus 15 s of
`stream` and the notification listener.

## Confirmed with a real player (2026-10-04)

YouTube Music in Chrome, on this macOS 26 Mac:

- **Direct MediaRemote from an ordinary process:** nothing (PID 0, no info), as expected.
- **Hosted in `/usr/bin/perl`, JXA in `osascript`, and mediaremote-adapter `get`:** title, artist, duration, elapsed time, play state and the source app (`com.google.Chrome`).
- **mediaremote-adapter `stream`:** pushes each change within a second or two: three skips arrived as three new titles, and a pause arrived as `playbackRate: 0`. No polling, no prompt.

Browser players are covered by the system-wide route, as on Windows, so it is the source for the feature (item 53), with the Music/Spotify notifications as the fallback when the adapter's self-test fails.

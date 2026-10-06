import Foundation
import Testing
@testable import Sanduhr

/// Now playing (item 53): the adapter's stream lines, the merged track and its live position, the
/// tracker's state machine, the restart policy, the fallback's mapping, the hide rules and the
/// app exclusion. Made-up tracks only; nothing here needs a player.

/// A stream line as the adapter prints it.
private func line(diff: Bool, _ payload: String) -> String {
    #"{"type":"data","diff":\#(diff),"payload":\#(payload)}"#
}

private let t0 = Date(timeIntervalSince1970: 1_791_133_000)

/// A full payload for a made-up song, as `stream --no-artwork --micros` prints it.
private func full(title: String = "Song A", artist: String = "Band", playing: Bool = true,
                  elapsedMicros: Int = 10_000_000, at: Date = t0, app: String = "com.google.Chrome") -> String {
    let stamp = Int(at.timeIntervalSince1970 * 1_000_000)
    return line(diff: false, """
    {"title":"\(title)","artist":"\(artist)","album":"Album","durationMicros":200000000,\
    "elapsedTimeMicros":\(elapsedMicros),"timestampEpochMicros":\(stamp),"playbackRate":\(playing ? 1 : 0),\
    "playing":\(playing),"bundleIdentifier":"\(app)","processIdentifier":4242,"contentItemIdentifier":"X"}
    """)
}

@Suite("Now playing stream")
struct NowPlayingStreamTests {
    @Test func parsesFullAndDiffLines() throws {
        let u = try #require(NowPlayingStream.parse(full()))
        #expect(u.diff == false)
        #expect(u.payload["title"] == .string("Song A"))
        #expect(u.payload["playing"] == .bool(true))
        #expect(u.payload["playbackRate"] == .number(1))
        let d = try #require(NowPlayingStream.parse(line(diff: true, #"{"playing":false,"album":null}"#)))
        #expect(d.diff)
        #expect(d.payload["playing"] == .bool(false))
        #expect(d.payload["album"] == .null)
    }

    @Test func malformedAndOtherLinesAreIgnored() {
        #expect(NowPlayingStream.parse("") == nil)
        #expect(NowPlayingStream.parse("   ") == nil)
        #expect(NowPlayingStream.parse("not json") == nil)
        #expect(NowPlayingStream.parse(#"{"type":"data","diff":false"#) == nil)
        #expect(NowPlayingStream.parse(#"{"type":"error","payload":{}}"#) == nil)
        #expect(NowPlayingStream.parse(#"{"type":"data","diff":false}"#) == nil)
        #expect(NowPlayingStream.parse(#"["data"]"#) == nil)
        // An empty full payload is a real update: nothing plays.
        #expect(NowPlayingStream.parse(line(diff: false, "{}")) == .init(diff: false, payload: [:]))
    }

    @Test func linesSplitAcrossChunks() {
        var buffer = Data()
        #expect(NowPlayingStream.lines(appending: Data("{\"a\":1}\n{\"b\"".utf8), to: &buffer) == ["{\"a\":1}"])
        #expect(NowPlayingStream.lines(appending: Data(":2}\n\n".utf8), to: &buffer) == ["{\"b\":2}", ""])
        #expect(buffer.isEmpty)
    }
}

@Suite("Now playing track")
struct NowPlayingInfoTests {
    func apply(_ lines: [String], at now: Date = t0) -> NowPlayingInfo {
        var info = NowPlayingInfo()
        for l in lines { if let u = NowPlayingStream.parse(l) { info.apply(u, receivedAt: now) } }
        return info
    }

    @Test func fullPayloadFillsTheTrack() {
        let info = apply([full()])
        #expect(info.title == "Song A")
        #expect(info.artist == "Band")
        #expect(info.duration == 200)
        #expect(info.elapsed == 10)
        #expect(info.elapsedAt == t0)
        #expect(info.bundleID == "com.google.Chrome")
        #expect(info.pid == 4242)
        #expect(info.state == .playing)
    }

    @Test func diffsMergeAndNullRemoves() {
        let info = apply([full(), line(diff: true, #"{"artist":"Other","album":null}"#)])
        #expect(info.title == "Song A")
        #expect(info.artist == "Other")
        #expect(info.album == nil)
    }

    @Test func aFullPayloadReplacesEverything() {
        let info = apply([full(), line(diff: false, #"{"title":"Song B"}"#)])
        #expect(info.title == "Song B")
        #expect(info.artist == nil)
        #expect(info.duration == nil)
        #expect(apply([full(), line(diff: false, "{}")]).state == .none)
    }

    @Test func stateFromPlayingOrRate() {
        #expect(NowPlayingInfo().state == .none)
        #expect(NowPlayingInfo(title: "  ", playing: true).state == .none)
        #expect(NowPlayingInfo(title: "x", playing: false).state == .paused)
        #expect(NowPlayingInfo(title: "x", rate: 1).state == .playing)
        #expect(NowPlayingInfo(title: "x", rate: 0).state == .paused)
        #expect(NowPlayingInfo(title: "x").state == .paused)
    }

    @Test func plainSecondsAndISOTimestampsToo() {
        let info = apply([line(diff: false, #"{"title":"x","duration":100.5,"elapsedTime":3,"timestamp":"2026-10-04T17:03:45Z","playing":true}"#)])
        #expect(info.duration == 100.5)
        #expect(info.elapsed == 3)
        #expect(info.elapsedAt == ISO8601DateFormatter().date(from: "2026-10-04T17:03:45Z"))
    }

    @Test func livePositionRunsWhilePlayingAndStopsAtTheEnd() {
        let info = apply([full()])
        #expect(info.position(at: t0) == 10)
        #expect(info.position(at: t0.addingTimeInterval(5)) == 15)
        #expect(info.position(at: t0.addingTimeInterval(1000)) == 200)
        // A clock that reads before the stamp never moves it back.
        #expect(info.position(at: t0.addingTimeInterval(-5)) == 10)
        #expect(info.progress(at: t0.addingTimeInterval(90)) == 0.5)
        let paused = apply([full(playing: false)])
        #expect(paused.position(at: t0.addingTimeInterval(60)) == 10)
        #expect(NowPlayingInfo(title: "x").position(at: t0) == nil)
        #expect(NowPlayingInfo(title: "x", elapsed: 5).progress(at: t0) == nil)
    }

    @Test func pauseWithoutANewElapsedTimeKeepsThePosition() throws {
        var info = apply([full()])
        // The adapter sends "playing":false first, the new elapsed time a moment later.
        let pause = try #require(NowPlayingStream.parse(line(diff: true, #"{"playing":false}"#)))
        info.apply(pause, receivedAt: t0.addingTimeInterval(4))
        #expect(info.state == .paused)
        #expect(info.position(at: t0.addingTimeInterval(30)) == 14)
        let resume = try #require(NowPlayingStream.parse(line(diff: true, #"{"playing":true}"#)))
        info.apply(resume, receivedAt: t0.addingTimeInterval(30))
        #expect(info.position(at: t0.addingTimeInterval(32)) == 16)
    }
}

@Suite("Now playing tracker")
struct NowPlayingTrackerTests {
    func update(_ l: String) -> NowPlayingStream.Update { NowPlayingStream.parse(l)! }

    @Test func playingPausedStopped() {
        var t = NowPlayingTracker()
        #expect(t.state == .none)
        #expect(t.receive(update(full()), now: t0) == nil)
        #expect(t.state == .playing)
        #expect(t.shown?.title == "Song A")
        t.receive(update(line(diff: true, #"{"playing":false,"playbackRate":0}"#)), now: t0)
        #expect(t.state == .paused)
        // Stopped: the player clears now playing. It counts after the delay.
        let wait = t.receive(update(line(diff: false, "{}")), now: t0.addingTimeInterval(1))
        #expect(wait == NowPlayingTracker.clearDelay)
        #expect(t.state == .paused)
        #expect(t.settle(now: t0.addingTimeInterval(2)) != nil)
        #expect(t.settle(now: t0.addingTimeInterval(2.6)) == nil)
        #expect(t.state == .none)
        #expect(t.shown == nil)
    }

    @Test func aSkipDoesNotBlink() {
        var t = NowPlayingTracker()
        t.receive(update(full(title: "Song A")), now: t0)
        // Between tracks: an empty payload, then the next song half a second later.
        #expect(t.receive(update(line(diff: false, "{}")), now: t0.addingTimeInterval(1)) != nil)
        #expect(t.shown?.title == "Song A")
        #expect(t.receive(update(full(title: "Song B")), now: t0.addingTimeInterval(1.5)) == nil)
        #expect(t.shown?.title == "Song B")
        #expect(t.clearingSince == nil)
        // A late settle from the first clear changes nothing.
        #expect(t.settle(now: t0.addingTimeInterval(3)) == nil)
        #expect(t.shown?.title == "Song B")
    }

    @Test func sourceChangesAndReplace() {
        var t = NowPlayingTracker()
        t.receive(update(full(app: "com.google.Chrome")), now: t0)
        t.receive(update(full(title: "Track", app: "com.apple.Music")), now: t0)
        #expect(t.shown?.bundleID == "com.apple.Music")
        // The fallback hands over whole tracks.
        t.replace(NowPlayingInfo(title: "Spot", playing: true, bundleID: "com.spotify.client"), now: t0)
        #expect(t.shown?.bundleID == "com.spotify.client")
        #expect(t.state == .playing)
        t.reset()
        #expect(t == NowPlayingTracker())
    }

    @Test func nothingToClearWhenNothingShowed() {
        var t = NowPlayingTracker()
        #expect(t.receive(update(line(diff: false, "{}")), now: t0) == nil)
        #expect(t.clearingSince == nil)
    }
}

@Suite("Now playing supervisor")
struct NowPlayingSupervisorTests {
    @Test func quickExitsBackOffThenFallBack() {
        var s = NowPlayingSupervisor()
        #expect(s.streamExited(ranFor: 1) == .restart(after: 1))
        #expect(s.streamExited(ranFor: 1) == .restart(after: 2))
        #expect(s.streamExited(ranFor: 1) == .restart(after: 4))
        #expect(s.streamExited(ranFor: 1) == .restart(after: 8))
        #expect(s.streamExited(ranFor: 1) == .fallback)
    }

    @Test func aHealthyRunStartsTheCountAgain() {
        var s = NowPlayingSupervisor()
        _ = s.streamExited(ranFor: 1)
        _ = s.streamExited(ranFor: 1)
        #expect(s.streamExited(ranFor: 600) == .restart(after: 1))
        s.reset()
        #expect(s.quickExits == 0)
    }

    @Test func backoffIsCapped() {
        #expect(NowPlayingSupervisor.backoff(1) == 1)
        #expect(NowPlayingSupervisor.backoff(0) == 1)
        #expect(NowPlayingSupervisor.backoff(20) == NowPlayingSupervisor.maxBackoff)
    }
}

@Suite("Now playing fallback")
struct NowPlayingFallbackTests {
    @Test func musicNotification() {
        let info = NowPlayingFallback.info(from: ["Name": "Tune", "Artist": "Singer", "Album": "LP",
                                                  "Total Time": NSNumber(value: 180_000), "Player State": "Playing"],
                                           app: NowPlayingFallback.music, now: t0)
        #expect(info.title == "Tune")
        #expect(info.artist == "Singer")
        #expect(info.duration == 180)
        #expect(info.state == .playing)
        #expect(info.bundleID == "com.apple.Music")
        #expect(info.elapsed == nil)
    }

    @Test func spotifyNotificationWithPosition() {
        let info = NowPlayingFallback.info(from: ["Name": "Tune", "Artist": "Singer", "Duration": NSNumber(value: 200_000),
                                                  "Playback Position": NSNumber(value: 12.5), "Player State": "Paused"],
                                           app: NowPlayingFallback.spotify, now: t0)
        #expect(info.duration == 200)
        #expect(info.elapsed == 12.5)
        #expect(info.elapsedAt == t0)
        #expect(info.state == .paused)
    }

    @Test func stoppedOrUnknownIsNothing() {
        #expect(NowPlayingFallback.info(from: ["Name": "Tune", "Player State": "Stopped"], app: "x", now: t0).state == .none)
        #expect(NowPlayingFallback.info(from: [:], app: "x", now: t0).state == .none)
    }

    @Test func scriptAnswers() {
        let ok = NowPlayingFallback.info(fromScript: "playing\nTune\nSinger\nLP\n180,5\n12.25\n",
                                         app: NowPlayingFallback.music, now: t0)
        #expect(ok.state == .playing)
        #expect(ok.title == "Tune")
        #expect(ok.duration == 180.5)
        #expect(ok.elapsed == 12.25)
        #expect(NowPlayingFallback.info(fromScript: "stopped\n", app: "x", now: t0).state == .none)
        #expect(NowPlayingFallback.info(fromScript: "", app: "x", now: t0).state == .none)
        #expect(NowPlayingFallback.info(fromScript: "weird\na\nb\nc\nd\ne", app: "x", now: t0).state == .none)
    }

    @Test func scriptNeverLaunchesTheApp() {
        for app in [NowPlayingFallback.music, NowPlayingFallback.spotify] {
            let s = NowPlayingFallback.script(for: app)
            #expect(s.hasPrefix("if application id \"\(app)\" is running then"))
        }
        #expect(NowPlayingFallback.script(for: NowPlayingFallback.spotify).contains("/ 1000"))
        #expect(!NowPlayingFallback.script(for: NowPlayingFallback.music).contains("/ 1000"))
    }

    @Test func statusLines() {
        func text(_ s: NowPlayingSource, placed: Bool = true, desk: Bool = true, checking: Bool = false,
                  failed: Bool = false) -> String {
            NowPlayingStatus.text(source: s, placed: placed, deskRunning: desk, checking: checking, adapterFailed: failed)
        }
        #expect(text(.adapter) == "Adapter working")
        #expect(text(.fallback) == "Fallback (Music and Spotify only)")
        #expect(text(.fallback, failed: true).hasPrefix("Fallback (Music and Spotify only): "))
        #expect(text(.off) == "Off")
        #expect(text(.adapter, placed: false) == "Not placed anywhere")
        #expect(text(.off, placed: false, desk: false) == "Not placed anywhere")
        #expect(text(.off, desk: false) == "Off (needs Desk)")
        #expect(text(.off, checking: true) == "Checking…")
    }
}

@Suite("Now playing hide rules and apps")
struct NowPlayingVisibilityTests {
    let playing = NowPlayingInfo(title: "Tune", artist: "Singer", playing: true, bundleID: "com.google.Chrome")
    var paused: NowPlayingInfo { var p = playing; p.playing = false; return p }

    @Test func nothingPlayingNeverShows() {
        let prefs = NowPlayingPrefs()
        #expect(prefs.visible(nil) == nil)
        #expect(prefs.visible(NowPlayingInfo()) == nil)
        #expect(prefs.visible(playing) == playing)
        #expect(prefs.visible(paused) == paused)
    }

    @Test func hideWhilePaused() {
        let prefs = NowPlayingPrefs(hideWhilePaused: true)
        #expect(prefs.visible(paused) == nil)
        #expect(prefs.visible(playing) == playing)
    }

    @Test func excludedAppsNeverShow() {
        let prefs = NowPlayingPrefs(excluded: ["com.google.Chrome"])
        #expect(prefs.visible(playing) == nil)
        var music = playing
        music.bundleID = "com.apple.Music"
        #expect(prefs.visible(music) == music)
        // No app known: not excluded.
        var unknown = playing
        unknown.bundleID = nil
        #expect(prefs.visible(unknown) == unknown)
    }

    @Test func savedPrefsAndDefaults() {
        let d = UserDefaults(suiteName: "sanduhr.tests.nowplaying.\(UUID().uuidString)")!
        #expect(NowPlayingPrefs.saved(in: d) == NowPlayingPrefs())
        d.set(true, forKey: NowPlayingPrefs.enabledKey)
        d.set(true, forKey: NowPlayingPrefs.hidePausedKey)
        d.set(["a", "b"], forKey: NowPlayingPrefs.excludedKey)
        d.set(false, forKey: NowPlayingPrefs.deskLineKey)
        d.set(true, forKey: NowPlayingPrefs.askAppsKey)
        // Item 53's switch and line no longer count here (NowPlayingPlacement.upgrade reads them once).
        #expect(NowPlayingPrefs.saved(in: d) == NowPlayingPrefs(hideWhilePaused: true, excluded: ["a", "b"], askApps: true))
    }

    @Test func seenAppsInOrderWithoutRepeats() {
        var seen: [String] = []
        seen = NowPlayingApps.seen(seen, adding: "com.google.Chrome")
        seen = NowPlayingApps.seen(seen, adding: "com.apple.Music")
        seen = NowPlayingApps.seen(seen, adding: "com.google.Chrome")
        seen = NowPlayingApps.seen(seen, adding: nil)
        seen = NowPlayingApps.seen(seen, adding: "")
        #expect(seen == ["com.google.Chrome", "com.apple.Music"])
        #expect(NowPlayingApps.rows(seen: seen, excluded: ["z.app", "com.apple.Music", "a.app"])
                == ["com.google.Chrome", "com.apple.Music", "a.app", "z.app"])
    }
}

@Suite("Now playing text")
struct NowPlayingTextTests {
    let song = NowPlayingInfo(title: "Breathe (In the Air)", artist: "Pink Floyd", playing: true)

    @Test func titleArtistAndGlyph() {
        #expect(NowPlayingText.line(song, at: .left) == "\(NowPlayingText.playingGlyph) Breathe (In the Air) · Pink Floyd")
        var paused = song
        paused.playing = false
        #expect(NowPlayingText.line(paused, at: .right)?.hasPrefix(NowPlayingText.pausedGlyph) == true)
        var noArtist = song
        noArtist.artist = " "
        #expect(NowPlayingText.line(noArtist, at: .strip) == "\(NowPlayingText.playingGlyph) Breathe (In the Air)")
        #expect(NowPlayingText.line(nil, at: .left) == nil)
        #expect(NowPlayingText.line(NowPlayingInfo(), at: .left) == nil)
    }

    @Test func everyPlaceGetsTheWholeLine() {
        // Item 53b: a wing no longer clips; a title too long for it scrolls through once.
        let long = NowPlayingInfo(title: "Shine On You Crazy Diamond (Parts I-V)", artist: "Pink Floyd and Friends", playing: true)
        let whole = "\(NowPlayingText.playingGlyph) Shine On You Crazy Diamond (Parts I-V) · Pink Floyd and Friends"
        #expect(NowPlayingText.line(long, at: .left) == whole)
        #expect(NowPlayingText.line(long, at: .right) == whole)
        #expect(NowPlayingText.line(long, at: .strip) == whole)
        #expect(NowPlayingText.desk(long) == whole)
    }

    @Test func notchChoiceShowsNowPlaying() {
        let now = t0
        #expect(NotchContent.nowPlaying.text(at: .left, meetings: [], meters: "5h 7%", message: nil,
                                             nowPlaying: song, now: now) == NowPlayingText.line(song, at: .left))
        #expect(NotchContent.nowPlaying.text(at: .right, meetings: [], meters: "5h 7%", message: "hi",
                                             nowPlaying: nil, now: now) == nil)
        #expect(NotchContent.nowPlaying.label == "Now playing")
        #expect(NotchContent.resolve(.right, raw: "nowPlaying") == .nowPlaying)
    }
}

@Suite("Now playing on the Desk")
struct DeskNowPlayingTests {
    @Test func theGapIsTheColumnSpacingAndThePadding() {
        #expect(DeskNowPlaying.columnSpacing + DeskNowPlaying.padding == DeskNowPlaying.gap)
        #expect(DeskNowPlaying.gap > DeskHitTest.slack(.meters).height + DeskHitTest.slack(.nowPlaying).height)
    }

    @Test func theStripNeedsEverySwitch() {
        #expect(DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 26, chinText: true, strip: .nowPlaying, hasTrack: true))
        #expect(!DeskNowPlaying.stripShows(notch: false, hasNotch: true, chin: 26, chinText: true, strip: .nowPlaying, hasTrack: true))
        #expect(!DeskNowPlaying.stripShows(notch: true, hasNotch: false, chin: 26, chinText: true, strip: .nowPlaying, hasTrack: true))
        #expect(!DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 0, chinText: true, strip: .nowPlaying, hasTrack: true))
        #expect(!DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 26, chinText: false, strip: .nowPlaying, hasTrack: true))
        #expect(!DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 26, chinText: true, strip: .meters, hasTrack: true))
        #expect(!DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 26, chinText: true, strip: .nowPlaying, hasTrack: false))
    }

    @Test func elementsListTheLineAndTheStrip() {
        var input = DeskElements.Input()
        input.placed = ["meters"]
        input.meterTiers = [.fiveHour]
        input.nowPlayingLine = true
        input.nowPlayingFrame = CGRect(x: 52, y: 820, width: 360, height: 30)
        input.nowPlayingStrip = true
        input.stripFrame = CGRect(x: 700, y: 38, width: 200, height: 26)
        let out = DeskElements.build(input)
        #expect(out.suffix(2) == [DeskElement(kind: .nowPlaying, key: "desk", frame: input.nowPlayingFrame),
                                  DeskElement(kind: .nowPlaying, key: "strip", frame: input.stripFrame)])
        input.nowPlayingLine = false
        input.nowPlayingStrip = false
        #expect(!DeskElements.build(input).contains { $0.kind == .nowPlaying })
    }

    @Test func clicksAndTheTwoFingerMenuFindIt() {
        let meters = DeskElement(kind: .meters, frame: CGRect(x: 52, y: 700, width: 360, height: 100))
        let line = DeskElement(kind: .nowPlaying, key: "desk", frame: CGRect(x: 52, y: 800 + DeskNowPlaying.gap, width: 360, height: 30))
        let hit = DeskHitTest.element(at: CGPoint(x: 100, y: 830), in: [meters, line])
        #expect(hit == line)
        #expect(DeskHitTest.hasMenu(hit))
        #expect(!DeskHitTest.isMeters(hit))
        #expect(DeskHitTest.hasMenu(meters))
        #expect(!DeskHitTest.hasMenu(DeskElement(kind: .account, frame: .zero)))
        #expect(!DeskHitTest.hasMenu(nil))
        #expect(DeskHitTest.priority.contains(.nowPlaying))
    }

    @Test func theGapKeepsTheFramesValid() {
        let window = CGSize(width: 1512, height: 982)
        let meters = DeskElement(kind: .meters, frame: CGRect(x: 52, y: 700, width: 360, height: 100))
        let line = DeskElement(kind: .nowPlaying, key: "desk", frame: CGRect(x: 52, y: 800 + DeskNowPlaying.gap, width: 360, height: 30))
        #expect(DeskFrameCheck.problem([meters, line], window: window) == nil)
        // Tight under the meters, their click areas would overlap.
        let tight = DeskElement(kind: .nowPlaying, key: "desk", frame: CGRect(x: 52, y: 804, width: 360, height: 30))
        #expect(DeskFrameCheck.problem([meters, tight], window: window) == "meters overlaps now_playing desk")
        #expect(DeskFrameCheck.name(line) == "now_playing desk")
    }
}

@Suite("Now playing state.yaml")
struct NowPlayingDebugTests {
    @Test func flagsOnlyNeverWhatPlays() {
        var s = DebugStateInput()
        s.nowPlaying = NowPlayingDebug(enabled: true, placed: [.wingRight, .desk], source: .adapter, state: .playing)
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("camera_light: false\nnow_playing:\n  enabled: true\n  placed:\n    - wing_right\n    - desk\n  source: adapter\n  state: playing\nnow_playing_idle: automatic\n"))
        let off = YAMLEmitter.emit(DebugState.yaml(DebugStateInput()))
        #expect(off.contains("now_playing:\n  enabled: false\n  placed: []\n  source: \"off\"\n  state: none\n"))
    }
}

@Suite("Now playing placement")
struct NowPlayingPlacementTests {
    typealias P = NowPlayingPlacement

    @Test func placedNowhereByDefault() {
        #expect(P.places(P.Input()).isEmpty)
        #expect(P.saved(in: MemoryDefaults()).isEmpty)
        #expect(!DeskLayout.placed(DeskLayout.standard).contains(P.widget))
    }

    @Test func eachPlaceCounts() {
        var i = P.Input()
        i.notch = true
        i.left = .nowPlaying
        #expect(P.places(i) == [.wingLeft])
        i.right = .nowPlaying
        #expect(P.places(i) == [.wingLeft, .wingRight])
        i.chinText = true
        i.strip = .nowPlaying
        #expect(P.places(i) == [.wingLeft, .wingRight, .strip])
        i.layout = "clock:bl meters:bl nowPlaying:bl"
        #expect(P.places(i) == [.wingLeft, .wingRight, .strip, .desk])
    }

    @Test func aChoiceThatCannotShowDoesNotCount() {
        var i = P.Input()
        i.left = .nowPlaying
        i.chinText = true
        i.strip = .nowPlaying
        // The island off: the wings and the strip are not drawn.
        #expect(P.places(i).isEmpty)
        i.notch = true
        i.wingText = false
        i.chin = 0
        #expect(P.places(i).isEmpty)
        // A corner the Desk does not know.
        i.layout = "nowPlaying:xx"
        #expect(P.places(i).isEmpty)
    }

    @Test func placementImpliesRunningWithDesk() {
        #expect(P.isRunning(deskRunning: true, places: [.desk]))
        #expect(P.isRunning(deskRunning: true, places: [.wingRight]))
        #expect(!P.isRunning(deskRunning: true, places: []))
        #expect(!P.isRunning(deskRunning: false, places: [.wingLeft, .desk]))
    }

    @Test func savedSettings() {
        let d = MemoryDefaults()
        d.set(true, forKey: DeskController.notchKey)
        d.set("nowPlaying", forKey: NotchContent.Place.right.key)
        d.set("message:tl nowPlaying:br", forKey: "layout")
        #expect(P.saved(in: d) == [.wingRight, .desk])
        d.set(false, forKey: "notchText")
        #expect(P.saved(in: d) == [.desk])
        // Item 53's switch on its own places nothing.
        let old = MemoryDefaults()
        old.set(true, forKey: NowPlayingPrefs.enabledKey)
        #expect(P.saved(in: old).isEmpty)
    }

    @Test func upgradePlacesTheLineWhereItWas() {
        // Under the meters, in their corner, the rest of the layout untouched.
        #expect(P.upgradedLayout("meetings:bl meters:bl message:tl", enabled: true, deskLine: true, showClaude: true)
                == "meetings:bl meters:bl nowPlaying:bl message:tl")
        // Meters off the desktop: under the Claude line.
        #expect(P.upgradedLayout(DeskLayout.standard, enabled: true, deskLine: true, showClaude: true)
                == "message:tl clock:bl claude:bl nowPlaying:bl meetings:bl")
        #expect(P.upgradedLayout("claude:tr meters:br", enabled: true, deskLine: true, showClaude: true)
                == "claude:tr meters:br nowPlaying:br")
    }

    @Test func upgradeLeavesTheLayoutAlone() {
        let layout = "clock:bl meters:bl"
        #expect(P.upgradedLayout(layout, enabled: false, deskLine: true, showClaude: true) == nil)
        #expect(P.upgradedLayout(layout, enabled: true, deskLine: false, showClaude: true) == nil)
        #expect(P.upgradedLayout("clock:bl meters:bl nowPlaying:tr", enabled: true, deskLine: true, showClaude: true) == nil)
        // Nothing for the line to sit under: it never showed.
        #expect(P.upgradedLayout("clock:bl message:tl", enabled: true, deskLine: true, showClaude: true) == nil)
        #expect(P.upgradedLayout(layout, enabled: true, deskLine: true, showClaude: false) == nil)
    }

    @Test func upgradeRunsOnce() {
        let d = MemoryDefaults()
        d.set(true, forKey: NowPlayingPrefs.enabledKey)
        d.set("clock:bl meters:bl", forKey: "layout")
        P.upgrade(d)
        #expect(d.values["layout"] as? String == "clock:bl meters:bl nowPlaying:bl")
        #expect(d.bool(forKey: P.upgradedKey))
        // Moved to Hidden afterwards: the old switch does not bring it back.
        d.set("clock:bl meters:bl", forKey: "layout")
        P.upgrade(d)
        #expect(d.values["layout"] as? String == "clock:bl meters:bl")
    }

    @Test func upgradeWithNoSavedLayoutUsesTheStandardOne() {
        let d = MemoryDefaults()
        d.set(true, forKey: NowPlayingPrefs.enabledKey)
        P.upgrade(d)
        #expect(d.values["layout"] as? String == "message:tl clock:bl claude:bl nowPlaying:bl meetings:bl")
        // A fresh install: nothing saved, nothing placed, only the flag.
        let fresh = MemoryDefaults()
        P.upgrade(fresh)
        #expect(fresh.values.keys.sorted() == [P.upgradedKey])
    }

    @Test func theLayoutPickerKnowsTheElement() {
        #expect(DeskLayout.widgets.contains { $0.key == P.widget && $0.name == "Now playing" })
        #expect(DeskLayout.placing(P.widget, in: "bl", layout: "meetings:bl meters:bl")
                == "meters:bl nowPlaying:bl meetings:bl")
        #expect(DeskLayout.placing(P.widget, in: "", layout: "meters:bl nowPlaying:bl") == "meters:bl")
    }

    @Test func theDeskElementNeedsThePlacementAndATrack() {
        var input = DeskElements.Input()
        input.placed = DeskLayout.placed("meters:bl nowPlaying:bl")
        input.nowPlayingLine = input.placed.contains(P.widget)
        input.nowPlayingFrame = CGRect(x: 52, y: 820, width: 360, height: 30)
        #expect(DeskElements.build(input).contains { $0.kind == .nowPlaying && $0.key == "desk" })
    }
}

@Suite("Now playing scroll")
struct NowPlayingScrollTests {
    typealias S = NowPlayingScroll

    @Test func aTitleThatFitsNeverMoves() {
        #expect(S.plan(textWidth: 120, room: 158, reduceMotion: false) == nil)
        #expect(S.plan(textWidth: 158, room: 158, reduceMotion: false) == nil)
        // Measuring and drawing may differ by a fraction of a point.
        #expect(S.plan(textWidth: 158.8, room: 158, reduceMotion: false) == nil)
        #expect(S.fits(textWidth: 158.8, room: 158))
        #expect(!S.fits(textWidth: 160, room: 158))
    }

    @Test func aLongTitleScrollsOnceAtEighteenPointsASecond() throws {
        let plan = try #require(S.plan(textWidth: 400, room: 158, reduceMotion: false))
        // Until the end clears the fade at the trailing edge.
        #expect(plan.distance == 400 - 158 + S.fade)
        #expect(abs(plan.travel - Double(plan.distance) / 18) < 0.0001)
        #expect(S.speed == 18)
        // A pause at the start and the end, then back to the beginning, and that's all.
        #expect(abs(plan.total - (S.startPause + plan.travel + S.endPause + S.returnDuration)) < 0.0001)
        #expect(S.startPause >= 1 && S.endPause >= 0.5 && S.returnDuration < 1)
    }

    @Test func reduceMotionOrNoRoomNeverScrolls() {
        #expect(S.plan(textWidth: 400, room: 158, reduceMotion: true) == nil)
        #expect(S.plan(textWidth: 400, room: 0, reduceMotion: false) == nil)
    }

    @Test func aNewTrackOrPlayingAgainScrollsAPauseHoldsStill() {
        let song = NowPlayingInfo(title: "Tune", artist: "Singer", playing: true)
        var paused = song
        paused.playing = false
        var next = song
        next.title = "Other Tune"
        // Pausing changes the key (the title goes back to its beginning and holds still), and
        // playing again changes it back, so the scroll runs once more.
        #expect(S.trackKey(song) != S.trackKey(paused))
        #expect(S.scrolls(song) && !S.scrolls(paused) && !S.scrolls(nil))
        #expect(S.trackKey(song) != S.trackKey(next))
        var otherArtist = song
        otherArtist.artist = "Someone"
        #expect(S.trackKey(song) != S.trackKey(otherArtist))
        #expect(S.trackKey(nil) == "")
    }
}

@Suite("Now playing while paused")
struct NowPlayingPausedLayoutTests {
    typealias L = NowPlayingWingLayout

    @Test func nextShowsOnlyWhilePaused() {
        #expect(L.showsNext(.paused))
        #expect(!L.showsNext(.playing))
        #expect(!L.showsNext(NowPlayingState.none))
        #expect(!L.showsNext(nil))
        #expect(L.nextSide(.left, state: .playing) == nil)
        #expect(L.nextRoom(.right, state: .playing, size: 16) == 0)
    }

    @Test func nextSitsAtTheOuterEdge() {
        // Away from the camera: the left wing's left, the right wing's right; the strip's end.
        #expect(L.nextSide(.left, state: .paused) == .leading)
        #expect(L.nextSide(.right, state: .paused) == .trailing)
        #expect(L.nextSide(.strip, state: .paused) == .trailing)
    }

    @Test func theTitleKeepsTheRestOfTheWing() {
        let size: CGFloat = 16
        let next = L.nextWidth(size) + L.wingSpacing
        #expect(L.textRoom(.right, width: 180, state: .playing, size: size) == 180 - L.wingInsets)
        #expect(L.textRoom(.right, width: 180, state: .paused, size: size) == 180 - L.wingInsets - next)
        #expect(L.textRoom(.left, width: 180, state: .paused, size: size) == 180 - L.wingInsets - next)
        #expect(L.textRoom(.left, width: 10, state: .paused, size: size) == 0)
        let stripNext = L.nextWidth(size) + L.stripSpacing
        #expect(L.textRoom(.strip, width: 400, state: .paused, size: size) == 400 - 36 - stripNext)
    }

    @Test func aPausedWingGrowsForTheButtonUpToTheLimit() {
        let size: CGFloat = 16
        let playing = L.wingWidth(textWidth: 60, place: .right, state: .playing, size: size, minimum: 36, maximum: 180)
        let paused = L.wingWidth(textWidth: 60, place: .right, state: .paused, size: size, minimum: 36, maximum: 180)
        #expect(playing == 60 + L.wingInsets)
        #expect(paused == playing + L.nextWidth(size) + L.wingSpacing)
        #expect(L.wingWidth(textWidth: 400, place: .left, state: .paused, size: size, minimum: 36, maximum: 180) == 180)
        // Other content (no state) never gets the room.
        #expect(L.wingWidth(textWidth: 0, place: .left, state: nil, size: size, minimum: 36, maximum: 180) == 36)
    }

    @Test func theStripButtonIsItsOwnClickArea() {
        var input = DeskElements.Input()
        input.nowPlayingStrip = true
        input.stripFrame = CGRect(x: 640, y: 38, width: 200, height: 26)
        input.nowPlayingStripNext = true
        let nextX = input.stripFrame.maxX + L.stripSpacing
        input.stripNextFrame = CGRect(x: nextX, y: 38, width: L.nextWidth(14), height: 26)
        let out = DeskElements.build(input)
        let next = DeskElement(kind: .nowPlayingNext, key: "strip", frame: input.stripNextFrame)
        #expect(out.last == next)
        #expect(DeskHitTest.element(at: CGPoint(x: nextX + 5, y: 50), in: out) == next)
        #expect(DeskHitTest.element(at: CGPoint(x: 700, y: 50), in: out)?.kind == .nowPlaying)
        #expect(DeskHitTest.hasMenu(next))
        #expect(DeskFrameCheck.problem(out, window: CGSize(width: 1512, height: 982)) == nil)
        #expect(DeskFrameCheck.name(next) == "now_playing_next strip")
        // Playing: no button.
        input.nowPlayingStripNext = false
        #expect(!DeskElements.build(input).contains { $0.kind == .nowPlayingNext })
        // The spacing keeps both click areas apart.
        #expect(L.stripSpacing > DeskHitTest.slack(.nowPlaying).width + DeskHitTest.slack(.nowPlayingNext).width)
    }
}

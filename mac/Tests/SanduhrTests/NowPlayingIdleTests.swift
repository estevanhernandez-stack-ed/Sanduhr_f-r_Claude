import Foundation
import Testing
@testable import Sanduhr

/// When nothing is playing: a notch place on Now playing with no line shows another content
/// (NowPlayingIdle, NotchContent.effective) and behaves fully like it, while now playing stays
/// placed. Made-up tracks only.
@Suite("Now playing idle")
struct NowPlayingIdleTests {
    typealias Place = NotchContent.Place
    let places: [Place] = [.left, .right, .strip]
    let song = NowPlayingInfo(title: "Song A", artist: "Band", playing: true)

    @Test func withALineNowPlayingStays() {
        for place in places {
            for idle in NowPlayingIdle.allCases {
                #expect(NotchContent.effective(.nowPlaying, at: place, hasLine: true, idle: idle) == .nowPlaying)
                #expect(NotchContent.effective(.nowPlaying, at: place, nowPlaying: song, idle: idle) == .nowPlaying)
            }
        }
    }

    @Test func automaticIsThePlacesOwnDefault() {
        #expect(NotchContent.effective(.nowPlaying, at: .left, hasLine: false, idle: .automatic) == .meetingOrTime)
        #expect(NotchContent.effective(.nowPlaying, at: .right, hasLine: false, idle: .automatic) == .meters)
        #expect(NotchContent.effective(.nowPlaying, at: .strip, hasLine: false, idle: .automatic) == .meetingOrMeters)
        for place in places {
            #expect(NowPlayingIdle.automatic.content(at: place) == place.fallback)
        }
    }

    @Test func aChosenContentShowsEverywhere() {
        for place in places {
            #expect(NotchContent.effective(.nowPlaying, at: place, hasLine: false, idle: .time) == .time)
            #expect(NotchContent.effective(.nowPlaying, at: place, hasLine: false, idle: .message) == .message)
            #expect(NotchContent.effective(.nowPlaying, at: place, hasLine: false, idle: .meetingOrMeters) == .meetingOrMeters)
        }
    }

    @Test func nothingKeepsThePlaceBlank() {
        for place in places {
            let shown = NotchContent.effective(.nowPlaying, at: place, hasLine: false, idle: .nothing)
            #expect(shown == .nothing)
            #expect(shown.text(at: place, meetings: [], meters: "5h 7%", message: "hi", now: Date()) == nil)
        }
    }

    @Test func noLineMeansNoTrackOrNoTitle() {
        // Nothing plays (hidden while paused, the app switched off, unavailable): nil info.
        #expect(NotchContent.effective(.nowPlaying, at: .right, nowPlaying: nil, idle: .automatic) == .meters)
        // A player with no title has no line either.
        #expect(NotchContent.effective(.nowPlaying, at: .left, nowPlaying: NowPlayingInfo(), idle: .time) == .time)
        // The stand-in's text is the stand-in's own.
        let shown = NotchContent.effective(.nowPlaying, at: .right, nowPlaying: nil, idle: .automatic)
        #expect(shown.text(at: .right, meetings: [], meters: "5h 7%  wk 63%", message: nil, now: Date()) == "5h 7%  wk 63%")
    }

    @Test func otherChoicesAreUntouched() {
        for content in NotchContent.allCases where content != .nowPlaying {
            for place in places {
                for idle in NowPlayingIdle.allCases {
                    #expect(NotchContent.effective(content, at: place, hasLine: false, idle: idle) == content)
                    #expect(NotchContent.effective(content, at: place, hasLine: true, idle: idle) == content)
                }
            }
        }
    }

    @Test func choicesAreEveryContentButNowPlaying() {
        let contents = NowPlayingIdle.allCases.filter { $0 != .automatic }.map { $0.content(at: .left) }
        #expect(Set(contents) == Set(NotchContent.allCases).subtracting([.nowPlaying]))
        #expect(NowPlayingIdle.allCases.first == .automatic)
        #expect(NowPlayingIdle.automatic.label == "What that spot shows by default")
        #expect(NowPlayingIdle.time.label == NotchContent.time.label)
        #expect(Set(NowPlayingIdle.allCases.map(\.label)).count == NowPlayingIdle.allCases.count)
    }

    @Test func notchPageCaption() {
        #expect(NowPlayingIdle.automatic.caption(at: .right) == "When nothing plays: Claude meters (its default).")
        #expect(NowPlayingIdle.automatic.caption(at: .left) == "When nothing plays: Next meeting, or the time (its default).")
        #expect(NowPlayingIdle.nothing.caption(at: .strip) == "When nothing plays: Nothing.")
    }

    @Test func standInWingHasNoNextRoom() {
        let L = NowPlayingWingLayout.self
        let size: CGFloat = 14
        // Paused with a line: the wing makes room for Next.
        let playing = L.sizingState(.nowPlaying, hasText: true, state: .paused)
        #expect(L.wingWidth(textWidth: 80, place: .right, state: playing, size: size, minimum: 36, maximum: 180)
                == 80 + L.wingInsets + L.nextWidth(size) + L.wingSpacing)
        // No line: the wing stands in as the meters and sizes like them, even with a paused state.
        let shown = NotchContent.effective(.nowPlaying, at: .right, hasLine: false, idle: .automatic)
        let idle = L.sizingState(shown, hasText: true, state: .paused)
        #expect(idle == nil)
        #expect(L.wingWidth(textWidth: 80, place: .right, state: idle, size: size, minimum: 36, maximum: 180)
                == 80 + L.wingInsets)
        #expect(L.sizingState(.nowPlaying, hasText: false, state: .paused) == nil)
    }

    @Test func stripTakesNoNowPlayingClicksWhileStandingIn() {
        let shown = NotchContent.effective(.nowPlaying, at: .strip, hasLine: false, idle: .automatic)
        #expect(!DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 26, chinText: true, strip: shown, hasTrack: false))
        let live = NotchContent.effective(.nowPlaying, at: .strip, hasLine: true, idle: .automatic)
        #expect(DeskNowPlaying.stripShows(notch: true, hasNotch: true, chin: 26, chinText: true, strip: live, hasTrack: true))
    }

    @Test func standingInStillCountsAsPlaced() {
        // Placement reads the saved choices, never what shows: a wing standing in keeps now
        // playing running so the next track comes back.
        let d = MemoryDefaults()
        d.set(true, forKey: DeskController.notchKey)
        d.set("nowPlaying", forKey: Place.left.key)
        d.set(true, forKey: "notchChinText")
        d.set("nowPlaying", forKey: Place.strip.key)
        d.set("time", forKey: NowPlayingIdle.key)
        let places = NowPlayingPlacement.saved(in: d)
        #expect(places == [.wingLeft, .strip])
        #expect(NowPlayingPlacement.isRunning(deskRunning: true, places: places))
        #expect(NotchContent.effective(.nowPlaying, at: .left, nowPlaying: nil, idle: .time) == .time)
    }

    @Test func settingRoundTrips() {
        let d = UserDefaults(suiteName: "sanduhr.tests.nowplayingidle.\(UUID().uuidString)")!
        #expect(NowPlayingIdle.saved(in: d) == .automatic)
        for idle in NowPlayingIdle.allCases {
            d.set(idle.rawValue, forKey: NowPlayingIdle.key)
            #expect(NowPlayingIdle.saved(in: d) == idle)
        }
        d.set("nowPlaying", forKey: NowPlayingIdle.key)
        #expect(NowPlayingIdle.saved(in: d) == .automatic)
        d.set("sparkles", forKey: NowPlayingIdle.key)
        #expect(NowPlayingIdle.saved(in: d) == .automatic)
        #expect(NowPlayingIdle.resolve(raw: nil) == .automatic)
        #expect(NowPlayingIdle.key == "nowPlayingIdle")
    }

    @Test func stateYAML() {
        var s = DebugStateInput()
        s.nowPlayingIdle = .time
        s.notchShows = NotchShowsDebug(left: .time, right: .meters, strip: .nowPlaying)
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("  state: none\nnow_playing_idle: time\nnotch_shows:\n  left: time\n  right: meters\n  strip: nowPlaying\nwidget_visible:"))
        let fresh = YAMLEmitter.emit(DebugState.yaml(DebugStateInput()))
        #expect(fresh.contains("now_playing_idle: automatic\nnotch_shows:\n  left: meetingOrTime\n  right: meters\n  strip: meetingOrMeters\n"))
    }
}

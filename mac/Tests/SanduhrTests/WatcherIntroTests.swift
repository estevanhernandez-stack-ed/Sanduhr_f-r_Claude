import Foundation
import Testing
@testable import Sanduhr

/// The notch's watcher line (item 66 follow-up): the full line once, then a short resting line.
/// Short titles, the rest line, and when the intro plays.
@Suite("Watcher notch intro")
struct WatcherIntroTests {
    let t0 = Date(timeIntervalSince1970: 1_790_000_000)

    private func board(_ starts: [(String, String, Int?, String?)]) -> WatcherBoard {
        var b = WatcherBoard()
        for (i, s) in starts.enumerated() {
            b.apply(.start(id: s.0, title: s.1, link: nil, total: s.2, work: false, short: s.3), now: t0 + Double(i))
        }
        return b
    }

    @Test func shortTitlesAreDerived() {
        #expect(WatcherText.shortTitle("PR 140 CI: combine statuslines") == "PR 140")
        #expect(WatcherText.shortTitle("PR #140 checks") == "PR 140")
        #expect(WatcherText.shortTitle("CI for pull 140") == "PR 140")
        #expect(WatcherText.shortTitle("pr#7") == "PR 7")
        #expect(WatcherText.shortTitle("Fix issue #140 flake") == "#140")
        #expect(WatcherText.shortTitle("Release 2.8.0 notarize") == "v2.8.0")
        #expect(WatcherText.shortTitle("Ship v2.8.0 to the Store") == "v2.8.0")
        #expect(WatcherText.shortTitle("deploy: production cluster") == "deploy")
        #expect(WatcherText.shortTitle("Migrations") == "Migrations")
        #expect(WatcherText.shortTitle("Supercalifragilistic run") == "Supercalif")
        #expect(WatcherText.shortTitle("prod deploy") == "prod")   // "pr" alone is not a pull request
    }

    @Test func automaticWatchersTakeTheWorkflowNameOrTheFirstWord() {
        #expect(BackgroundTask(id: "a", type: "workflow", status: "running", description: "", name: "release-train").short
                == "release-tr")
        #expect(BackgroundTask(id: "b", type: "shell", status: "running", description: "Run the test suite").short == "Run")
        #expect(BackgroundTask(id: "c", type: "monitor", status: "running", description: "").short == "monitor")
        var b = WatcherBoard()
        b.applyStop(StopReport(session: "s", folder: nil, tasks: [
            BackgroundTask(id: "t", type: "shell", status: "running", description: "Build the app")]), work: false, now: t0)
        #expect(b.watcher("b:s:t")?.short == "Build")
    }

    @Test func theAgentsShortWinsAndTheRestLineIsShort() {
        let b = board([("w1", "PR 140 CI: combine statuslines", 12, nil), ("w2", "Release notes", nil, "notes")])
        #expect(b.watcher("a:w1")?.short == "PR 140")
        #expect(b.watcher("a:w2")?.short == "notes")
        let shown = b.ordered()   // w2 is newer: first among equals
        #expect(WatcherText.restLine(shown, now: t0 + 125) == "notes · 2m +1")
        #expect(WatcherText.restLine(Array(shown.suffix(1)), now: t0 + 125) == "PR 140 · 0/12")
        #expect(WatcherText.fullLine(Array(shown.suffix(1)), now: t0 + 125) == "PR 140 CI: combine statuslines · 2m · 0/12")
        #expect(WatcherText.notchLine(shown, intro: false, now: t0) == WatcherText.restLine(shown, now: t0))
        #expect(WatcherText.notchLine(shown, intro: true, now: t0) == WatcherText.fullLine(shown, now: t0))
        #expect(WatcherText.restLine([], now: t0) == nil)
    }

    @Test func theShortArgumentTravelsThroughTheRequest() {
        let cmd = WatcherCommand.start(id: "w0123456789ab", title: "PR 140 CI", link: nil, total: 12, work: false,
                                       short: "PR 140")
        #expect(WatcherRequest.decode(WatcherRequest.json(cmd, at: t0), now: t0)?.command == cmd)
        let long = Data(#"{"schema_version":1,"op":"start","id":"w1","requested_at":"\#(HandoffFiles.stamp(t0))","title":"x","short":"a much too long short"}"#.utf8)
        guard case let .start(_, _, _, _, _, short)? = WatcherRequest.decode(long, now: t0)?.command else {
            Issue.record("no start")
            return
        }
        #expect(short?.hasSuffix("…") == true && (short?.count ?? 99) <= WatcherLimits.short)
    }

    @Test func theIntroPlaysOnATopOrStateChangeAndNeverWithReduceMotion() {
        let a = WatcherIntro.key([Watcher(id: "a:1", source: .agent, title: "x", state: .running, started: t0, touched: t0)])
        let aWaiting = WatcherIntro.key([Watcher(id: "a:1", source: .agent, title: "x", state: .waiting, started: t0, touched: t0)])
        let b = WatcherIntro.key([Watcher(id: "a:2", source: .agent, title: "y", state: .running, started: t0, touched: t0)])
        #expect(WatcherIntro.starts(from: nil, to: a, reduceMotion: false))
        #expect(!WatcherIntro.starts(from: a, to: a, reduceMotion: false))          // same top, same state
        #expect(WatcherIntro.starts(from: a, to: aWaiting, reduceMotion: false))    // waiting on you
        #expect(WatcherIntro.starts(from: a, to: b, reduceMotion: false))           // another top
        #expect(!WatcherIntro.starts(from: a, to: nil, reduceMotion: false))        // nothing to show
        #expect(!WatcherIntro.starts(from: nil, to: a, reduceMotion: true))
        #expect(!WatcherIntro.starts(from: a, to: aWaiting, reduceMotion: true))
        // A line that fits holds; one that doesn't lasts its scroll.
        #expect(WatcherIntro.duration(textWidth: 80, room: 120) == WatcherIntro.hold)
        let long = WatcherIntro.duration(textWidth: 400, room: 120)
        #expect(long == NowPlayingScroll.plan(textWidth: 400, room: 120, reduceMotion: false)?.total)
        #expect(long > WatcherIntro.hold)
    }

    @Test func theModelKeepsThePhaseForTheLayout() {
        let model = DeskModel()
        var reduce = false
        model.reduceMotion = { reduce }
        var b = board([("w1", "PR 140 CI: combine statuslines", 12, nil)])
        model.setWatchers(b.ordered())
        #expect(model.watcherIntroUntil != nil)
        // Rest after the intro; an unchanged top does not replay it.
        model.endWatcherIntro()
        model.setWatchers(b.ordered())
        #expect(model.watcherIntroUntil == nil)
        // A state change replays it.
        b.apply(.update(id: "w1", done: 3, note: nil, state: .waiting), now: t0 + 10)
        model.setWatchers(b.ordered())
        #expect(model.watcherIntroUntil != nil)
        // Reduce Motion: straight to rest, and no intro on the next change.
        reduce = true
        b.apply(.end(id: "w1", result: .failed, note: nil), now: t0 + 20)
        model.setWatchers(b.ordered())
        #expect(model.watcherIntroUntil == nil)
        // No watcher: rest, and the next one plays again.
        reduce = false
        model.setWatchers([])
        #expect(model.watcherIntroUntil == nil)
        model.setWatchers(b.ordered())
        #expect(model.watcherIntroUntil != nil)
        // The layout reads the phase: a wing on Watchers is wider during the intro.
        let rest = NotchContent.watchers.text(at: .right, meetings: [], meters: nil, message: nil,
                                              watchers: model.watchers, watcherIntro: false, now: t0 + 30)
        let intro = NotchContent.watchers.text(at: .right, meetings: [], meters: nil, message: nil,
                                               watchers: model.watchers, watcherIntro: model.watcherIntroUntil != nil,
                                               now: t0 + 30)
        #expect(rest == "PR 140 · 3/12")
        #expect((intro?.count ?? 0) > (rest?.count ?? 0))
        model.endWatcherIntro()
    }
}

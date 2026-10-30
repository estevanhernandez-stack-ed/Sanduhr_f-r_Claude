import Foundation
import Testing
@testable import Sanduhr

/// The replay gate (spec section 15, "Starting mid-job"): the tail Docker replays rebuilds the
/// open job but never puts up a card for one that already ended. Ported from Windows'
/// RunnerFollowTests.
@Suite("Runner follow")
struct RunnerFollowTests {
    let opened = Date(timeIntervalSince1970: 1_790_000_000)

    private func start(_ job: String, _ before: TimeInterval) -> RunnerLine { .jobStarted(job: job, at: opened - before) }
    private func end(_ job: String, _ before: TimeInterval, _ r: WatcherState = .passed) -> RunnerLine {
        .jobEnded(job: job, result: r, at: opened - before)
    }
    private func online(_ before: TimeInterval) -> RunnerLine { .online(at: opened - before) }

    @Test func finishedJobsInTheReplayPutUpNothing() {
        var f = RunnerFollow(opened: opened)
        let a = f.feed(start("compile", 600))
        let b = f.feed(end("compile", 590))
        let idle = f.endReplay() // idle, no card
        #expect(a.isEmpty && b.isEmpty)
        #expect(idle == [.online(at: opened)])
        #expect(f.live)
        let again = f.endReplay()
        #expect(again.isEmpty) // once
    }

    @Test func anOpenJobAtTheEndOfTheReplayComesOutWithItsRealStart() {
        var f = RunnerFollow(opened: opened)
        _ = f.feed(start("compile", 600))
        _ = f.feed(end("compile", 590))
        _ = f.feed(start("test", 300))
        let got = f.endReplay()
        #expect(got == [start("test", 300)])
        let again = f.endReplay()
        #expect(again.isEmpty) // once
    }

    @Test func restartInReplayLeavesNothingOpen() {
        var f = RunnerFollow(opened: opened)
        _ = f.feed(start("stuck", 900))
        _ = f.feed(online(800))
        let got = f.endReplay()
        #expect(got == [.online(at: opened)])
    }

    @Test func aLiveLineEndsTheReplayFirst() {
        var f = RunnerFollow(opened: opened)
        _ = f.feed(start("test", 300))
        let live = end("test", -5)
        let got = f.feed(live)
        #expect(got == [start("test", 300), live])
        #expect(f.live)
    }

    @Test func withinTheMarginIsLive() {
        var f = RunnerFollow(opened: opened)
        let s = start("test", 29)
        let got = f.feed(s)
        #expect(got == [.online(at: opened), s])
    }

    @Test func afterReplayEveryLinePassesThrough() {
        var f = RunnerFollow(opened: opened)
        let idle = f.endReplay()
        #expect(idle == [.online(at: opened)])
        let old = end("x", 3600)
        let got = f.feed(old)
        #expect(got == [old])
    }
}

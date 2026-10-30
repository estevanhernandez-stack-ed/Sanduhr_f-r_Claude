import Foundation
import Testing
@testable import Sanduhr

/// Runner cards on the watcher board (spec section 15). Ported from Windows' RunnerBoardTests.
@Suite("Runner board")
struct RunnerBoardTests {
    let t0 = Date(timeIntervalSince1970: 1_790_000_000)

    private func start(_ job: String, at: TimeInterval = 0) -> RunnerLine { .jobStarted(job: job, at: t0 + at) }
    private func end(_ job: String, _ r: WatcherState, at: TimeInterval = 60) -> RunnerLine {
        .jobEnded(job: job, result: r, at: t0 + at)
    }

    @Test func aJobPutsUpACard() throws {
        var b = WatcherBoard()
        let change = b.applyRunner(key: "alpha-1", runner: "alpha-1", line: start("test (ubuntu-latest, 3.12)", at: -30), now: t0)
        #expect(change == RunnerChange(changed: true, failed: false))
        let w = try #require(b.watcher("r:alpha-1:test (ubuntu-latest, 3.12)"))
        #expect(w.source == .runner)
        #expect(w.title == "alpha-1 · test (ubuntu-latest, 3.12)")
        #expect(w.short == WatcherText.clip("test (ubuntu-latest, 3.12)", WatcherLimits.short))
        #expect(w.note == "CI runner")
        #expect(w.link == nil)
        #expect(w.state == .running)
        #expect(w.started == t0 - 30)
        #expect(w.runner == "alpha-1")
        #expect(w.job == "test (ubuntu-latest, 3.12)")
    }

    @Test(arguments: [(WatcherState.passed, false), (.failed, true), (.finished, false)])
    func theResultEndsIt(result: WatcherState, failed: Bool) throws {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        let change = b.applyRunner(key: "echo-1", runner: "echo-1", line: end("build", result), now: t0 + 60)
        #expect(change == RunnerChange(changed: true, failed: failed))
        let w = try #require(b.watcher("r:echo-1:build"))
        #expect(w.state == result)
        #expect(w.ended == t0 + 60)
    }

    @Test func anEndWithNoCardDoesNothing() {
        var b = WatcherBoard()
        let change = b.applyRunner(key: "echo-1", runner: "echo-1", line: end("build", .failed), now: t0)
        #expect(change == RunnerChange())
        #expect(b.watchers.isEmpty)
    }

    @Test func aSecondEndDoesNotGlowAgain() {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        b.applyRunner(key: "echo-1", runner: "echo-1", line: end("build", .failed), now: t0)
        let change = b.applyRunner(key: "echo-1", runner: "echo-1", line: end("build", .failed), now: t0)
        #expect(change == RunnerChange())
    }

    @Test func aNewJobOnTheSameRunnerEndsTheOpenOneAsFinished() {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("test", at: 30), now: t0 + 30)
        #expect(b.watcher("r:echo-1:build")?.state == .finished)
        #expect(b.watcher("r:echo-1:test")?.state == .running)
    }

    @Test func otherRunnersAreUntouched() {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        b.applyRunner(key: "echo-2", runner: "echo-2", line: start("build"), now: t0)
        b.applyRunner(key: "echo-2", runner: "echo-2", line: .online(at: t0), now: t0)
        #expect(b.watcher("r:echo-1:build")?.state == .running)
        #expect(b.watcher("r:echo-2:build")?.state == .finished)
    }

    @Test func siblingsThatShareALabelAreToldApartByTheirKey() {
        var b = WatcherBoard()
        b.applyRunner(key: "ci-runner-1", runner: "runner", line: start("build"), now: t0)
        b.applyRunner(key: "ci-runner-2", runner: "runner", line: start("test", at: 10), now: t0 + 10)
        // B starts a new job and comes online again: A's running card is untouched.
        b.applyRunner(key: "ci-runner-2", runner: "runner", line: start("lint", at: 20), now: t0 + 20)
        b.applyRunner(key: "ci-runner-2", runner: "runner", line: .online(at: t0), now: t0 + 30)
        #expect(b.watcher("r:ci-runner-1:build")?.state == .running)
        #expect(b.watcher("r:ci-runner-2:test")?.state == .finished)
        // B ends its job: only B's card moves.
        b.applyRunner(key: "ci-runner-2", runner: "runner", line: start("lint", at: 40), now: t0 + 40)
        b.applyRunner(key: "ci-runner-2", runner: "runner", line: end("lint", .failed), now: t0 + 50)
        #expect(b.watcher("r:ci-runner-1:build")?.state == .running)
        #expect(b.watcher("r:ci-runner-2:lint")?.state == .failed)
        #expect(b.watcher("r:ci-runner-1:build")?.title == "runner · build")
    }

    @Test func aSiblingLeavingEndsOnlyItsOwnCard() {
        var b = WatcherBoard()
        b.applyRunner(key: "ci-runner-1", runner: "runner", line: start("build"), now: t0)
        b.applyRunner(key: "ci-runner-2", runner: "runner", line: start("test"), now: t0)
        let changed = b.runnerGone(key: "ci-runner-2", now: t0 + 5)
        #expect(changed)
        #expect(b.watcher("r:ci-runner-1:build")?.state == .running)
        #expect(b.watcher("r:ci-runner-2:test")?.state == .finished)
    }

    @Test func aLongRunnerAndJobAreClippedToTheTitleCap() {
        var b = WatcherBoard()
        b.applyRunner(key: "k", runner: String(repeating: "r", count: 40), line: start(String(repeating: "j", count: 120)), now: t0)
        #expect((b.watchers.first?.title.count ?? 999) <= WatcherLimits.title)
    }

    @Test func anEndedRunnerCardSaysSoOnTheDesk() throws {
        var b = WatcherBoard()
        b.applyRunner(key: "k", runner: "k", line: start("build"), now: t0)
        #expect(WatcherText.deskNote(try #require(b.watcher("r:k:build"))) == "CI runner")
        b.applyRunner(key: "k", runner: "k", line: end("build", .failed), now: t0)
        #expect(WatcherText.deskNote(try #require(b.watcher("r:k:build"))) == "failed")
    }

    @Test func runnerGoneEndsItsOpenCard() {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        let first = b.runnerGone(key: "echo-1", now: t0 + 5)
        #expect(first)
        #expect(b.watcher("r:echo-1:build")?.state == .finished)
        let second = b.runnerGone(key: "echo-1", now: t0 + 6)
        #expect(!second)
    }

    @Test func runnerCardNeverLosesTouchAndSchedulesNothing() {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        #expect(WatcherBoard.window(.runner) == nil)
        #expect(b.nextChange() == nil)
        let day = b.tick(now: t0 + 24 * 3600)
        let forever = b.tick(now: .distantFuture)
        #expect(!day && !forever)
        #expect(b.watcher("r:echo-1:build")?.state == .running)
    }

    @Test func passedRunnerCardsFadeLikeAnyOther() {
        var b = WatcherBoard()
        b.applyRunner(key: "echo-1", runner: "echo-1", line: start("build"), now: t0)
        b.applyRunner(key: "echo-1", runner: "echo-1", line: end("build", .passed), now: t0)
        #expect(b.nextChange() == t0 + WatcherBoard.fadeAfter)
        let faded = b.tick(now: t0 + WatcherBoard.fadeAfter)
        #expect(faded)
        #expect(b.watchers.isEmpty)
    }

    @Test func demoModeHidesRunnerCards() {
        var b = WatcherBoard()
        b.applyRunner(key: "alpha-1", runner: "alpha-1", line: start("build"), now: t0)
        b.apply(.start(id: "w1", title: "deploy", link: nil, total: nil, work: false), now: t0)
        #expect(WatcherBoard.shown(b.ordered(), demo: true).map(\.id) == ["a:w1"])
        #expect(WatcherBoard.shown(b.ordered(), demo: false).count == 2)
    }

    @Test func clearDropsOnlyRunnerCards() {
        var b = WatcherBoard()
        b.applyRunner(key: "alpha-1", runner: "alpha-1", line: start("build"), now: t0)
        b.apply(.start(id: "w1", title: "deploy", link: nil, total: nil, work: false), now: t0)
        b.clear(.runner)
        #expect(b.watchers.map(\.source) == [.agent])
    }

    @Test func spokenNamesTheJobAndRunner() throws {
        var b = WatcherBoard()
        b.applyRunner(key: "alpha-1", runner: "alpha-1", line: start("test"), now: t0)
        #expect(WatcherText.spoken(try #require(b.watcher("r:alpha-1:test")), now: t0 + 180)
            == "CI job test on alpha-1, running, 3m")
    }
}

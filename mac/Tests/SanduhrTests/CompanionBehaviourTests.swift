import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// FORMAT 6: mood from pacing, the triggers, which frame plays, and the roaming route and clock (port
/// of Windows' CompanionBehaviourTests.cs). Differences from the Windows set: the alert lines are the
/// Mac's two (session and weekly, Settings > Alerts) instead of Windows' warn and urgent, so the
/// near_limit case uses a session line of 90 where Windows used 80 and 95; the idle case feeds seconds
/// instead of two timestamps; the key cases check `PackValidator.moodKeys` and `triggerKeys` too.
@Suite("Companion behaviour")
struct CompanionBehaviourTests {
    static let now = Date(timeIntervalSince1970: 1_792_670_400)   // 2026-10-22 12:00:00 UTC

    static func iso(_ t: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f.string(from: t)
    }

    /// A tier `through` of the way through its window (0 to 1) at `now`.
    static func tier(_ tier: Tier, _ util: Double, _ through: Double, at: Date? = nil) -> CompanionTierReading {
        CompanionTierReading(tier: tier, utilization: util, resetsAt: iso((at ?? now).addingTimeInterval(tier.totalSeconds * (1 - through))))
    }

    static func usage(_ tiers: CompanionTierReading...) -> CompanionUsage { CompanionUsage(tiers: tiers) }

    static func hourglass() -> CompanionSprite { CompanionTestKit.fixture("packs", "hourglass") }

    static func mood(_ tiers: CompanionTierReading...) -> CompanionMood {
        CompanionMoods.fromUsage(CompanionUsage(tiers: tiers), now: now)
    }

    // ---- mood ----

    @Test func underPaceIsContentNearIsFocusedOverIsWorried() {
        typealias T = CompanionBehaviourTests
        #expect(T.mood(T.tier(.fiveHour, 10, 0.5), T.tier(.sevenDay, 20, 0.5)) == .content)
        #expect(T.mood(T.tier(.fiveHour, 48, 0.5), T.tier(.sevenDay, 20, 0.5)) == .focused)
        #expect(T.mood(T.tier(.fiveHour, 70, 0.5), T.tier(.sevenDay, 20, 0.5)) == .worried)
        #expect(T.mood(T.tier(.fiveHour, 10, 0.5), T.tier(.sevenDay, 55, 0.5)) == .worried)
    }

    @Test func weeklyAt85IsWorriedEvenUnderPaceAndTheLimitIsExhausted() {
        typealias T = CompanionBehaviourTests
        #expect(T.mood(T.tier(.sevenDay, 85, 0.95)) == .worried)
        #expect(T.mood(T.tier(.sevenDay, 84, 0.99)) == .content)
        #expect(T.mood(T.tier(.fiveHour, 100, 0.9)) == .exhausted)
        #expect(T.mood(T.tier(.fiveHour, 20, 0.9), T.tier(.sevenDay, 104, 0.9)) == .exhausted)
    }

    @Test func signedOutNoNumbersOrNothingUsedIsContent() {
        typealias T = CompanionBehaviourTests
        #expect(CompanionMoods.fromUsage(.empty, now: T.now) == .content)
        #expect(CompanionMoods.fromUsage(CompanionUsage(tiers: [T.tier(.fiveHour, 100, 0.5)], signInNeeded: true), now: T.now) == .content)
        #expect(T.mood(T.tier(.fiveHour, 0, 0.02)) == .content)
    }

    @Test func aLimitWithoutAResetTimeCannotBeExhaustedOrPaced() {
        let noReset = CompanionTierReading(tier: .fiveHour, utilization: 100, resetsAt: nil)
        #expect(CompanionMoods.fromUsage(CompanionUsage(tiers: [noReset]), now: Self.now) == .content)
        let special = Self.tier(.iguanaNecktie, 100, 0.5)
        #expect(CompanionMoods.fromUsage(CompanionUsage(tiers: [special]), now: Self.now) == .content)
    }

    @Test func aUsageResponseMapsToReadingsInTierOrder() {
        var r = UsageResponse()
        r.tiers[.sevenDay] = TierUsage(utilization: 40, resetsAt: "2026-10-25T12:00:00Z")
        r.tiers[.fiveHour] = TierUsage(utilization: 10, resetsAt: nil)
        r.tiers[.sevenDayOpus] = TierUsage(utilization: nil, resetsAt: nil)
        #expect(CompanionUsage(r).tiers.map(\.tier) == [.fiveHour, .sevenDay])
        #expect(CompanionUsage(r, only: [.sevenDay]).tiers.map(\.tier) == [.sevenDay])
        #expect(CompanionUsage(r, signInNeeded: true).signInNeeded)
    }

    @Test(arguments: [(CompanionMood.content, "content"), (.focused, "focused"), (.worried, "worried"), (.exhausted, "exhausted"), (.celebrate, "celebrate")])
    func eachMoodHasItsFormatKey(mood: CompanionMood, key: String) {
        #expect(mood.key == key)
        #expect(PackValidator.moodKeys.contains(key))
    }

    @Test func eachTriggerHasItsFormatKey() {
        #expect(PackValidator.triggerKeys == CompanionTrigger.allCases.map(\.key))
    }

    // ---- the brain ----

    @Test func theWeeklyResetCelebratesForTenMinutesThenTheNumbersMoodAgain() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        _ = brain.observeUsage(T.usage(T.tier(.sevenDay, 60, 0.999)), sessionLine: 80, weeklyLine: 80, now: T.now)
        #expect(brain.mood(at: T.now) == .content)
        let later = T.now.addingTimeInterval(20 * 60)
        _ = brain.observeUsage(T.usage(T.tier(.sevenDay, 0, 0.001, at: later)), sessionLine: 80, weeklyLine: 80, now: later)
        #expect(brain.mood(at: later) == .celebrate)
        #expect(brain.mood(at: later.addingTimeInterval(9 * 60)) == .celebrate)
        #expect(brain.mood(at: later.addingTimeInterval(CompanionBrain.celebrateFor)) == .content)
    }

    @Test func exhaustedSleepsUntilTheSessionResetsAndTheResetCelebrates() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 100, 0.9)), sessionLine: 80, weeklyLine: 80, now: T.now)
        #expect(brain.mood(at: T.now) == .exhausted)
        let reset = T.now.addingTimeInterval(40 * 60)
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 2, 0.01, at: reset)), sessionLine: 80, weeklyLine: 80, now: reset)
        #expect(brain.mood(at: reset) == .celebrate)
    }

    @Test func aNewWindowThatIsAlreadyAtTheLimitSleepsInsteadOfCelebrating() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 100, 0.9)), sessionLine: 80, weeklyLine: 80, now: T.now)
        let reset = T.now.addingTimeInterval(40 * 60)
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 100, 0.01, at: reset)), sessionLine: 80, weeklyLine: 80, now: reset)
        #expect(brain.mood(at: reset) == .exhausted)
    }

    @Test func aSessionResetFromACalmWindowIsNotAParty() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 30, 0.9)), sessionLine: 80, weeklyLine: 80, now: T.now)
        let reset = T.now.addingTimeInterval(3600)
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 0, 0.01, at: reset)), sessionLine: 80, weeklyLine: 80, now: reset)
        #expect(brain.mood(at: reset) == .content)
    }

    @Test func resetTimeJitterIsNotAReset() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        let a = CompanionTierReading(tier: .sevenDay, utilization: 60, resetsAt: T.iso(T.now.addingTimeInterval(86_400)))
        let b = CompanionTierReading(tier: .sevenDay, utilization: 61, resetsAt: T.iso(T.now.addingTimeInterval(86_430)))
        _ = brain.observeUsage(T.usage(a), sessionLine: 80, weeklyLine: 80, now: T.now)
        _ = brain.observeUsage(T.usage(b), sessionLine: 80, weeklyLine: 80, now: T.now)
        #expect(brain.mood(at: T.now) != .celebrate)
    }

    @Test func crossingAnAlertLineUpwardPlaysNearLimitOnce() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        func read(_ t: CompanionTierReading) -> [CompanionTrigger] {
            brain.observeUsage(T.usage(t), sessionLine: 90, weeklyLine: 80, now: T.now)
        }
        #expect(read(T.tier(.fiveHour, 85, 0.9)).isEmpty)   // the first reading is the baseline
        #expect(read(T.tier(.fiveHour, 88, 0.9)).isEmpty)
        #expect(read(T.tier(.fiveHour, 96, 0.9)) == [.nearLimit])
        #expect(read(T.tier(.fiveHour, 97, 0.9)).isEmpty)
        #expect(read(T.tier(.fiveHour, 100, 0.9)) == [.nearLimit])
        // The weekly limit has its own line: down and back up over it crosses again.
        _ = read(T.tier(.sevenDay, 50, 0.5))
        #expect(read(T.tier(.sevenDay, 81, 0.5)) == [.nearLimit])
    }

    @Test func theSessionAndWeeklyLinesAreEachTheirOwn() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        _ = brain.observeUsage(T.usage(T.tier(.fiveHour, 70, 0.9), T.tier(.sevenDay, 70, 0.9)), sessionLine: 90, weeklyLine: 75, now: T.now)
        // Session 70 -> 80 stays under its 90 line; weekly 70 -> 80 crosses its 75 line.
        let fired = brain.observeUsage(T.usage(T.tier(.fiveHour, 80, 0.9), T.tier(.sevenDay, 80, 0.9)), sessionLine: 90, weeklyLine: 75, now: T.now)
        #expect(fired == [.nearLimit])
        var alerts = AlertSettings()
        alerts.sessionLine = 60
        alerts.weeklyLine = 95
        let b2 = CompanionBrain()
        _ = b2.observeUsage(T.usage(T.tier(.fiveHour, 50, 0.9)), alerts: alerts, now: T.now)
        #expect(b2.observeUsage(T.usage(T.tier(.fiveHour, 61, 0.9)), alerts: alerts, now: T.now) == [.nearLimit])
    }

    static func watcher(_ id: String, _ state: WatcherState) -> Watcher {
        Watcher(id: id, source: .agent, title: id, state: state, started: now, touched: now)
    }

    @Test func aWatcherWaitingOnYouPlaysNeedsYouAndOneThatPassedPlaysJobPassed() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        #expect(brain.observeWatchers([T.watcher("a:1", .waiting)]).isEmpty)   // baseline: already waiting at start
        #expect(brain.observeWatchers([T.watcher("a:1", .waiting), T.watcher("a:2", .running)]).isEmpty)
        #expect(brain.observeWatchers([T.watcher("a:1", .running), T.watcher("a:2", .waiting)]) == [.needsYou])
        #expect(brain.observeWatchers([T.watcher("a:1", .passed), T.watcher("a:2", .waiting)]) == [.jobPassed])
        #expect(brain.observeWatchers([T.watcher("a:1", .passed)]).isEmpty)
        #expect(brain.observeWatchers([T.watcher("a:3", .failed)]).isEmpty)
        #expect(brain.observeWatchers([T.watcher("a:4", .waiting), T.watcher("a:5", .passed)]) == [.needsYou, .jobPassed])
    }

    @Test func otherWatcherStatesFireNothing() {
        typealias T = CompanionBehaviourTests
        let brain = CompanionBrain()
        _ = brain.observeWatchers([])
        for state in [WatcherState.running, .failed, .finished, .lostTouch] {
            #expect(brain.observeWatchers([T.watcher("a:9", state)]).isEmpty, "\(state)")
        }
    }

    @Test func twentyMinutesWithoutInputPlaysIdleOnceUntilInputReturns() {
        let brain = CompanionBrain()
        #expect(brain.observeActivity(idleSeconds: 19 * 60) == nil)
        #expect(brain.observeActivity(idleSeconds: nil) == nil)
        #expect(brain.observeActivity(idleSeconds: 20 * 60) == .idle)
        #expect(brain.observeActivity(idleSeconds: 45 * 60) == nil)
        #expect(brain.observeActivity(idleSeconds: 2) == nil)   // input is back
        #expect(brain.observeActivity(idleSeconds: CompanionBrain.idleAfter) == .idle)
    }

    // ---- frames ----

    @Test func theMoodLoopsAtThePacksFpsFromItsFirstFrame() {
        typealias T = CompanionBehaviourTests
        let a = CompanionAnimator(T.hourglass())
        #expect(a.pose(mood: .content, walking: false, at: T.now, reduceMotion: false) == CompanionPose(animation: "sit", frame: 4, oneShot: false))
        #expect(a.pose(mood: .content, walking: false, at: T.now.addingTimeInterval(0.125), reduceMotion: false).frame == 5)
        #expect(a.pose(mood: .content, walking: false, at: T.now.addingTimeInterval(0.25), reduceMotion: false).frame == 4)
        // A new mood starts at its own first frame.
        let t = T.now.addingTimeInterval(1.3)
        #expect(a.pose(mood: .worried, walking: false, at: t, reduceMotion: false) == CompanionPose(animation: "worry", frame: 16, oneShot: false))
        #expect(a.pose(mood: .focused, walking: false, at: t, reduceMotion: false) == CompanionPose(animation: "focus", frame: 18, oneShot: false))
        #expect(a.pose(mood: .exhausted, walking: false, at: t, reduceMotion: false).animation == "sleep")
        #expect(a.pose(mood: .celebrate, walking: false, at: t, reduceMotion: false).animation == "celebrate")
    }

    @Test(arguments: [(CompanionTrigger.needsYou, "pop"), (.jobPassed, "cheer"), (.nearLimit, "worry"), (.idle, "nap")])
    func aTriggerPlaysItsAnimationOnceThenTheMoodResumes(trigger: CompanionTrigger, animation: String) {
        typealias T = CompanionBehaviourTests
        let sprite = T.hourglass()
        let a = CompanionAnimator(sprite)
        _ = a.pose(mood: .content, walking: false, at: T.now, reduceMotion: false)
        a.play(trigger, at: T.now)
        let frames = sprite.frames(animation)
        for i in frames.indices {
            let at = T.now.addingTimeInterval(Double(i) / 8)
            #expect(a.pose(mood: .content, walking: true, at: at, reduceMotion: false) == CompanionPose(animation: animation, frame: frames[i], oneShot: true))
            #expect(a.playing(at: at, reduceMotion: false))
            #expect(!a.canWalk(mood: .content, at: at, reduceMotion: false))
        }
        #expect(a.duration(of: trigger, reduceMotion: false) == Double(frames.count) / 8)
        let after = T.now.addingTimeInterval(Double(frames.count) / 8)
        #expect(!a.playing(at: after, reduceMotion: false))
        #expect(a.pose(mood: .content, walking: false, at: after, reduceMotion: false) == CompanionPose(animation: "sit", frame: 4, oneShot: false))
    }

    @Test func reduceMotionDrawsAtOneFrameASecond() {
        typealias T = CompanionBehaviourTests
        let sprite = T.hourglass()
        #expect(CompanionAnimator.fps(for: sprite, reduceMotion: true) == 1)
        #expect(CompanionAnimator.fps(for: sprite, reduceMotion: false) == 8)
        let a = CompanionAnimator(sprite)
        #expect(a.pose(mood: .content, walking: false, at: T.now, reduceMotion: true).frame == 4)
        #expect(a.pose(mood: .content, walking: false, at: T.now.addingTimeInterval(0.9), reduceMotion: true).frame == 4)
        #expect(a.pose(mood: .content, walking: false, at: T.now.addingTimeInterval(1), reduceMotion: true).frame == 5)
        a.play(.needsYou, at: T.now.addingTimeInterval(2))
        #expect(a.playing(at: T.now.addingTimeInterval(5.5), reduceMotion: true))   // four frames take four seconds
        #expect(!a.playing(at: T.now.addingTimeInterval(6), reduceMotion: true))
    }

    @Test func walkingDrawsWalkButExhaustedNeverWalks() {
        typealias T = CompanionBehaviourTests
        let a = CompanionAnimator(T.hourglass())
        #expect(a.pose(mood: .content, walking: true, at: T.now, reduceMotion: false) == CompanionPose(animation: "walk", frame: 0, oneShot: false))
        #expect(a.pose(mood: .content, walking: true, at: T.now.addingTimeInterval(3.0 / 8), reduceMotion: false).frame == 3)
        #expect(a.pose(mood: .exhausted, walking: true, at: T.now, reduceMotion: false).animation == "sleep")
        #expect(!a.canWalk(mood: .exhausted, at: T.now, reduceMotion: false))
        #expect(a.canWalk(mood: .worried, at: T.now, reduceMotion: false))
    }

    // ---- roaming ----

    static let screen = CGRect(x: 0, y: 0, width: 1000, height: 600)

    @Test func alongTheBottomItWalksThereAndBackFacingItsWay() throws {
        let path = try #require(CompanionPath.build(edges: [.bottom], area: Self.screen, spriteWidth: 64, spriteHeight: 64))
        #expect(abs(path.length - (1000 - 64)) < 1e-6)
        #expect(path.at(0) == CompanionSpot(x: 32, y: 600, facingLeft: false, edge: .bottom))
        #expect(path.at(100) == CompanionSpot(x: 132, y: 600, facingLeft: false, edge: .bottom))
        let back = path.at(path.length + 100)
        #expect(abs(back.x - (968 - 100)) < 1e-6)
        #expect(back.facingLeft)
        #expect(abs(path.at(2 * path.length).x - 32) < 1e-6)
    }

    @Test func bottomLeftAndRightMakeOneRouteUpBothSides() throws {
        let path = try #require(CompanionPath.build(edges: [.bottom, .left, .right], area: Self.screen, spriteWidth: 64, spriteHeight: 64))
        let side = 600.0 - 64, bottom = 1000.0 - 64
        #expect(abs(path.length - (2 * side + bottom)) < 1e-6)
        let start = path.at(0)
        #expect(start.x == 32 && start.y == 64 && start.edge == .left)
        #expect(!start.facingLeft)
        #expect(path.at(side + 10).edge == .bottom)
        let right = path.at(side + bottom + 10)
        #expect(abs(right.x - 968) < 1e-6 && abs(right.y - 590) < 1e-6 && right.edge == .right)
        #expect(right.facingLeft)
    }

    @Test func withoutTheBottomItHopsBetweenTheSides() throws {
        let path = try #require(CompanionPath.build(edges: [.left, .right], area: Self.screen, spriteWidth: 64, spriteHeight: 64))
        #expect(abs(path.length - 2 * (600 - 64)) < 1e-6)
        #expect(path.at(600 - 64 - 1).edge == .left)
        let hop = path.at(600 - 64 + 1)
        #expect(hop.edge == .right)
        #expect(abs(hop.x - 968) < 1e-6)
    }

    @Test func aRouteStartsNearestTheAnchorAndAScreenTooSmallHasNone() throws {
        let path = try #require(CompanionPath.build(edges: [.bottom], area: Self.screen, spriteWidth: 64, spriteHeight: 64))
        #expect(abs(path.nearest(0, 600)) < 1e-6)
        #expect(abs(path.nearest(1000, 600) - path.length) < 1e-6)
        #expect(abs(path.nearest(500, 0) - (500 - 32)) < 1e-6)
        #expect(CompanionPath.build(edges: [.bottom], area: CGRect(x: 0, y: 0, width: 60, height: 600), spriteWidth: 64, spriteHeight: 64) == nil)
        #expect(CompanionPath.build(edges: [], area: Self.screen, spriteWidth: 64, spriteHeight: 64) == nil)
    }

    @Test func theRoamerWalksThenRestsAndHoldsStillWhenItMayNotWalk() {
        var r = CompanionRoamer(start: 100)
        #expect(r.walking)
        for _ in 0..<48 { r.advance(0.25, allowed: true) }   // 12 s
        #expect(abs(r.walkedSeconds - 12) < 1e-3)
        #expect(!r.walking)
        #expect(abs(r.distance(speed: 30) - (100 + 12 * 30)) < 1e-3)
        for _ in 0..<16 { r.advance(0.25, allowed: true) }   // resting 4 s
        #expect(abs(r.walkedSeconds - 12) < 1e-3)
        r.advance(5, allowed: false)   // asleep, covered or a trigger playing: nothing moves
        #expect(abs(r.walkedSeconds - 12) < 1e-3)
        for _ in 0..<20 { r.advance(0.25, allowed: true) }   // the rest ends, walking again
        #expect(r.walking)
        #expect(r.walkedSeconds > 12)
        // A long gap is not a sprint.
        var s = CompanionRoamer()
        s.advance(3 * 3600, allowed: true)
        #expect(abs(s.walkedSeconds - 1) < 1e-6)
    }

    @Test func reduceMotionTheSwitchSleepAndATriggerAllStopTheWalk() {
        #expect(CompanionRoamer.allowed(roams: true, reduceMotion: false, mood: .content, triggerPlaying: false))
        #expect(!CompanionRoamer.allowed(roams: true, reduceMotion: true, mood: .content, triggerPlaying: false))
        #expect(!CompanionRoamer.allowed(roams: false, reduceMotion: false, mood: .content, triggerPlaying: false))
        #expect(!CompanionRoamer.allowed(roams: true, reduceMotion: false, mood: .exhausted, triggerPlaying: false))
        #expect(!CompanionRoamer.allowed(roams: true, reduceMotion: false, mood: .content, triggerPlaying: true))
        #expect(CompanionRoamer.allowed(roams: true, reduceMotion: false, mood: .celebrate, triggerPlaying: false))
    }
}

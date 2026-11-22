import Foundation
import Testing
@testable import Sanduhr

/// Mac agent notifications, item 1: which surfaces a turn event uses (design section 3, Windows
/// section 11). Pure: the front app and the clock are injected.
@Suite("Notification rules")
struct NotificationRulesTests {
    let now = Date(timeIntervalSince1970: 1_791_018_000)
    func at(_ s: TimeInterval) -> Date { now.addingTimeInterval(s) }
    let color = "#2FB5A3"
    var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    func settings(banner: Bool = true, card: Bool = true, glowFinished: Bool = true, glowWaiting: Bool = true,
                  skipInFront: Bool = true, idle: Bool = false, threshold: TimeInterval = 60,
                  quiet: (enabled: Bool, start: Int, end: Int) = (false, 0, 0)) -> NotificationSettings {
        NotificationSettings(banner: banner, card: card, glowFinished: glowFinished, glowWaiting: glowWaiting,
                             skipInFront: skipInFront, idle: idle, threshold: threshold, quiet: quiet)
    }

    func event(_ kind: AgentTurnEvent.Kind, agent: AgentSource = .claude, session: String = "s1",
               duration: TimeInterval? = nil, at when: Date? = nil) -> AgentTurnEvent {
        AgentTurnEvent(agent: agent, kind: kind, project: "alpha", session: session, at: when ?? now, duration: duration)
    }

    /// Counts how many times the front app was read.
    final class Front: @unchecked Sendable {
        var reads = 0
        let bundle: String?
        let name: String?
        init(_ bundle: String? = nil, name: String? = nil) { self.bundle = bundle; self.name = name }
    }

    func decide(_ e: AgentTurnEvent, _ s: NotificationSettings, front: Front = Front(),
                limiter: inout NotificationLimiter, at when: Date? = nil) -> NotificationDecision {
        NotificationRules.decide(e, settings: s, frontBundleID: { front.reads += 1; return front.bundle },
                                 frontName: { front.name }, sourceColorHex: color, limiter: &limiter,
                                 now: when ?? e.at, calendar: calendar)
    }

    func decide(_ e: AgentTurnEvent, _ s: NotificationSettings, front: Front = Front()) -> NotificationDecision {
        var l = NotificationLimiter()
        return decide(e, s, front: front, limiter: &l)
    }

    let all = NotificationDecision(banner: true, glow: true, card: true, glowColorHex: nil)

    // MARK: The table

    @Test func startedIsOnlyACard() {
        let front = Front()
        let d = decide(event(.started), settings(), front: front)
        #expect(d == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
        #expect(front.reads == 0)
    }

    @Test func finishedAtTheThresholdInterruptsInTheSourceColor() {
        let d = decide(event(.finished, duration: 60), settings())
        #expect(d == NotificationDecision(banner: true, glow: true, card: true, glowColorHex: color))
    }

    @Test func finishedBelowTheThresholdIsOnlyACard() {
        let front = Front()
        let d = decide(event(.finished, duration: 59), settings(), front: front)
        #expect(d == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
        #expect(front.reads == 0)
    }

    @Test func stoppedIsLikeFinished() {
        #expect(decide(event(.stopped, duration: 120), settings()).banner)
        #expect(decide(event(.stopped, duration: 120), settings()).glowColorHex == color)
        #expect(!decide(event(.stopped, duration: 5), settings()).banner)
        #expect(decide(event(.stopped, duration: 5), settings()).card)
    }

    @Test func waitingInterruptsAtOnceWithTheNotchInk() {
        let d = decide(event(.waiting, duration: nil), settings())
        #expect(d == NotificationDecision(banner: true, glow: true, card: true, glowColorHex: nil))
    }

    @Test func idleIsNothingWithTheSwitchOff() {
        #expect(decide(event(.idle), settings(idle: false)) == NotificationDecision(banner: false, glow: false, card: false, glowColorHex: nil))
    }

    @Test func idleWithTheSwitchOnInterruptsLikeWaitingButNeverMakesACard() {
        let d = decide(event(.idle), settings(idle: true))
        #expect(d == NotificationDecision(banner: true, glow: true, card: false, glowColorHex: nil))
    }

    @Test func theGlowSwitchesAreSplitByKind() {
        let s = settings(glowFinished: false, glowWaiting: true)
        #expect(!decide(event(.finished, duration: 90), s).glow)
        #expect(decide(event(.waiting), s).glow)
        let s2 = settings(glowFinished: true, glowWaiting: false)
        #expect(decide(event(.finished, duration: 90), s2).glow)
        #expect(!decide(event(.waiting), s2).glow)
        #expect(!decide(event(.idle), settings(glowWaiting: false, idle: true)).glow)
    }

    @Test func theBannerAndCardSwitchesAreIndependent() {
        let noBanner = decide(event(.waiting), settings(banner: false))
        #expect(!noBanner.banner && noBanner.glow && noBanner.card)
        let noCard = decide(event(.waiting), settings(card: false))
        #expect(noCard.banner && noCard.glow && !noCard.card)
        let none = decide(event(.started), settings(card: false))
        #expect(none == NotificationDecision(banner: false, glow: false, card: false, glowColorHex: nil))
    }

    @Test func nothingInterruptingIsOnSoTheFrontAppIsNotRead() {
        let front = Front("com.apple.Terminal")
        let d = decide(event(.waiting), settings(banner: false, glowFinished: false, glowWaiting: false), front: front)
        #expect(d == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
        #expect(front.reads == 0)
    }

    // MARK: Unknown duration

    @Test func anUnknownDurationNeverInterruptsAtOneMinute() {
        let d = decide(event(.finished, duration: nil), settings(threshold: 60))
        #expect(d == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
    }

    @Test func everyTurnLetsAnUnknownOrZeroDurationThrough() {
        #expect(decide(event(.finished, duration: nil), settings(threshold: 0)).banner)
        #expect(decide(event(.finished, duration: 0), settings(threshold: 0)).banner)
        #expect(decide(event(.stopped, duration: nil), settings(threshold: 0)).banner)
    }

    @Test func theOtherThresholds() {
        #expect(decide(event(.finished, duration: 30), settings(threshold: 30)).banner)
        #expect(!decide(event(.finished, duration: 119), settings(threshold: 120)).banner)
        #expect(decide(event(.finished, duration: 120), settings(threshold: 120)).banner)
        #expect(!decide(event(.finished, duration: 299), settings(threshold: 300)).banner)
        #expect(decide(event(.finished, duration: 300), settings(threshold: 300)).banner)
    }

    @Test func theThresholdNeverHoldsAWait() {
        #expect(decide(event(.waiting), settings(threshold: 300)).banner)
    }

    // MARK: In front

    @Test func claudeAndAntigravityUseTheTerminalList() {
        for agent in [AgentSource.claude, .antigravity] {
            for id in ClaudeCodeGlowRules.terminalBundleIDs {
                #expect(NotificationRules.inFront(agent, bundleID: id, name: nil))
            }
            #expect(!NotificationRules.inFront(agent, bundleID: "com.apple.Safari", name: "Safari"))
            #expect(!NotificationRules.inFront(agent, bundleID: nil, name: nil))
            // The Codex rule does not leak to the others.
            #expect(!NotificationRules.inFront(agent, bundleID: "com.openai.codex", name: "Codex"))
        }
    }

    @Test func codexAddsItsOwnAppByNameOrOpenAIBundlePrefix() {
        #expect(NotificationRules.inFront(.codex, bundleID: "com.apple.Terminal", name: "Terminal"))
        #expect(NotificationRules.inFront(.codex, bundleID: "com.openai.codex", name: nil))
        #expect(NotificationRules.inFront(.codex, bundleID: "com.openai.anything", name: "Whatever"))
        #expect(NotificationRules.inFront(.codex, bundleID: "org.example.x", name: "Codex"))
        #expect(NotificationRules.inFront(.codex, bundleID: nil, name: " codex "))
        #expect(!NotificationRules.inFront(.codex, bundleID: "com.openai", name: "Safari"))
        #expect(!NotificationRules.inFront(.codex, bundleID: "com.apple.Safari", name: "Codex Notes"))
        #expect(!NotificationRules.inFront(.codex, bundleID: nil, name: nil))
    }

    @Test func theAgentsAppInFrontKeepsOnlyTheCard() {
        let d = decide(event(.waiting), settings(), front: Front("com.apple.Terminal"))
        #expect(d == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
        let c = decide(event(.finished, agent: .codex, duration: 200), settings(), front: Front(nil, name: "Codex"))
        #expect(!c.banner && !c.glow && c.card)
    }

    @Test func theFrontAppIsReadOnceAndOnlyWhenAnInterruptIsDue() {
        let front = Front("com.apple.Safari")
        #expect(decide(event(.waiting), settings(), front: front).banner)
        #expect(front.reads == 1)
    }

    @Test func withTheOptionOffTheFrontAppIsNotReadAndNothingIsHeld() {
        let front = Front("com.apple.Terminal")
        let d = decide(event(.waiting), settings(skipInFront: false), front: front)
        #expect(d.banner && d.glow)
        #expect(front.reads == 0)
    }

    @Test func codexInFrontDoesNotHoldAClaudeEvent() {
        let d = decide(event(.waiting), settings(), front: Front("com.openai.codex", name: "Codex"))
        #expect(d.banner)
    }

    // MARK: Limiter

    @Test func aSameKindEventAt19sIsHeldAndAt21sPasses() {
        var l = NotificationLimiter()
        let first = decide(event(.waiting, at: at(0)), settings(), limiter: &l)
        #expect(first.banner)
        do { let r = decide(event(.waiting, at: at(19)), settings(), limiter: &l).banner; #expect(!r) }
        do { let r = decide(event(.waiting, at: at(21)), settings(), limiter: &l).banner; #expect(r) }
    }

    @Test func aHeldEventStillGetsItsCard() {
        var l = NotificationLimiter()
        _ = decide(event(.waiting, at: at(0)), settings(), limiter: &l)
        let held = decide(event(.waiting, at: at(5)), settings(), limiter: &l)
        #expect(held == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
    }

    @Test func aFinishedFourSecondsAfterAWaitingIsHeldAndSixPasses() {
        var l = NotificationLimiter()
        _ = decide(event(.waiting, at: at(0)), settings(), limiter: &l)
        do { let r = decide(event(.finished, duration: 90, at: at(4)), settings(), limiter: &l).banner; #expect(!r) }
        var m = NotificationLimiter()
        _ = decide(event(.waiting, at: at(0)), settings(), limiter: &m)
        do { let r = decide(event(.finished, duration: 90, at: at(6)), settings(), limiter: &m).banner; #expect(r) }
    }

    @Test func theLimiterIsPerAgentAndSession() {
        var l = NotificationLimiter()
        _ = decide(event(.waiting, session: "a", at: at(0)), settings(), limiter: &l)
        do { let r = decide(event(.waiting, session: "b", at: at(1)), settings(), limiter: &l).banner; #expect(r) }
        do { let r = decide(event(.waiting, agent: .codex, session: "a", at: at(2)), settings(), limiter: &l).banner; #expect(r) }
        do { let r = decide(event(.waiting, session: "a", at: at(3)), settings(), limiter: &l).banner; #expect(!r) }
    }

    @Test func anEventTheFrontAppStopsDoesNotUseUpTheLimit() {
        var l = NotificationLimiter()
        let held = decide(event(.waiting, at: at(0)), settings(), front: Front("com.apple.Terminal"), limiter: &l)
        #expect(!held.banner)
        do { let r = decide(event(.waiting, at: at(1)), settings(), limiter: &l).banner; #expect(r) }
    }

    @Test func aBelowThresholdFinishDoesNotUseUpTheLimit() {
        var l = NotificationLimiter()
        _ = decide(event(.finished, duration: 10, at: at(0)), settings(), limiter: &l)
        do { let r = decide(event(.finished, duration: 90, at: at(1)), settings(), limiter: &l).banner; #expect(r) }
    }

    @Test func idleSharesTheWaitingLimit() {
        var l = NotificationLimiter()
        _ = decide(event(.waiting, at: at(0)), settings(idle: true), limiter: &l)
        do { let r = decide(event(.idle, at: at(10)), settings(idle: true), limiter: &l).banner; #expect(!r) }
    }

    @Test func theLimiterForgetsAClockSetBack() {
        var l = NotificationLimiter()
        _ = decide(event(.waiting, at: at(100)), settings(), limiter: &l)
        do { let r = decide(event(.waiting, at: at(50)), settings(), limiter: &l).banner; #expect(r) }
    }

    @Test func theLimiterKeepsAtMostSixtyFourSessions() {
        var l = NotificationLimiter()
        for i in 0..<(NotificationLimiter.maxSessions + 10) {
            do { let r = l.allow(event(.waiting, session: "s\(i)", at: at(Double(i))), now: at(Double(i))); #expect(r) }
        }
        #expect(l.sessionCount <= NotificationLimiter.maxSessions)
    }

    // MARK: Quiet hours

    func quietNow(_ start: Int, _ end: Int) -> (enabled: Bool, start: Int, end: Int) { (true, start, end) }

    @Test func quietHoursHoldBannerAndGlowButTheCardStays() {
        // now is 2026-10-01 or thereabouts in UTC; find its minute of day.
        let c = calendar.dateComponents([.hour, .minute], from: now)
        let m = c.hour! * 60 + c.minute!
        let q = quietNow(m - 30 < 0 ? 0 : m - 30, m + 30 > 1439 ? 1439 : m + 30)
        let front = Front()
        let d = decide(event(.waiting), settings(quiet: q), front: front)
        #expect(d == NotificationDecision(banner: false, glow: false, card: true, glowColorHex: nil))
        #expect(front.reads == 0)
        let f = decide(event(.finished, duration: 300), settings(quiet: q))
        #expect(!f.banner && !f.glow && f.card)
    }

    @Test func quietHoursDoNotUseUpTheLimit() {
        let c = calendar.dateComponents([.hour, .minute], from: now)
        let m = c.hour! * 60 + c.minute!
        let q = quietNow(max(0, m - 30), min(1439, m + 30))
        var l = NotificationLimiter()
        do { let r = decide(event(.waiting, at: at(0)), settings(quiet: q), limiter: &l).banner; #expect(!r) }
        do { let r = decide(event(.waiting, at: at(1)), settings(), limiter: &l).banner; #expect(r) }
    }

    @Test func outsideQuietHoursOrWithItDisabledNothingIsHeld() {
        let c = calendar.dateComponents([.hour, .minute], from: now)
        let m = c.hour! * 60 + c.minute!
        let away = quietNow((m + 120) % 1440, (m + 180) % 1440)
        #expect(decide(event(.waiting), settings(quiet: away)).banner)
        let off: (enabled: Bool, start: Int, end: Int) = (false, max(0, m - 30), min(1439, m + 30))
        #expect(decide(event(.waiting), settings(quiet: off)).banner)
    }

    // MARK: The glow is unchanged

    @Test func theGlowLimiterStillBehavesAsBefore() {
        var l = ClaudeCodeGlowLimiter()
        do { let r = l.allow(.waiting, now: now); #expect(r) }
        do { let r = l.allow(.waiting, now: at(19)); #expect(!r) }
        do { let r = l.allow(.waiting, now: at(21)); #expect(r) }
        do { let r = l.allow(.done, now: at(23)); #expect(!r) }
        do { let r = l.allow(.done, now: at(27)); #expect(r) }
    }
}

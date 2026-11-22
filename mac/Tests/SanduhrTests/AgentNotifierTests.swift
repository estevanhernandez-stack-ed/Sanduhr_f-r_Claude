import Foundation
import Testing
@testable import Sanduhr

/// Agent notifications, the surfaces (plan Task 5): the notifier calling each surface in its own failure scope,
/// the banner's words and identifier, the glow color, and what a click on one does. Limit alerts' banners and
/// clicks are pinned first, unchanged. Made-up sessions and projects only; nothing touches Notification Center.
@Suite("Agent notifier")
struct AgentNotifierTests {
    /// 2026-10-02 09:00 UTC.
    let t0 = Date(timeIntervalSince1970: 1_791_018_000)

    var utc: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    struct Boom: Error {}

    final class Recorder {
        var banners: [AgentBanner] = []
        var glows: [String?] = []
        var cards: [AgentTurnEvent] = []
        var logs: [String] = []
        var frontReads = 0
        var now: Date
        init(now: Date) { self.now = now }
    }

    private func make(_ settings: NotificationSettings = .init(banner: true, card: true, glowFinished: true,
                                                               glowWaiting: true),
                      front: String? = nil, on: Set<AgentSource> = Set(AgentSource.allCases),
                      throwing: Set<String> = []) -> (AgentNotifier, Recorder) {
        let r = Recorder(now: t0)
        let frontApp = FrontApp(bundleID: { r.frontReads += 1; return front }, name: { nil })
        let n = AgentNotifier(
            settings: { settings },
            banner: { b in if throwing.contains("banner") { throw Boom() }; r.banners.append(b) },
            glow: { c in if throwing.contains("glow") { throw Boom() }; r.glows.append(c) },
            card: { e in if throwing.contains("card") { throw Boom() }; r.cards.append(e) },
            front: frontApp, clock: { r.now },
            isOn: { on.contains($0) },
            color: { $0.defaultColor },
            calendar: utc,
            log: { r.logs.append($0) })
        return (n, r)
    }

    private func event(_ kind: AgentTurnEvent.Kind, agent: AgentSource = .claude, session: String = "s1",
                       project: String? = "alpha", duration: TimeInterval? = 252, at: Date? = nil) -> AgentTurnEvent {
        AgentTurnEvent(agent: agent, kind: kind, project: project, session: session, at: at ?? t0, duration: duration)
    }

    // MARK: Limit alerts, pinned first

    @Test func limitAlertBannerIsUnchanged() {
        let standard = Notifier.BannerSpec.alert(id: "five_hour|w|line", title: "T", body: "B", sound: AlertSound.standard)
        #expect(standard == Notifier.BannerSpec(id: "five_hour|w|line", title: "T", body: "B", thread: nil, defaultSound: true))
        // A named sound plays through NSSound, so the banner itself is silent; None is silent too.
        #expect(!Notifier.BannerSpec.alert(id: "k", title: "T", body: "B", sound: "Glass").defaultSound)
        #expect(!Notifier.BannerSpec.alert(id: "k", title: "T", body: "B", sound: AlertSound.none).defaultSound)
        #expect(Notifier.BannerSpec.alert(id: "k", title: "T", body: "B", sound: "Glass").thread == nil)
    }

    @Test func existingClicksAreUnchanged() {
        #expect(Notifier.clickTarget(for: Notifier.deskSuggestionPrefix + "p") == .settings(.message))
        #expect(Notifier.clickTarget(for: Notifier.themeSuggestionPrefix + "p") == .settings(.themes))
        #expect(Notifier.clickTarget(for: Notifier.widgetSuggestionPrefix + "p") == .settings(.widgets))
        #expect(Notifier.clickTarget(for: Notifier.spriteMotionPrefix + "p") == .settings(.widgets))
        #expect(Notifier.clickTarget(for: Notifier.deskDesignSuggestionPrefix + "p") == .settings(.deskLayout))
        #expect(Notifier.clickTarget(for: ExpiredAccountNotice.idPrefix + "x") == .settings(.credentials))
        // A limit alert (its once-key) and the test alert still do nothing on a click.
        #expect(Notifier.clickTarget(for: "five_hour|2026-10-02T10:00|line") == nil)
        #expect(Notifier.clickTarget(for: "codex|seven_day|w|pace") == nil)
        #expect(Notifier.clickTarget(for: "test-1791018000") == nil)
    }

    // MARK: The new click

    @Test func clickingAnAgentNotificationShowsTheWidget() {
        #expect(Notifier.clickTarget(for: "agent-claude-s1") == .widget)
        #expect(Notifier.clickTarget(for: "agent-codex-019a") == .widget)
        #expect(Notifier.clickTarget(for: "agent-idle-s1") == .widget)
        #expect(Notifier.clickTarget(for: AgentBanner(event(.finished)).id) == .widget)
    }

    @Test func agentBannerSpecCarriesItsThreadAndTheAlertSound() {
        let b = AgentBanner(event(.finished))
        let spec = Notifier.BannerSpec.agent(b, sound: AlertSound.standard)
        #expect(spec == Notifier.BannerSpec(id: "agent-claude-s1", title: "Claude Code finished", body: "alpha, 4m 12s",
                                            thread: "sanduhr-agents", defaultSound: true))
        #expect(!Notifier.BannerSpec.agent(b, sound: "Glass").defaultSound)
        #expect(!Notifier.BannerSpec.agent(b, sound: AlertSound.none).defaultSound)
    }

    // MARK: The banner's words

    @Test func durationFormat() {
        #expect(AgentBanner.duration(252) == "4m 12s")
        #expect(AgentBanner.duration(45) == "45s")
        #expect(AgentBanner.duration(3720) == "1h 02m")
        #expect(AgentBanner.duration(65) == "1m 05s")
        #expect(AgentBanner.duration(59.9) == "59s")
        #expect(AgentBanner.duration(0) == "0s")
        #expect(AgentBanner.duration(-5) == "0s")
        #expect(AgentBanner.duration(.infinity) == "0s")
        #expect(AgentBanner.duration(.nan) == "0s")
    }

    @Test func titlesAndBodies() {
        let fin = AgentBanner(event(.finished))
        #expect(fin.title == "Claude Code finished" && fin.body == "alpha, 4m 12s" && fin.thread == "sanduhr-agents")
        let stop = AgentBanner(event(.stopped, agent: .codex))
        #expect(stop.title == "Codex stopped" && stop.body == "alpha, 4m 12s")
        let wait = AgentBanner(event(.waiting))
        #expect(wait.title == "Claude Code is waiting on you" && wait.body == "alpha")
        let idle = AgentBanner(event(.idle))
        #expect(idle.title == "Claude Code is waiting on you" && idle.body == "alpha")
        #expect(AgentBanner(event(.finished, agent: .antigravity)).title == "Antigravity finished")
        #expect(AgentBanner(event(.finished, project: nil)).body == "4m 12s")
        #expect(AgentBanner(event(.finished, duration: nil)).body == "alpha")
        #expect(AgentBanner(event(.finished, project: "  ", duration: nil)).body == "")
        // Non-ASCII, spaces and quotes pass through as they are.
        #expect(AgentBanner(event(.waiting, project: "Café \"Ünï\" app")).body == "Café \"Ünï\" app")
    }

    @Test func identifierReplacesPerSessionAndIdleHasItsOwn() {
        #expect(AgentBanner(event(.finished)).id == "agent-claude-s1")
        #expect(AgentBanner(event(.waiting)).id == "agent-claude-s1")
        #expect(AgentBanner(event(.finished, session: "s2")).id == "agent-claude-s2")
        #expect(AgentBanner(event(.finished, agent: .codex)).id == "agent-codex-s1")
        #expect(AgentBanner(event(.idle)).id == "agent-idle-s1")
        #expect(AgentBanner(event(.idle)).id != AgentBanner(event(.finished)).id)
    }

    @Test func cardTitles() {
        #expect(AgentBanner.cardTitle(.claude, project: "alpha") == "Claude Code · alpha")
        #expect(AgentBanner.cardTitle(.codex, project: nil) == "Codex")
        #expect(AgentBanner.cardTitle(.antigravity, project: " ") == "Antigravity")
    }

    // MARK: The notifier

    @Test func eachSurfaceHonorsItsDecision() {
        let (n, r) = make()
        let d = n.handle(event(.finished, duration: 120))
        #expect(d == NotificationDecision(banner: true, glow: true, card: true, glowColorHex: "#E0875A"))
        #expect(r.banners.map(\.id) == ["agent-claude-s1"])
        #expect(r.glows == ["#E0875A"])
        #expect(r.cards.count == 1)

        let (quiet, q) = make(.init(banner: false, card: false, glowFinished: true))
        quiet.handle(event(.finished, duration: 120))
        #expect(q.banners.isEmpty && q.cards.isEmpty && q.glows == ["#E0875A"])

        let (cardOnly, c) = make(.init(banner: false, card: true))
        cardOnly.handle(event(.finished, duration: 120))
        #expect(c.banners.isEmpty && c.glows.isEmpty && c.cards.count == 1)
    }

    @Test func startedOnlyMakesACardAndNeverReadsTheFrontApp() {
        let (n, r) = make()
        n.handle(event(.started, duration: nil))
        #expect(r.cards.map(\.kind) == [.started])
        #expect(r.banners.isEmpty && r.glows.isEmpty)
        #expect(r.frontReads == 0)
    }

    @Test func aThrowingSurfaceDoesNotStopTheOthers() {
        let (n, r) = make(throwing: ["banner"])
        n.handle(event(.finished, duration: 120))
        #expect(r.glows == ["#E0875A"] && r.cards.count == 1)
        #expect(r.logs.count == 1)
        #expect(r.logs.first?.contains("banner") == true && r.logs.first?.contains("Boom") == true)
        // The log names the surface and the error's type, never the project.
        #expect(r.logs.first?.contains("alpha") == false)

        let (n2, r2) = make(throwing: ["card", "glow"])
        n2.handle(event(.finished, duration: 120))
        #expect(r2.banners.count == 1 && r2.logs.count == 2)
    }

    @Test func glowColorForFinishedAndStoppedOnlyInkForWaiting() {
        let (n, r) = make()
        n.handle(event(.finished, agent: .codex, duration: 120))
        n.handle(event(.waiting, session: "s2"))
        n.handle(event(.stopped, agent: .antigravity, session: "s3", duration: 120))
        #expect(r.glows == ["#2FB5A3", nil, "#7B8CF0"])
    }

    @Test func anAgentWhoseSourceIsOffRaisesNothing() {
        let (n, r) = make(on: [.claude])
        #expect(n.handle(event(.finished, agent: .codex, duration: 120)) == .none)
        #expect(n.handle(event(.started, agent: .antigravity, duration: nil)) == .none)
        #expect(r.banners.isEmpty && r.glows.isEmpty && r.cards.isEmpty)
        n.handle(event(.finished, duration: 120))
        #expect(r.banners.count == 1)
    }

    @Test func idleHasItsOwnBannerAndNoCard() {
        var s = NotificationSettings(banner: true, card: true, glowWaiting: true)
        s.idle = true
        let (n, r) = make(s)
        n.handle(event(.idle))
        #expect(r.banners.map(\.id) == ["agent-idle-s1"])
        #expect(r.glows == [nil])
        #expect(r.cards.isEmpty)
    }

    @Test func aTerminalInFrontHoldsTheBannerAndGlowNotTheCard() {
        let (n, r) = make(front: "com.apple.Terminal")
        n.handle(event(.finished, duration: 120))
        #expect(r.banners.isEmpty && r.glows.isEmpty && r.cards.count == 1)
        #expect(r.frontReads == 1)
    }

    /// Review Focus 4: the Mac sleeps mid-turn and wakes after the threshold. The finish that arrives after wake,
    /// read twice (the file event and the poll that follows the wake), notifies once.
    @Test func sleepAndWakeNotifiesOnce() {
        let (n, r) = make()
        n.handle(event(.started, duration: nil, at: t0))
        r.now = t0 + 7200   // asleep for two hours
        let fin = event(.finished, duration: 7200, at: t0 + 7200)
        n.handle(fin)
        r.now = t0 + 7230   // past the limiter's 20 s: only the repeat check can hold it
        n.handle(fin)
        #expect(r.banners.count == 1)
        #expect(r.banners.first?.body == "alpha, 2h 00m")
        #expect(r.glows.count == 1)
        #expect(r.cards.map(\.kind) == [.started, .finished])
        // A new turn in the same session afterwards still notifies.
        r.now = t0 + 7400
        n.handle(event(.finished, duration: 120, at: t0 + 7400))
        #expect(r.banners.count == 2)
    }
}

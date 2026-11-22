import Foundation
import Testing
@testable import Sanduhr

/// Agent notifications, the Desk turn card (plan Task 5): a card per agent and session on the watcher board, its
/// running time, its finish and fade, lost touch, the keep-alive, an agent switched off, demo mode, and the band
/// that never carries one. The board's other cards are pinned unchanged. Made-up sessions and projects only.
@Suite("Watcher turn cards")
struct WatcherTurnCardsTests {
    let t0 = Date(timeIntervalSince1970: 1_790_000_000)

    private func event(_ kind: AgentTurnEvent.Kind, agent: AgentSource = .claude, session: String = "s1",
                       project: String? = "alpha", duration: TimeInterval? = nil, at: Date? = nil) -> AgentTurnEvent {
        AgentTurnEvent(agent: agent, kind: kind, project: project, session: session, at: at ?? t0, duration: duration)
    }

    @Test func startPutsUpARunningCard() {
        var b = WatcherBoard()
        let r1 = b.applyTurn(event(.started), now: t0)
        #expect(r1)
        let w = b.watcher("t:claude:s1")
        #expect(w?.source == .turn && w?.state == .running)
        #expect(w?.title == "Claude Code · alpha" && w?.short == "CC")
        #expect(w?.kind == "claude" && w?.session == "s1")
        #expect(w.map { WatcherText.elapsed($0.elapsed(now: t0 + 125)) } == "2m")
        #expect(w.map(WatcherText.deskNote) == .some(nil))
    }

    @Test func finishShowsTheTurnsLengthThenFadesAfterAMinute() {
        var b = WatcherBoard()
        b.applyTurn(event(.started), now: t0)
        let r2 = b.applyTurn(event(.finished, duration: 252, at: t0 + 252), now: t0 + 252)
        #expect(r2)
        let w = b.watcher("t:claude:s1")!
        #expect(w.state == .finished && w.ended == t0 + 252)
        #expect(WatcherText.elapsed(w.elapsed(now: t0 + 400)) == "4m")
        #expect(WatcherText.deskNote(w) == "finished")
        #expect(b.nextChange() == t0 + 252 + 60)
        let r3 = b.tick(now: t0 + 252 + 59)
        #expect(!r3)
        #expect(b.watcher("t:claude:s1") != nil)
        let r4 = b.tick(now: t0 + 252 + 60)
        #expect(r4)
        #expect(b.watcher("t:claude:s1") == nil)
    }

    @Test func stoppedSaysSo() {
        var b = WatcherBoard()
        b.applyTurn(event(.started, agent: .codex), now: t0)
        b.applyTurn(event(.stopped, agent: .codex, duration: 30), now: t0 + 30)
        let w = b.watcher("t:codex:s1")!
        #expect(w.state == .finished && w.note == "stopped" && w.title == "Codex · alpha" && w.short == "Codex")
        #expect(WatcherText.deskNote(w) == "stopped")
    }

    @Test func waitingMarksTheCardAndKeepsItsStart() {
        var b = WatcherBoard()
        b.applyTurn(event(.started), now: t0)
        let r5 = b.applyTurn(event(.waiting), now: t0 + 40)
        #expect(r5)
        let w = b.watcher("t:claude:s1")!
        #expect(w.state == .waiting && w.started == t0 && w.touched == t0 + 40)
    }

    @Test func aFinishWithNoCardMakesOne() {
        var b = WatcherBoard()
        b.applyTurn(event(.finished, duration: 90), now: t0 + 500)
        let w = b.watcher("t:claude:s1")!
        #expect(w.state == .finished && w.started == t0 + 410 && w.elapsed(now: t0 + 600) == 90)
    }

    @Test func idleIsNeverACard() {
        var b = WatcherBoard()
        let r6 = b.applyTurn(event(.idle), now: t0)
        #expect(!r6)
        #expect(b.watchers.isEmpty)
        b.applyTurn(event(.started), now: t0)
        let r7 = b.applyTurn(event(.idle), now: t0 + 10)
        #expect(!r7)
        #expect(b.watcher("t:claude:s1")?.state == .running)
    }

    @Test func twoSessionsInOneFolderHaveTwoCards() {
        var b = WatcherBoard()
        b.applyTurn(event(.started, session: "s1"), now: t0)
        b.applyTurn(event(.started, session: "s2", at: t0 + 30), now: t0 + 30)
        b.applyTurn(event(.finished, session: "s1", duration: 100), now: t0 + 100)
        #expect(b.watcher("t:claude:s1")?.state == .finished)
        #expect(b.watcher("t:claude:s2")?.state == .running && b.watcher("t:claude:s2")?.started == t0 + 30)
    }

    @Test func aRunningCardLosesTouchAfterAnHourAndLeavesAMinuteLater() {
        var b = WatcherBoard()
        b.applyTurn(event(.started), now: t0)
        #expect(b.nextChange() == t0 + 3601)
        let r8 = b.tick(now: t0 + 3600)
        #expect(!r8)
        let r9 = b.tick(now: t0 + 3601)
        #expect(r9)
        #expect(b.watcher("t:claude:s1")?.state == .lostTouch)
        #expect(b.nextChange() == t0 + 3661)
        let r10 = b.tick(now: t0 + 3660)
        #expect(!r10)
        let r11 = b.tick(now: t0 + 3661)
        #expect(r11)
        #expect(b.watcher("t:claude:s1") == nil)
    }

    @Test func antigravityLosesTouchAfterTenMinutes() {
        var b = WatcherBoard()
        b.applyTurn(event(.started, agent: .antigravity), now: t0)
        let r12 = b.tick(now: t0 + 601)
        #expect(r12)
        #expect(b.watcher("t:antigravity:s1")?.state == .lostTouch)
    }

    @Test func aKeepAliveRefreshesTheRunningCard() {
        var b = WatcherBoard()
        b.applyTurn(event(.started), now: t0)
        // The watcher's keep-alive is a .started at the current time for a session already running (D6).
        b.applyTurn(event(.started, at: t0 + 3000), now: t0 + 3000)
        let w = b.watcher("t:claude:s1")!
        #expect(w.state == .running && w.started == t0 && w.touched == t0 + 3000)
        let r13 = b.tick(now: t0 + 3601)
        #expect(!r13)
        // A card that lost touch comes back running on a keep-alive, its start kept.
        b.tick(now: t0 + 6601)
        #expect(b.watcher("t:claude:s1")?.state == .lostTouch)
        b.applyTurn(event(.started, at: t0 + 6620), now: t0 + 6620)
        let back = b.watcher("t:claude:s1")!
        #expect(back.state == .running && back.started == t0 && back.ended == nil)
        // A waiting card stays waiting.
        b.applyTurn(event(.waiting), now: t0 + 6630)
        b.applyTurn(event(.started, at: t0 + 6640), now: t0 + 6640)
        #expect(b.watcher("t:claude:s1")?.state == .waiting)
    }

    @Test func aStartAfterAFinishIsANewTurn() {
        var b = WatcherBoard()
        b.applyTurn(event(.started), now: t0)
        b.applyTurn(event(.finished, duration: 50), now: t0 + 50)
        b.applyTurn(event(.started, at: t0 + 70), now: t0 + 70)
        let w = b.watcher("t:claude:s1")!
        #expect(w.state == .running && w.started == t0 + 70 && w.ended == nil)
    }

    /// Review Focus 4: the Mac slept mid-turn. A duration that can't be believed (negative, endless) never moves the
    /// start, and the running time is never negative, even with the clock set back.
    @Test func sleepAndWakeNeverShowANegativeOrHugeTime() {
        var b = WatcherBoard()
        b.applyTurn(event(.started), now: t0)
        #expect(b.watcher("t:claude:s1")!.elapsed(now: t0 - 500) == 0)
        b.applyTurn(event(.finished, duration: -40), now: t0 + 7200)
        #expect(b.watcher("t:claude:s1")!.started == t0)
        #expect(b.watcher("t:claude:s1")!.elapsed(now: t0 + 7300) == 7200)

        var c = WatcherBoard()
        c.applyTurn(event(.finished, duration: .infinity), now: t0)
        #expect(c.watcher("t:claude:s1")!.started == t0)
        c.applyTurn(event(.finished, session: "s2", duration: 400 * 86_400), now: t0)
        #expect(c.watcher("t:claude:s2")!.started == t0)
        // A start stamped ahead of the clock starts now, never in the future.
        c.applyTurn(event(.started, session: "s3", at: t0 + 50), now: t0)
        #expect(c.watcher("t:claude:s3")!.started == t0)
    }

    @Test func switchingAnAgentOffClearsOnlyItsTurnCards() {
        var b = WatcherBoard()
        b.apply(.start(id: "w1", title: "CI", link: nil, total: nil, work: false), now: t0)
        b.applyTurn(event(.started, agent: .claude), now: t0)
        b.applyTurn(event(.started, agent: .codex), now: t0)
        let r14 = b.clearTurns(.codex)
        #expect(r14)
        #expect(b.watcher("t:codex:s1") == nil && b.watcher("t:claude:s1") != nil && b.watcher("a:w1") != nil)
        let r15 = b.clearTurns(.codex)
        #expect(!r15)
    }

    @Test func demoModeHidesTurnCards() {
        var b = WatcherBoard()
        b.apply(.start(id: "w1", title: "CI", link: nil, total: nil, work: false), now: t0)
        b.applyTurn(event(.started), now: t0)
        #expect(WatcherBoard.shown(b.ordered(), demo: true).map(\.id) == ["a:w1"])
        #expect(WatcherBoard.shown(b.ordered(), demo: false).count == 2)
    }

    @Test func theBandNeverCarriesATurnCard() {
        var b = WatcherBoard()
        b.apply(.start(id: "w1", title: "CI", link: nil, total: nil, work: false), now: t0)
        b.applyTurn(event(.started), now: t0)
        #expect(BandFile.carried(b.ordered()).map(\.id) == ["a:w1"])
    }

    /// Pinned: the other cards fade, lose touch and order as before.
    @Test func otherCardsUnchanged() {
        #expect(WatcherBoard.window(.agent) == 600 && WatcherBoard.window(.automatic) == 3600)
        #expect(WatcherBoard.window(.runner) == nil && WatcherBoard.fadeAfter == 6)
        var b = WatcherBoard()
        b.apply(.start(id: "w1", title: "CI", link: nil, total: nil, work: false), now: t0)
        b.apply(.end(id: "w1", result: .passed, note: nil), now: t0 + 10)
        #expect(b.nextChange() == t0 + 16)
        let r16 = b.tick(now: t0 + 16)
        #expect(r16 && b.watchers.isEmpty)
    }

    // MARK: The store

    @MainActor
    @Test func theStoreAppliesTurnsAndClearsAnAgent() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-turns-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        var clock = t0
        let store = WatcherStore(support: dir, agents: { false }, background: { false }, now: { clock })
        var changes = 0
        store.onChange = { changes += 1 }
        store.applyTurn(event(.started))
        #expect(changes == 1 && store.ordered.map(\.id) == ["t:claude:s1"])
        clock = t0 + 5
        store.applyTurn(event(.idle))
        #expect(changes == 1)
        store.clear(agent: .codex)
        #expect(changes == 1)
        store.clear(agent: .claude)
        #expect(changes == 2 && store.ordered.isEmpty)
        // The agents' and background switches don't gate turn cards.
        store.applySwitches()
        store.applyTurn(event(.started, agent: .codex))
        #expect(store.ordered.map(\.id) == ["t:codex:s1"])
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// A Codex alert pulses the Desk pieces that show Codex (2.48.0, item 2; D9 and D12 of the 2.46 run lifted).
/// Pulses are keyed by source and limit; a Claude-only piece never answers to Codex, and Claude's pulses are
/// as they were. Made-up numbers only; nothing reads `~/.codex`.

private let now = Date(timeIntervalSince1970: 1_800_000_000)

private func tier(_ util: Double, in seconds: TimeInterval = 3600) -> TierUsage {
    TierUsage(utilization: util, resetsAt: ISO8601.stamp(now.addingTimeInterval(seconds), fractional: false))
}

private let claudeUsage = UsageResponse(tiers: [.fiveHour: tier(40), .sevenDay: tier(63, in: 86_400), .sevenDaySonnet: tier(18)])
private let codexUsage = UsageResponse(tiers: [.fiveHour: tier(30, in: 7200), .sevenDay: tier(70, in: 3 * 86_400)])

private func input(codexOn: Bool = true, usage: UsageResponse? = codexUsage) -> DeskSourceInput {
    DeskSourceInput(codexOn: codexOn,
                    codex: CodexWidgetReading(usage: usage, asOf: now, freshTiers: [.fiveHour, .sevenDay], emptyState: nil),
                    primary: .claude, colors: [.claude: "#E0875A", .codex: "#2FB5A3"])
}

@MainActor
private func model(_ source: DeskSourceInput, claude: UsageResponse? = claudeUsage) -> DeskModel {
    let m = DeskModel()
    m.updateSources(source, now: now)
    m.update(DeskUsage(usage: claude, fetchedAt: now), now: now)
    return m
}

private func key(_ source: AgentSource, _ tier: Tier) -> DeskPulseKey { DeskPulseKey(source: source, tier: tier) }

@Suite("Desk pulses: keyed by source and limit")
struct DeskPulseKeyTests {
    private func row(_ tier: Tier, joined: Bool) -> DeskMeterRow {
        var r = DeskMeterRow.rows(from: claudeUsage, now: now).first { $0.tier == tier }!
        if joined {
            r.layout = SourceBar.layout(primary: .claude, window: .fiveHour, points: [
                SourceBarPoint(source: .claude, percent: 40, colorHex: "#E0875A"),
                SourceBarPoint(source: .codex, percent: 30, colorHex: "#2FB5A3")])
        }
        return r
    }

    @MainActor
    @Test func aPulseCountsUnderItsOwnSourceAndLimit() {
        let m = DeskModel()
        m.pulse([.fiveHour, .sevenDay], source: .codex)
        m.pulse([.fiveHour])
        #expect(m.pulses == [key(.codex, .fiveHour): 1, key(.codex, .sevenDay): 1, key(.claude, .fiveHour): 1])
        #expect(m.pulseCount == 2)
    }

    @MainActor
    @Test func aPulseWithNoSourceIsClaudesAsItAlwaysWas() {
        let m = DeskModel()
        m.pulse([.sevenDay])
        #expect(m.pulses == [key(.claude, .sevenDay): 1])
    }

    @Test func aClaudePieceReadsOnlyClaudesPulses() {
        let pulses = [key(.claude, .fiveHour): 2, key(.codex, .fiveHour): 5]
        #expect(DeskPulses.trigger(pulses, drawn: .claude, row: row(.fiveHour, joined: false)) == 2)
        #expect(DeskPulses.trigger([key(.codex, .fiveHour): 5], drawn: .claude, row: row(.fiveHour, joined: false)) == 0)
    }

    @Test func aCodexPieceReadsOnlyCodexsPulses() {
        let pulses = [key(.claude, .fiveHour): 2, key(.codex, .fiveHour): 5]
        #expect(DeskPulses.trigger(pulses, drawn: .codex, row: row(.fiveHour, joined: false)) == 5)
        #expect(DeskPulses.trigger([key(.claude, .fiveHour): 2], drawn: .codex, row: row(.fiveHour, joined: false)) == 0)
    }

    @Test func aCombinedRowAnswersToCodexOnlyWhereItIsJoined() {
        let pulses = [key(.codex, .fiveHour): 1]
        #expect(DeskPulses.trigger(pulses, drawn: .combined, row: row(.fiveHour, joined: true)) == 1)
        #expect(DeskPulses.trigger(pulses, drawn: .combined, row: row(.fiveHour, joined: false)) == 0)
        // Claude's pulse lights every combined row, as it lights every Claude one.
        let claude = [key(.claude, .fiveHour): 1]
        #expect(DeskPulses.trigger(claude, drawn: .combined, row: row(.fiveHour, joined: true)) == 1)
        #expect(DeskPulses.trigger(claude, drawn: .combined, row: row(.fiveHour, joined: false)) == 1)
    }

    @Test func aLimitsPulseLeavesTheOthersStill() {
        let pulses = [key(.codex, .fiveHour): 1]
        #expect(DeskPulses.trigger(pulses, drawn: .codex, row: row(.sevenDay, joined: false)) == 0)
    }
}

@Suite("Desk pulses: which pieces show a source")
struct DeskPulseTargetTests {
    @MainActor
    private func canPulse(_ layout: String, codex: DeskSourceInput = input(), _ source: AgentSource = .codex) -> Bool {
        model(codex).canPulse(source, saved: layout)
    }

    @MainActor
    @Test func aCodexPieceTakesACodexPulse() {
        #expect(canPulse("meters:bl:1:0:bar:src=codex"))
    }

    @MainActor
    @Test func aCombinedPieceWithAJoinedRowTakesACodexPulse() {
        #expect(canPulse("meters:bl"))
    }

    @MainActor
    @Test func aClaudeOnlyPieceNeverTakesACodexPulse() {
        #expect(!canPulse("meters:bl:1:0:bar:src=claude"))
        #expect(!canPulse("clock:bl"))
    }

    @MainActor
    @Test func aCombinedPieceWithNoCodexNumbersDoesNot() {
        // Codex is on but read nothing: the piece is Claude as today.
        #expect(!canPulse("meters:bl", codex: input(usage: nil)))
        #expect(!canPulse("meters:bl", codex: input(usage: UsageResponse())))
    }

    @MainActor
    @Test func aCombinedPieceWithNoClaudeDrawsCodexAloneSoItTakesACodexPulse() {
        let m = model(input(), claude: nil)
        #expect(m.canPulse(.codex, saved: "meters:bl"))
        #expect(!m.canPulse(.codex, saved: "meters:bl:1:0:bar:src=claude"))
    }

    @MainActor
    @Test func aCodexPieceWithNoCodexNumbersHasNothingToPulse() {
        #expect(!canPulse("meters:bl:1:0:bar:src=codex", codex: input(usage: nil)))
    }

    @MainActor
    @Test func withCodexOffNothingPulsesForIt() {
        #expect(!canPulse("meters:bl:1:0:bar:src=codex", codex: input(codexOn: false)))
        #expect(!canPulse("meters:bl", codex: .claudeOnly))
    }

    @MainActor
    @Test func theClaudeLinePieceIsNotAMeter() {
        #expect(!canPulse("claude:bl:1:0:bar:src=codex"))
    }

    @MainActor
    @Test func anyOneOfSeveralPiecesWillDo() {
        #expect(canPulse("meters:bl:1:0:bar:src=claude meters:tr:1:0:bar:src=codex"))
    }

    @MainActor
    @Test func claudesPulseHasAPlaceWhereverTheDeskRuns() {
        #expect(canPulse("clock:bl", .claude))
        #expect(canPulse("meters:bl:1:0:bar:src=codex", .claude))
    }
}

@Suite("Desk pulses: a Codex alert's route")
struct DeskCodexRouteTests {
    private let at = parseISO("2026-11-09T12:00:00Z")!

    private var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.locale = Locale(identifier: "en_US_POSIX")
        return c
    }

    private func run(_ source: AgentSource, delivery: AlertDelivery, desk: Bool, quiet: Bool = false) -> AlertOutcome {
        var s = AlertSettings()
        s.enabled = true
        s.codexAlerts = true
        s.delivery = delivery
        if quiet { s.quietEnabled = true; s.quietStart = 11 * 60; s.quietEnd = 13 * 60 }
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 95, resetsAt: "2026-11-09T15:00:00Z")
        return AlertRules.evaluate(usage: u, previous: nil, settings: s, fired: [], deskRunning: desk, now: at,
                                   source: source, freshTiers: [.fiveHour], calendar: calendar)
    }

    @Test func aCodexAlertPulsesWhenTheDeskCanShowIt() {
        let desk = run(.codex, delivery: .desk, desk: true)
        #expect(desk.pulse && !desk.banner)
        let both = run(.codex, delivery: .both, desk: true)
        #expect(both.pulse && both.banner)
    }

    @Test func bannerOnlyDeliveryNeverPulses() {
        let out = run(.codex, delivery: .banner, desk: true)
        #expect(out.banner && !out.pulse)
    }

    @Test func withNoDeskPieceForCodexItFallsBackToABanner() {
        let out = run(.codex, delivery: .desk, desk: false)
        #expect(out.banner && !out.pulse)
    }

    @Test func quietHoursHoldTheBannerAndLeaveTheCodexPulseAlone() {
        let both = run(.codex, delivery: .both, desk: true, quiet: true)
        #expect(both.quiet && !both.banner && both.pulse)
        let desk = run(.codex, delivery: .desk, desk: true, quiet: true)
        #expect(!desk.banner && desk.pulse)
    }

    @Test func claudesRouteIsUnchanged() {
        let desk = run(.claude, delivery: .desk, desk: true)
        #expect(desk.pulse && !desk.banner)
        let both = run(.claude, delivery: .both, desk: true, quiet: true)
        #expect(!both.banner && both.pulse)
        let fallback = run(.claude, delivery: .desk, desk: false)
        #expect(fallback.banner && !fallback.pulse)
        let banner = run(.claude, delivery: .banner, desk: true)
        #expect(banner.banner && !banner.pulse)
    }
}

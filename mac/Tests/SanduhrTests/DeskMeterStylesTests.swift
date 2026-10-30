import Foundation
import Testing
@testable import Sanduhr

/// Meter styles (2.37.0 item 3), ported from Windows' DeskMeterStylesTests (LitTicks, ShortLabel,
/// the Claude line's compact form, the pieces that have a style) plus what each style shows.
@Suite("Desk meter styles")
struct DeskMeterStylesTests {
    @Test(arguments: [(0.0, 0), (0.004, 1), (0.34, 3), (0.35, 4), (0.93, 9), (1.0, 10), (1.4, 10), (Double.nan, 0)])
    func ticksLightToTheNearestCellAndAnyReadingLightsOne(_ c: (Double, Int)) {
        #expect(DeskMeterStyleModel.litTicks(c.0) == c.1)
    }

    @Test func aRingCarriesAShortName() {
        #expect(DeskMeterStyleModel.shortLabel(.fiveHour) == "Session")
        #expect(DeskMeterStyleModel.shortLabel(.sevenDay) == "Week")
        #expect(DeskMeterStyleModel.shortLabel(.sevenDaySonnet) == "Sonnet")
        #expect(DeskMeterStyleModel.shortLabel(.sevenDayOpus) == "Opus")
    }

    private func usage(_ tiers: [Tier: TierUsage], account: String? = nil) -> DeskUsage {
        var u = DeskUsage()
        u.usage = UsageResponse(tiers: tiers, extraUsage: nil)
        u.fetchedAt = Date()
        u.account = account
        return u
    }

    @Test func theClaudeLinesCompactFormIsTwoShortRows() {
        let now = Date()
        let iso = ISO8601DateFormatter().string(from: now.addingTimeInterval(3 * 3600))
        let both = usage([.fiveHour: TierUsage(utilization: 7.9, resetsAt: iso), .sevenDay: TierUsage(utilization: 63, resetsAt: nil)])
        let utc = TimeZone(identifier: "UTC")
        let rows = DeskClaudeCompact.make(both, zone: utc)!.lines
        #expect(rows.count == 2)
        #expect(rows[0] == "claude   7% session")
        #expect(rows[1].hasPrefix("resets ") && rows[1].hasSuffix("   63% week"))
        #expect(rows.map(\.count).max()! < Int(Double(DeskClaudeText.line(both)!.count) * 0.65))

        let weekOnly = usage([.sevenDay: TierUsage(utilization: 63, resetsAt: nil)])
        #expect(DeskClaudeCompact.make(weekOnly)!.lines == ["claude", "63% week"])
        let sessionOnly = usage([.fiveHour: TierUsage(utilization: 7, resetsAt: nil)], account: "Work")
        #expect(DeskClaudeCompact.make(sessionOnly)!.lines == ["Work   7% session"])
        var signIn = both
        signIn.signInNeeded = true
        #expect(DeskClaudeCompact.make(signIn)!.lines == ["claude", "sign in again in Sanduhr"])
        signIn.firstSignIn = true
        #expect(DeskClaudeCompact.make(signIn)!.lines == ["claude", "sign in to Sanduhr"])
        signIn.signInReminder = false
        #expect(DeskClaudeCompact.make(signIn) == nil)
        #expect(DeskClaudeCompact.make(DeskUsage()) == nil)
    }

    @Test func onlyTheMeterLikePiecesHaveAStyle() {
        #expect(DeskMeterStyles.pieces == ["claude", "meters", "telemetry"])
        #expect(DeskMeterStyle.allCases.map(DeskMeterStyles.name) == ["Bars", "Slim", "Rings", "Ticks"])
    }

    // MARK: what each style shows

    @Test func slimRingAndTicksLeaveOutTheResetLineAndBarKeepsIt() {
        #expect(DeskMeterStyleModel.showsReset(.bar))
        for s in [DeskMeterStyle.slim, .ring, .ticks] { #expect(!DeskMeterStyleModel.showsReset(s)) }
        for s in DeskMeterStyle.allCases {
            #expect(DeskMeterStyleModel.showsPaceMark(s))
            #expect(DeskMeterStyleModel.showsWarning(s))
        }
        #expect(DeskMeterStyleModel.percentText(42, style: .ring) == "42")
        #expect(DeskMeterStyleModel.percentText(42, style: .slim) == "42%")
        #expect(DeskMeterStyleModel.claudeForm(.bar) == .line)
        #expect(DeskMeterStyleModel.claudeForm(.slim) == .compactRows)
        #expect(DeskMeterStyleModel.claudeForm(.ring) == .gauges)
        #expect(DeskMeterStyleModel.claudeForm(.ticks) == .gauges)
    }

    @Test func gaugesCarryTheRowsPaceAndWarning() {
        let now = Date()
        let iso = ISO8601DateFormatter().string(from: now.addingTimeInterval(2.5 * 3600))
        let u = UsageResponse(tiers: [.fiveHour: TierUsage(utilization: 95, resetsAt: iso),
                                      .sevenDay: TierUsage(utilization: 40, resetsAt: nil),
                                      .sevenDaySonnet: TierUsage(utilization: 10, resetsAt: nil)], extraUsage: nil)
        let rows = DeskMeterRow.rows(from: u, now: now) { _ in MeterWarningSettings(enabled: true, threshold: 90, minReset: 0) }
        let g = DeskMeterStyleModel.gauges(rows)
        #expect(g.map(\.short) == ["Session", "Week", "Sonnet"])
        #expect(g[0].pace != nil && g[0].warning && g[0].percent == 95)
        #expect(g[1].pace == nil && !g[1].warning)
        let c = DeskMeterStyleModel.claudeGauges(rows, signInNeeded: false)
        #expect(c.map(\.label) == ["session", "week"])
        #expect(c[0].pace == g[0].pace && c[0].warning)
        #expect(DeskMeterStyleModel.claudeGauges(rows, signInNeeded: true).isEmpty)
    }

    @Test func geometryMapsPaceIntoEachShape() {
        #expect(DeskMeterStyleModel.ringPaceAngle(0) == 135)
        #expect(DeskMeterStyleModel.ringPaceAngle(0.5) == 270)
        #expect(DeskMeterStyleModel.ringPaceAngle(1) == 405)
        #expect(DeskMeterStyleModel.ringTrim(1) == 0.75)
        #expect(DeskMeterStyleModel.ringTrim(0) == 0)
        #expect(DeskMeterStyleModel.ringTrim(.nan) == 0)
        #expect(DeskMeterStyleModel.markOffset(0, width: 100) == 0)
        #expect(DeskMeterStyleModel.markOffset(0.5, width: 100) == 49)
        #expect(DeskMeterStyleModel.markOffset(1, width: 100) == 98)
        #expect(DeskMeterStyleModel.slimFillWidth(1.4, width: 100) == 100)
    }

    // MARK: Settings

    @Test func theStylePickerShowsUnderPlacedStylePiecesOnly() {
        #expect(DeskMeterStyleSettings.shows("claude", in: "clock:bl claude:bl"))
        #expect(!DeskMeterStyleSettings.shows("meters", in: "clock:bl claude:bl"))
        #expect(!DeskMeterStyleSettings.shows("clock", in: "clock:bl"))
        #expect(DeskMeterStyleSettings.anchorWidget(in: "meters:tr claude:bl") == "claude")
        #expect(DeskMeterStyleSettings.anchorWidget(in: "clock:bl") == nil)
        var a = DeskArrangement("claude:bl meters:tr")
        a.setStyle("meters", .ring)
        #expect(a.string == "claude:bl meters:tr:1:0:ring")
    }

    @Test func searchFindsTheStyle() {
        for word in ["meter style", "ring", "gauge", "ticks", "slim"] {
            let hit = SettingsSearch.hits(word).first { $0.entry.anchor == SettingsAnchor.meterStyle }
            #expect(hit != nil, "\(word)")
            #expect(hit?.entry.page == .deskLayout)
        }
    }
}

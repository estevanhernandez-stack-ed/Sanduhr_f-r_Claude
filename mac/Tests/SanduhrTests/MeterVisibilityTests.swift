import Foundation
import Testing
@testable import Sanduhr

/// Hidden limits (item 38): the one filter the widget, Desk and the alerts read. Settings come
/// from an in-memory store, never the real defaults.
@Suite("Meter visibility")
struct MeterVisibilityTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    func usage(_ tiers: [Tier: Double]) -> UsageResponse {
        var u = UsageResponse()
        for (tier, util) in tiers {
            u.tiers[tier] = TierUsage(utilization: util, resetsAt: ISO8601DateFormatter().string(from: now.addingTimeInterval(3 * 86400)))
        }
        return u
    }

    @Test func everyLimitShowsByDefault() {
        #expect(MeterVisibility.hidden(in: MemoryDefaults()).isEmpty)
        #expect(MeterVisibility.showKey(.iguanaNecktie) == "meterShow.iguana_necktie")
    }

    @Test func sessionAndWeeklyCannotBeHidden() {
        let d = MemoryDefaults()
        d.set(false, forKey: MeterVisibility.showKey(.fiveHour))
        d.set(false, forKey: MeterVisibility.showKey(.sevenDay))
        d.set(false, forKey: MeterVisibility.showKey(.iguanaNecktie))
        d.set(true, forKey: MeterVisibility.showKey(.sevenDayOpus))
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        #expect(!MeterVisibility.canHide(.fiveHour) && !MeterVisibility.canHide(.sevenDay))
        for tier in Tier.allCases where tier != .fiveHour && tier != .sevenDay {
            #expect(MeterVisibility.canHide(tier))
        }
        let all = usage([.fiveHour: 10, .sevenDay: 20])
        #expect(MeterVisibility.visible(all, hidden: [.fiveHour, .sevenDay])?.tiers.count == 2)
    }

    @Test func filterTakesOutHiddenLimits() {
        let u = usage([.fiveHour: 10, .sevenDay: 20, .iguanaNecktie: 100, .sevenDayOpus: 50])
        let shown = MeterVisibility.visible(u, hidden: [.iguanaNecktie])
        #expect(Set(shown.map { Array($0.tiers.keys) } ?? []) == [.fiveHour, .sevenDay, .sevenDayOpus])
        #expect(MeterVisibility.visible(u, hidden: [])?.tiers.count == 4)
        #expect(MeterVisibility.visible(nil, hidden: [.iguanaNecktie]) == nil)
    }

    @Test func hiddenLimitLeavesDeskWarningsAndAlerts() {
        let u = usage([.fiveHour: 10, .sevenDay: 20, .iguanaNecktie: 100])
        let shown = MeterVisibility.visible(u, hidden: [.iguanaNecktie])
        #expect(DeskMeterRow.rows(from: shown, now: now).map(\.tier) == [.fiveHour, .sevenDay])
        #expect(MeterWarning.tiers(u, now: now).contains(.iguanaNecktie))
        #expect(MeterWarning.tiers(shown, now: now).isEmpty)

        var settings = AlertSettings()
        settings.enabled = true
        let loud = AlertRules.evaluate(usage: u, previous: nil, settings: settings, fired: [],
                                       deskRunning: false, now: now)
        #expect(loud.alerts.map(\.tier) == [.iguanaNecktie])
        let quiet = AlertRules.evaluate(usage: u, previous: nil, settings: settings, fired: [],
                                        deskRunning: false, now: now, hidden: [.iguanaNecktie])
        #expect(quiet.alerts.isEmpty)
    }

    @Test func widgetWarningsSkipHiddenLimits() {
        let u = usage([.sevenDay: 20, .iguanaNecktie: 100])
        let d = MemoryDefaults()
        #expect(UsageViewModel.warningTiers(u, now: now, desk: d) == [.iguanaNecktie])
        d.set(false, forKey: MeterVisibility.showKey(.iguanaNecktie))
        #expect(UsageViewModel.warningTiers(u, now: now, desk: d).isEmpty)
    }
}

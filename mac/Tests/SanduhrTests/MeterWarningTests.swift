import Foundation
import Testing
@testable import Sanduhr

/// The Desk meter warning rule (item 25): per meter settings, the threshold and time edges, a
/// missing reset. Settings come from an in-memory store, never the real defaults.
@Suite("Meter warnings")
struct MeterWarningTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    let hour: TimeInterval = 3600
    let day: TimeInterval = 86400

    func on(_ pct: Double, _ minReset: TimeInterval) -> MeterWarningSettings {
        MeterWarningSettings(enabled: true, threshold: pct, minReset: minReset)
    }

    func warns(_ percent: Double, resetIn: TimeInterval?, _ s: MeterWarningSettings) -> Bool {
        MeterWarning.isWarning(percent: percent, resetsAt: resetIn.map { now.addingTimeInterval($0) },
                               settings: s, now: now)
    }

    @Test func defaultsPerTier() {
        let session = MeterWarningSettings.standard(for: .fiveHour)
        #expect(session == MeterWarningSettings(enabled: false, threshold: 90, minReset: hour))
        for tier in Tier.allCases where tier != .fiveHour {
            #expect(MeterWarningSettings.standard(for: tier) == MeterWarningSettings(enabled: true, threshold: 90, minReset: day))
        }
    }

    @Test func weeklyAt92WithThreeDaysLeftWarnsWithSixHoursDoesNot() {
        let weekly = MeterWarningSettings.standard(for: .sevenDay)
        #expect(warns(92, resetIn: 3 * day, weekly))
        #expect(!warns(92, resetIn: 6 * hour, weekly))
    }

    @Test func sessionNeverWarnsUntilSwitchedOn() {
        let session = MeterWarningSettings.standard(for: .fiveHour)
        #expect(!warns(100, resetIn: 4 * hour, session))
        #expect(!warns(100, resetIn: nil, session))
        var switched = session
        switched.enabled = true
        #expect(warns(95, resetIn: 4 * hour, switched))
    }

    @Test func thresholdEdge() {
        let s = on(90, hour)
        #expect(!warns(89.9, resetIn: day, s))
        #expect(warns(90, resetIn: day, s))
        #expect(warns(90.1, resetIn: day, s))
        #expect(warns(112, resetIn: day, s))
    }

    @Test func timeEdge() {
        let s = on(90, day)
        #expect(!warns(95, resetIn: day - 1, s))
        #expect(!warns(95, resetIn: day, s))        // "more than" a day: exactly a day does not
        #expect(warns(95, resetIn: day + 1, s))
        #expect(!warns(95, resetIn: -60, s))         // a reset already past is not far away
    }

    @Test func missingResetCountsAsFarAway() {
        #expect(warns(95, resetIn: nil, on(90, 3 * day)))
        #expect(!warns(80, resetIn: nil, on(90, 3 * day)))
    }

    @Test func disabledNeverWarns() {
        let off = MeterWarningSettings(enabled: false, threshold: 50, minReset: 0)
        #expect(!warns(100, resetIn: 7 * day, off))
        #expect(!warns(100, resetIn: nil, off))
    }

    @Test func savedSettingsAreReadPerTierWithDefaultsWhenUnset() {
        let d = MemoryDefaults()
        d.set(false, forKey: "meterWarn.seven_day.on")
        d.set(true, forKey: "meterWarn.five_hour.on")
        d.set(75.0, forKey: "meterWarn.five_hour.pct")
        d.set(1800.0, forKey: "meterWarn.five_hour.minReset")
        d.set(60, forKey: "meterWarn.seven_day_opus.pct")       // an integer from `defaults write -int`
        #expect(MeterWarningSettings.saved(.fiveHour, in: d) == MeterWarningSettings(enabled: true, threshold: 75, minReset: 1800))
        #expect(MeterWarningSettings.saved(.sevenDay, in: d) == MeterWarningSettings(enabled: false, threshold: 90, minReset: day))
        #expect(MeterWarningSettings.saved(.sevenDayOpus, in: d) == MeterWarningSettings(enabled: true, threshold: 60, minReset: day))
        #expect(MeterWarningSettings.saved(.sevenDaySonnet, in: d) == MeterWarningSettings.standard(for: .sevenDaySonnet))
        #expect(MeterWarningSettings.onKey(.sevenDay) == "meterWarn.seven_day.on")
        #expect(MeterWarningSettings.thresholdKey(.sevenDay) == "meterWarn.seven_day.pct")
        #expect(MeterWarningSettings.minResetKey(.sevenDay) == "meterWarn.seven_day.minReset")
    }

    @Test func eachMeterIsIndependentInTheRows() {
        let iso = ISO8601DateFormatter()
        let usage = UsageResponse(tiers: [
            .fiveHour: TierUsage(utilization: 95, resetsAt: iso.string(from: now.addingTimeInterval(3 * hour))),
            .sevenDay: TierUsage(utilization: 92, resetsAt: iso.string(from: now.addingTimeInterval(3 * day))),
            .sevenDayOpus: TierUsage(utilization: 92, resetsAt: iso.string(from: now.addingTimeInterval(3 * day))),
            .sevenDaySonnet: TierUsage(utilization: 92, resetsAt: nil),
        ], extraUsage: nil)
        let d = MemoryDefaults()
        d.set(false, forKey: MeterWarningSettings.onKey(.sevenDayOpus))
        let rows = DeskMeterRow.rows(from: usage, now: now) { MeterWarningSettings.saved($0, in: d) }
        let warning = Dictionary(uniqueKeysWithValues: rows.map { ($0.tier, $0.warning) })
        #expect(warning[.fiveHour] == false)        // session off by default
        #expect(warning[.sevenDay] == true)
        #expect(warning[.sevenDayOpus] == false)    // switched off on its own
        #expect(warning[.sevenDaySonnet] == true)   // no reset time: far away

        // A changed setting restyles on the next build of the rows.
        d.set(true, forKey: MeterWarningSettings.onKey(.fiveHour))
        let again = DeskMeterRow.rows(from: usage, now: now) { MeterWarningSettings.saved($0, in: d) }
        #expect(again.first { $0.tier == .fiveHour }?.warning == true)
    }

    @Test func minResetChoicesInOrder() {
        #expect(MeterWarning.minResetChoices.map(\.seconds)
                == [900, 1800, hour, 3 * hour, 6 * hour, 12 * hour, day, 2 * day, 3 * day])
    }

    @Test func settingsPageListsSessionWeeklyAndReportedTiers() {
        #expect(DeskMetersSection.tiers(present: []) == [.fiveHour, .sevenDay])
        #expect(DeskMetersSection.tiers(present: [.sevenDayOpus, .fiveHour, .sevenDay])
                == [.fiveHour, .sevenDay, .sevenDayOpus])
    }
}

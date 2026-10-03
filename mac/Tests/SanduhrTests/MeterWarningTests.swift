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

    /// Item 26: the widget's cards warn exactly where the Desk rows do, for the same numbers and
    /// settings, across thresholds, resets and switches.
    @Test func widgetAndDeskAgree() {
        let iso = ISO8601DateFormatter()
        let resets: [TimeInterval?] = [nil, -60, 6 * hour, day, day + 1, 3 * day]
        let percents: [Double] = [0, 50, 89.9, 90, 92, 100]
        var tiers: [Tier: TierUsage] = [:]
        for (i, tier) in Tier.allCases.enumerated() {
            let reset = resets[i % resets.count].map { iso.string(from: now.addingTimeInterval($0)) }
            tiers[tier] = TierUsage(utilization: percents[(i * 5) % percents.count], resetsAt: reset)
        }
        let usage = UsageResponse(tiers: tiers, extraUsage: nil)
        let stores: [MemoryDefaults] = [MemoryDefaults(), MemoryDefaults(), MemoryDefaults()]
        stores[1].set(true, forKey: MeterWarningSettings.onKey(.fiveHour))
        stores[1].set(0.0, forKey: MeterWarningSettings.thresholdKey(.sevenDay))
        stores[1].set(0.0, forKey: MeterWarningSettings.minResetKey(.sevenDay))
        for tier in Tier.allCases { stores[2].set(false, forKey: MeterWarningSettings.onKey(tier)) }
        for d in stores {
            let rows = DeskMeterRow.rows(from: usage, now: now) { MeterWarningSettings.saved($0, in: d) }
            let desk = Set(rows.filter(\.warning).map(\.tier))
            let widget = UsageViewModel.warningTiers(usage, now: now, desk: d)
            #expect(widget == desk)
            for (tier, t) in tiers {
                let rule = MeterWarning.isWarning(percent: t.utilization ?? 0, resetsAt: parseISO(t.resetsAt),
                                                  settings: MeterWarningSettings.saved(tier, in: d), now: now)
                #expect(widget.contains(tier) == rule)
            }
        }
    }

    /// The acceptance case: weekly at 92% with days left warns on both; its switch off clears both.
    @Test func weeklyAt92WarnsOnWidgetUntilSwitchedOff() {
        let iso = ISO8601DateFormatter()
        let usage = UsageResponse(tiers: [
            .fiveHour: TierUsage(utilization: 40, resetsAt: iso.string(from: now.addingTimeInterval(2 * hour))),
            .sevenDay: TierUsage(utilization: 92, resetsAt: iso.string(from: now.addingTimeInterval(3 * day))),
        ], extraUsage: nil)
        let d = MemoryDefaults()
        #expect(UsageViewModel.warningTiers(usage, now: now, desk: d) == [.sevenDay])
        d.set(false, forKey: MeterWarningSettings.onKey(.sevenDay))
        #expect(UsageViewModel.warningTiers(usage, now: now, desk: d).isEmpty)
        #expect(DeskMeterRow.rows(from: usage, now: now) { MeterWarningSettings.saved($0, in: d) }
                    .allSatisfy { !$0.warning })
    }

    @Test func noUtilizationOrNoUsageNeverWarns() {
        let always = MeterWarningSettings(enabled: true, threshold: 0, minReset: 0)
        #expect(!MeterWarning.isWarning(TierUsage(utilization: nil, resetsAt: nil), settings: always, now: now))
        #expect(MeterWarning.isWarning(TierUsage(utilization: 0, resetsAt: nil), settings: always, now: now))
        #expect(MeterWarning.tiers(nil, now: now).isEmpty)
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

import Foundation
import Testing
@testable import Sanduhr

/// What the menu bar shows (item 38): each mode, a missing limit, Rotate's turns, and limits other
/// than Session and Weekly never driving it. Settings come from an in-memory store.
@Suite("Menu bar text")
struct MenuBarTextTests {
    func usage(_ tiers: [Tier: Double]) -> UsageResponse {
        var u = UsageResponse()
        for (tier, util) in tiers { u.tiers[tier] = TierUsage(utilization: util, resetsAt: nil) }
        return u
    }

    func text(_ u: UsageResponse?, _ mode: MenuBarMode, step: Int = 0) -> String? {
        MenuBarText.reading(u, mode: mode, step: step)?.text
    }

    @Test func eachMode() {
        let u = usage([.fiveHour: 12, .sevenDay: 96])
        #expect(text(u, .session) == "12%")
        #expect(text(u, .weekly) == "96%")
        #expect(text(u, .higher) == "96%")
        #expect(MenuBarText.reading(u, mode: .higher, step: 0)?.tier == .sevenDay)
        #expect(text(usage([.fiveHour: 40, .sevenDay: 20]), .higher) == "40%")
    }

    @Test func specialLimitsNeverDriveTheMenuBar() {
        let u = usage([.fiveHour: 12, .sevenDay: 30, .iguanaNecktie: 100, .sevenDayOpus: 99])
        for mode in MenuBarMode.allCases {
            let reading = MenuBarText.reading(u, mode: mode, step: 1)
            #expect(reading?.tier == .fiveHour || reading?.tier == .sevenDay)
        }
        #expect(text(u, .higher) == "30%")
        #expect(MenuBarText.reading(usage([.iguanaNecktie: 100]), mode: .higher, step: 0) == nil)
    }

    @Test func missingLimitFallsBackToTheOther() {
        let onlyWeekly = usage([.sevenDay: 63])
        let onlySession = usage([.fiveHour: 7])
        #expect(text(onlyWeekly, .session) == "63%")
        #expect(text(onlySession, .weekly) == "7%")
        #expect(text(onlyWeekly, .higher) == "63%")
        #expect(text(onlySession, .higher) == "7%")
        #expect(text(onlyWeekly, .rotate, step: 0) == "W 63%")
        #expect(text(onlySession, .rotate, step: 1) == "S 7%")
    }

    @Test func noDataShowsNothing() {
        for mode in MenuBarMode.allCases {
            #expect(MenuBarText.reading(nil, mode: mode, step: 0) == nil)
            #expect(MenuBarText.reading(UsageResponse(), mode: mode, step: 3) == nil)
        }
        var nilUtil = UsageResponse()
        nilUtil.tiers[.fiveHour] = TierUsage(utilization: nil, resetsAt: nil)
        #expect(MenuBarText.reading(nilUtil, mode: .session, step: 0) == nil)
    }

    @Test func rotateAlternates() {
        let u = usage([.fiveHour: 12, .sevenDay: 96.7])
        #expect(text(u, .rotate, step: 0) == "S 12%")
        #expect(text(u, .rotate, step: 1) == "W 96%")
        #expect(text(u, .rotate, step: 2) == "S 12%")
        #expect(text(u, .rotate, step: 7) == "W 96%")
        #expect(MenuBarText.reading(u, mode: .rotate, step: 1)?.percent == 96)
    }

    @Test func savedChoice() {
        let d = MemoryDefaults()
        #expect(MenuBarMode.saved(in: d) == .higher)
        d.set("rotate", forKey: MenuBarMode.key)
        #expect(MenuBarMode.saved(in: d) == .rotate)
        d.set("bogus", forKey: MenuBarMode.key)
        #expect(MenuBarMode.saved(in: d) == .higher)
        #expect(MenuBarMode.key == "menuBarMode")
        #expect(MenuBarMode.rotateInterval == 8)
    }
}

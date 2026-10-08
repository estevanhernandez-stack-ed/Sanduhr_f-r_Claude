import Foundation
import Testing
@testable import Sanduhr

/// A limit's two-finger menu on a Desk meter row or a widget tier card (item 39).

@Suite("Limit menu")
struct LimitMenuTests {
    let two = SanduhrMenu.accounts(["Personal", "Work"], active: "Work")
    /// Limits believed temporary in these tests (LimitLifetime decides it in the app).
    static let temp: Set<Tier> = [.sevenDayOpus, .iguanaNecktie]
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    func iso(_ date: Date) -> String { ISO8601DateFormatter().string(from: date) }

    /// Titles in order, "-" for each separator the renderers put between groups.
    func flat(_ groups: [[LimitMenuEntry]]) -> [String] {
        groups.enumerated().flatMap { i, g in (i > 0 ? ["-"] : []) + g.map(\.title) }
    }

    @Test func aLimitThatCanBeHiddenWithTwoAccounts() {
        let groups = LimitMenu.groups(tier: .sevenDayOpus, accounts: two, hidden: [], temporary: Self.temp, warningsOn: true)
        #expect(flat(groups) == [
            "Accounts", "-",
            "Hide Weekly — Opus", "Stop warnings for this limit", "-",
            LimitMenu.meterSettings,
        ])
        // The submenu: each account, the active one checked.
        guard case .accounts(let menu) = groups[0][0] else { Issue.record("no Accounts submenu"); return }
        #expect(menu.items.map(\.label) == ["Personal", "Work"])
        #expect(menu.items.map(\.checked) == [false, true])
    }

    @Test func sessionAndWeeklyCannotBeHidden() {
        for tier in [Tier.fiveHour, .sevenDay] {
            let groups = LimitMenu.groups(tier: tier, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: true)
            #expect(flat(groups) == ["Stop warnings for this limit", "-", LimitMenu.meterSettings])
        }
    }

    @Test func oneAccountHasNoAccountsSubmenu() {
        let one = SanduhrMenu.accounts(["Personal"], active: "Personal")
        let groups = LimitMenu.groups(tier: .iguanaNecktie, accounts: one, hidden: [], temporary: Self.temp, warningsOn: false)
        #expect(flat(groups) == ["Hide Weekly — Special", "Warn again for this limit", "-", LimitMenu.meterSettings])
    }

    @Test func warningsItemFollowsTheSetting() {
        let on = LimitMenu.groups(tier: .sevenDay, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: true)
        let off = LimitMenu.groups(tier: .sevenDay, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: false)
        #expect(on[0] == [.warnings(.sevenDay, on: true)])
        #expect(off[0] == [.warnings(.sevenDay, on: false)])
        #expect(off[0][0].title == "Warn again for this limit")
    }

    @Test func anAlreadyHiddenLimitHasNoHide() {
        let groups = LimitMenu.groups(tier: .sevenDayOpus, accounts: nil, hidden: [.sevenDayOpus], temporary: Self.temp, warningsOn: true)
        #expect(flat(groups) == ["Stop warnings for this limit", "-", "Hidden Limits", LimitMenu.meterSettings])
    }

    /// Item 42: Hide only for a limit believed temporary; a permanent one keeps its warnings item.
    @Test func aPermanentLimitHasNoHide() {
        for tier in [Tier.sevenDaySonnet, .sevenDayOpus, .sevenDayCowork] {
            let groups = LimitMenu.groups(tier: tier, accounts: nil, hidden: [], temporary: [], warningsOn: true)
            #expect(flat(groups) == ["Stop warnings for this limit", "-", LimitMenu.meterSettings])
        }
        let temporary = LimitMenu.groups(tier: .sevenDaySonnet, accounts: nil, hidden: [],
                                         temporary: [.sevenDaySonnet], warningsOn: true)
        #expect(temporary[0] == [.hide(.sevenDaySonnet), .warnings(.sevenDaySonnet, on: true)])
    }

    /// Item 42: Hidden Limits lists every hidden limit in display order, beside the rows too, and
    /// is gone when nothing is hidden.
    @Test func hiddenLimitsSubmenu() {
        let hidden: Set<Tier> = [.iguanaNecktie, .sevenDayOpus]
        let groups = LimitMenu.groups(tier: nil, accounts: two, hidden: hidden, temporary: Self.temp,
                                      warningsOn: true, widgetVisible: false)
        #expect(flat(groups) == ["Show Widget", "-", "Accounts", "-", "Hidden Limits", LimitMenu.meterSettings])
        #expect(groups.last?.first == .hiddenLimits([.sevenDayOpus, .iguanaNecktie]))
        #expect(LimitMenuEntry.show(.iguanaNecktie).title == "Weekly — Special")
        let onRow = LimitMenu.groups(tier: .sevenDay, accounts: nil, hidden: [.iguanaNecktie],
                                     temporary: Self.temp, warningsOn: true)
        #expect(onRow.last == [.hiddenLimits([.iguanaNecktie]), .meterSettings])
        let none = LimitMenu.groups(tier: .sevenDay, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: true)
        #expect(!none.joined().contains { if case .hiddenLimits = $0 { true } else { false } })
    }

    /// Picking a hidden limit shows it again: its hide and its record are cleared.
    @Test func showFromTheSubmenuClearsTheHide() {
        let d = MemoryDefaults()
        LimitMenu.apply(.hide(.iguanaNecktie), to: d)
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        #expect(LimitMenu.groups(tier: nil, accounts: nil, store: d, usage: nil, now: now).last?.first
            == .hiddenLimits([.iguanaNecktie]))
        LimitMenu.apply(.show(.iguanaNecktie), to: d)
        #expect(MeterVisibility.hidden(in: d).isEmpty)
        #expect(d.values.isEmpty)
    }

    @Test func besideTheRowsOnlyAccountsAndMeterSettings() {
        #expect(flat(LimitMenu.groups(tier: nil, accounts: two, hidden: [], temporary: Self.temp, warningsOn: true))
            == ["Accounts", "-", LimitMenu.meterSettings])
        #expect(flat(LimitMenu.groups(tier: nil, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: true)) == [LimitMenu.meterSettings])
    }

    @Test func readsTheSavedSettings() {
        let d = MemoryDefaults()
        // Defaults: the session warns only once switched on, the weekly limits warn.
        #expect(LimitMenu.groups(tier: .fiveHour, accounts: nil, store: d, usage: nil, now: now)[0]
            == [.warnings(.fiveHour, on: false)])
        // Weekly — Opus is a known model limit: no Hide while its reset is a week off…
        var u = UsageResponse()
        u.tiers[.sevenDayOpus] = TierUsage(utilization: 40, resetsAt: iso(now.addingTimeInterval(3 * 86_400)))
        #expect(LimitMenu.groups(tier: .sevenDayOpus, accounts: nil, store: d, usage: u, now: now)[0]
            == [.warnings(.sevenDayOpus, on: true)])
        // …and Hide once its reset is more than 8 days away.
        u.tiers[.sevenDayOpus] = TierUsage(utilization: 40, resetsAt: iso(now.addingTimeInterval(20 * 86_400)))
        #expect(LimitMenu.groups(tier: .sevenDayOpus, accounts: nil, store: d, usage: u, now: now)[0]
            == [.hide(.sevenDayOpus), .warnings(.sevenDayOpus, on: true)])
        // The promo slot is temporary whatever its reset, once it is reported.
        u.tiers[.iguanaNecktie] = TierUsage(utilization: 90, resetsAt: nil)
        #expect(LimitMenu.groups(tier: .iguanaNecktie, accounts: nil, store: d, usage: u, now: now)[0]
            == [.hide(.iguanaNecktie), .warnings(.iguanaNecktie, on: true)])
        d.set(false, forKey: MeterVisibility.showKey(.iguanaNecktie))
        d.set(false, forKey: MeterWarningSettings.onKey(.iguanaNecktie))
        #expect(LimitMenu.groups(tier: .iguanaNecktie, accounts: nil, store: d, usage: u, now: now)[0]
            == [.warnings(.iguanaNecktie, on: false)])
    }

    /// Hide and the warnings item write the keys Settings, Desk, Meters reads.
    @Test func applyWritesTheSettingsKeys() {
        let d = MemoryDefaults()
        LimitMenu.apply(.hide(.iguanaNecktie), to: d)
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        LimitMenu.apply(.warnings(.sevenDay, on: true), to: d)
        #expect(MeterWarningSettings.saved(.sevenDay, in: d).enabled == false)
        #expect(LimitMenu.silenced(in: d) == [.fiveHour, .sevenDay])
        LimitMenu.apply(.warnings(.sevenDay, on: false), to: d)
        #expect(MeterWarningSettings.saved(.sevenDay, in: d).enabled)
        #expect(LimitMenu.silenced(in: d) == [.fiveHour])
    }

    /// Item 41: a plain click on the Desk meters does nothing, so their menu has the widget on top.
    @Test func aDeskMeterMenuStartsWithShowOrHideWidget() {
        let hidden = LimitMenu.groups(tier: .sevenDay, accounts: two, hidden: [], temporary: Self.temp, warningsOn: true, widgetVisible: false)
        #expect(flat(hidden) == [
            "Show Widget", "-", "Accounts", "-", "Stop warnings for this limit", "-", LimitMenu.meterSettings,
        ])
        let shown = LimitMenu.groups(tier: nil, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: true, widgetVisible: true)
        #expect(flat(shown) == ["Hide Widget", "-", LimitMenu.meterSettings])
        #expect(LimitMenu.groups(tier: nil, accounts: nil, store: MemoryDefaults(), usage: nil, now: now,
                                 widgetVisible: false)[0]
            == [.widget(visible: false)])
        // A widget card's menu (no widgetVisible) has no widget item.
        #expect(LimitMenu.groups(tier: nil, accounts: nil, hidden: [], temporary: Self.temp, warningsOn: true)[0] == [.meterSettings])
    }

    /// Below the limit's items the shared menu follows without its own Show or Hide Widget.
    @Test func theSharedMenuCanDropShowHide() {
        let groups = SanduhrMenu.groups(widgetVisible: false, deepWork: false, pacing: false, snake: false)
        let rest = SanduhrMenu.without(.showHide, in: groups)
        #expect(rest.count == groups.count - 1)
        #expect(rest.first?.header == "Tools")
        #expect(!rest.flatMap(\.entries).contains { $0.command == .showHide })
    }

    @Test func applyNeverHidesSessionOrWeeklyAndLeavesTheRestAlone() {
        let d = MemoryDefaults()
        LimitMenu.apply(.hide(.fiveHour), to: d)
        LimitMenu.apply(.hide(.sevenDay), to: d)
        // A known model limit with a weekly reset is not temporary: Hide writes nothing.
        var u = UsageResponse()
        u.tiers[.sevenDaySonnet] = TierUsage(utilization: 40, resetsAt: iso(now.addingTimeInterval(3 * 86_400)))
        LimitMenu.apply(.hide(.sevenDaySonnet), to: d, usage: u, now: now)
        LimitMenu.apply(.hiddenLimits([.iguanaNecktie]), to: d)
        LimitMenu.apply(.meterSettings, to: d)
        LimitMenu.apply(.widget(visible: false), to: d)
        if let two { LimitMenu.apply(.accounts(two), to: d) }
        #expect(d.values.isEmpty)
    }
}

import Testing
@testable import Sanduhr

/// A limit's two-finger menu on a Desk meter row or a widget tier card (item 39).

@Suite("Limit menu")
struct LimitMenuTests {
    let two = SanduhrMenu.accounts(["Personal", "Work"], active: "Work")

    /// Titles in order, "-" for each separator the renderers put between groups.
    func flat(_ groups: [[LimitMenuEntry]]) -> [String] {
        groups.enumerated().flatMap { i, g in (i > 0 ? ["-"] : []) + g.map(\.title) }
    }

    @Test func aLimitThatCanBeHiddenWithTwoAccounts() {
        let groups = LimitMenu.groups(tier: .sevenDayOpus, accounts: two, hidden: [], warningsOn: true)
        #expect(flat(groups) == [
            "Accounts", "-",
            "Hide Weekly — Opus", "Stop warnings for this limit", "-",
            "Meter Settings…",
        ])
        // The submenu: each account, the active one checked.
        guard case .accounts(let menu) = groups[0][0] else { Issue.record("no Accounts submenu"); return }
        #expect(menu.items.map(\.label) == ["Personal", "Work"])
        #expect(menu.items.map(\.checked) == [false, true])
    }

    @Test func sessionAndWeeklyCannotBeHidden() {
        for tier in [Tier.fiveHour, .sevenDay] {
            let groups = LimitMenu.groups(tier: tier, accounts: nil, hidden: [], warningsOn: true)
            #expect(flat(groups) == ["Stop warnings for this limit", "-", "Meter Settings…"])
        }
    }

    @Test func oneAccountHasNoAccountsSubmenu() {
        let one = SanduhrMenu.accounts(["Personal"], active: "Personal")
        let groups = LimitMenu.groups(tier: .iguanaNecktie, accounts: one, hidden: [], warningsOn: false)
        #expect(flat(groups) == ["Hide Weekly — Special", "Warn again for this limit", "-", "Meter Settings…"])
    }

    @Test func warningsItemFollowsTheSetting() {
        let on = LimitMenu.groups(tier: .sevenDay, accounts: nil, hidden: [], warningsOn: true)
        let off = LimitMenu.groups(tier: .sevenDay, accounts: nil, hidden: [], warningsOn: false)
        #expect(on[0] == [.warnings(.sevenDay, on: true)])
        #expect(off[0] == [.warnings(.sevenDay, on: false)])
        #expect(off[0][0].title == "Warn again for this limit")
    }

    @Test func anAlreadyHiddenLimitHasNoHide() {
        let groups = LimitMenu.groups(tier: .sevenDayOpus, accounts: nil, hidden: [.sevenDayOpus], warningsOn: true)
        #expect(flat(groups) == ["Stop warnings for this limit", "-", "Meter Settings…"])
    }

    @Test func besideTheRowsOnlyAccountsAndMeterSettings() {
        #expect(flat(LimitMenu.groups(tier: nil, accounts: two, hidden: [], warningsOn: true))
            == ["Accounts", "-", "Meter Settings…"])
        #expect(flat(LimitMenu.groups(tier: nil, accounts: nil, hidden: [], warningsOn: true)) == ["Meter Settings…"])
    }

    @Test func readsTheSavedSettings() {
        let d = MemoryDefaults()
        // Defaults: the session warns only once switched on, the weekly limits warn.
        #expect(LimitMenu.groups(tier: .fiveHour, accounts: nil, store: d)[0] == [.warnings(.fiveHour, on: false)])
        #expect(LimitMenu.groups(tier: .sevenDayOpus, accounts: nil, store: d)[0]
            == [.hide(.sevenDayOpus), .warnings(.sevenDayOpus, on: true)])
        d.set(false, forKey: MeterVisibility.showKey(.sevenDayOpus))
        d.set(false, forKey: MeterWarningSettings.onKey(.sevenDayOpus))
        #expect(LimitMenu.groups(tier: .sevenDayOpus, accounts: nil, store: d)[0] == [.warnings(.sevenDayOpus, on: false)])
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

    @Test func applyNeverHidesSessionOrWeeklyAndLeavesTheRestAlone() {
        let d = MemoryDefaults()
        LimitMenu.apply(.hide(.fiveHour), to: d)
        LimitMenu.apply(.hide(.sevenDay), to: d)
        LimitMenu.apply(.meterSettings, to: d)
        if let two { LimitMenu.apply(.accounts(two), to: d) }
        #expect(d.values.isEmpty)
    }
}

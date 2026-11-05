import Foundation
import Testing
@testable import Sanduhr

/// A host over a stored setup: like the app's, its usage reads the setup the service just saved.
@MainActor
private final class StoreHost: SetupHost {
    let store: MemoryDefaults
    var calls: [String] = []
    var drawersOn = false
    var drawersEverSorted = true
    var drawerFiles = 0
    var addOnsInstalled = false
    var hasAccount = false
    var offResult = SetupDrawersOff(off: true, line: nil)

    init(store: MemoryDefaults) { self.store = store }

    var usageOn: Bool { SetupStore.claudeUsageOn(from: store) }
    func turnOffDrawers() -> SetupDrawersOff {
        calls.append("drawersOff")
        if offResult.off { drawersOn = false }
        return offResult
    }
    func turnOnDrawers() { calls.append("drawersOn"); drawersOn = true }
    func sortNow() -> String? { calls.append("sort"); return nil }
    func setUsage(_ on: Bool) { calls.append("usage:\(on)") }
    func reloadDesk() { calls.append("reload") }
}

@MainActor
@Suite("Set up Sanduhr window model")
struct SetupViewModelTests {
    private let setupStore = MemoryDefaults()
    private let deskStore = MemoryDefaults()
    private let host: StoreHost

    init() { host = StoreHost(store: setupStore) }

    private func service(failSave: Bool = false) -> SetupService {
        var storage = SetupStorage.defaults(setup: setupStore, desk: deskStore)
        if failSave { storage.save = { _, _ in throw SetupStorage.SaveError() } }
        return SetupService(storage: storage, host: host)
    }

    private func store(_ s: SetupSettings, deskOn: Bool = true) {
        SetupStore.save(s, to: setupStore)
        deskStore.set(deskOn, forKey: DeskController.enabledKey)
    }

    private func model(firstRun: Bool, failSave: Bool = false, hasAccount: @escaping () -> Bool = { false }) -> SetupViewModel {
        SetupViewModel(service: service(failSave: failSave), firstRun: firstRun, hasAccount: hasAccount)
    }

    // MARK: ticks

    @Test func ticksComeFromTheCurrentState() {
        store(SetupSettings(desktop: true, usage: ["claude": true]))
        host.drawersOn = true
        let m = model(firstRun: false)
        #expect(m.choices == SetupChoices(desktop: true, drawers: true, claudeUsage: true))
        #expect(m.dismissLabel == "Cancel" && m.changeLines.isEmpty && m.blocked == nil)

        store(SetupSettings(desktop: true, usage: ["claude": false]), deskOn: false)
        host.drawersOn = false
        #expect(model(firstRun: false).choices == SetupChoices(desktop: false, drawers: false, claudeUsage: false))
    }

    @Test func aFirstRunStartsOnTheDefaultsWithNotNowAndNoLines() {
        let m = model(firstRun: true)
        #expect(m.choices == .firstRun)
        #expect(m.dismissLabel == "Not now")
        m.desktop = false
        #expect(m.changeLines.isEmpty)   // nothing is on yet, so nothing is taken away
    }

    @Test func anAccountOnAFirstRunTicksUsage() {
        #expect(model(firstRun: true, hasAccount: { true }).claudeUsage)
        #expect(!model(firstRun: true, hasAccount: { false }).claudeUsage)
    }

    // MARK: lines, blocked

    @Test func untickingSaysWhatChangesBeforeContinue() {
        store(SetupSettings(desktop: true, usage: ["claude": true]))
        host.drawersOn = true
        host.drawerFiles = 3
        host.hasAccount = true
        let m = model(firstRun: false)
        #expect(m.changeLines.isEmpty)
        m.desktop = false
        #expect(m.changeLines == [SetupCopy.desktopOffLine])
        m.drawers = false
        m.claudeUsage = false
        #expect(m.changeLines == [SetupCopy.desktopOffLine, SetupCopy.drawersOffLine(3), SetupCopy.usageOffLine])
        m.claudeUsage = true
        #expect(m.changeLines == [SetupCopy.desktopOffLine, SetupCopy.drawersOffLine(3)])
        m.desktop = true
        m.drawers = true
        #expect(m.changeLines.isEmpty)
        // Nothing was saved by looking.
        #expect(host.calls.isEmpty)
    }

    @Test func continueIsBlockedWithNothingTicked() {
        store(SetupSettings(desktop: true, usage: ["claude": true]))
        let m = model(firstRun: false)
        m.desktop = false; m.drawers = false; m.claudeUsage = false
        #expect(m.blocked == SetupCopy.pickOne && !m.canContinue)
        var closed = false
        m.onClose = { closed = true }
        m.continueTapped()
        #expect(!closed && !m.saved && host.calls.isEmpty)
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true]))
        m.claudeUsage = true
        #expect(m.blocked == nil && m.canContinue)
    }

    @Test func theTrademarkLineShowsWheneverClaudeIsNamed() {
        let m = model(firstRun: true)
        #expect(!m.showTrademark)
        m.claudeUsage = true
        #expect(m.showTrademark)
        // Usage unticked but the add-ons line names Claude Code.
        store(SetupSettings(desktop: true, usage: ["claude": true]))
        host.addOnsInstalled = true
        let r = model(firstRun: false)
        r.claudeUsage = false
        #expect(r.changeLines == [SetupCopy.addOnsStayLine] && r.showTrademark)
    }

    // MARK: Continue, Not now

    @Test func notNowAndCancelSaveNothing() {
        let m = model(firstRun: true)
        m.desktop = false
        var closed = 0
        m.onClose = { closed += 1 }
        m.dismiss()
        #expect(closed == 1 && !m.saved)
        #expect(SetupStore.load(from: setupStore) == nil)
        #expect(deskStore.object(forKey: DeskController.enabledKey) == nil)
        #expect(host.calls.isEmpty)
    }

    @Test func continueWithNothingToSayAppliesAndCloses() {
        let m = model(firstRun: true)
        var applied: SetupResult?
        var closed = false
        m.onApplied = { applied = $0 }
        m.onClose = { closed = true }
        m.claudeUsage = true
        m.continueTapped()
        #expect(m.saved && closed && !m.said)
        #expect(applied?.now == SetupChoices(desktop: true, drawers: true, claudeUsage: true))
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true]))
        // The first-on line is not worth a pane of its own: a default first run ends without one.
        #expect(m.resultLines.isEmpty)
    }

    @Test func continueWithSomethingToSayShowsItAndWaitsForDone() {
        store(SetupSettings(desktop: true, usage: ["claude": false]))
        host.drawersOn = true
        host.offResult = SetupDrawersOff(off: true, line: "2 files went back.")
        let m = model(firstRun: false)
        var closed = false
        m.onClose = { closed = true }
        m.drawers = false
        m.continueTapped()
        #expect(m.saved && m.said && !closed)
        #expect(m.resultLines == ["2 files went back."])
        #expect(!m.canContinue)
        m.dismiss()
        #expect(closed)
    }

    @Test func aFailedSaveKeepsTheWindowOpenWithTheLine() {
        let m = model(firstRun: true, failSave: true)
        var closed = false
        m.onClose = { closed = true }
        m.continueTapped()
        #expect(!m.saved && !closed && !m.said)
        #expect(m.resultLines.contains(SetupCopy.saveFailed))
        #expect(SetupStore.load(from: setupStore) == nil)
    }

    @Test func anAccountAddedWhileTheScreenWasUpTicksUsageAtContinueUnlessTouched() {
        var account = false
        let m = model(firstRun: true, hasAccount: { account })
        #expect(!m.claudeUsage)
        account = true
        m.continueTapped()
        #expect(SetupStore.load(from: setupStore)?.claudeUsage == true)

        // A person who touched the tick keeps their answer.
        let other = MemoryDefaults()
        let h2 = StoreHost(store: other)
        let svc = SetupService(storage: .defaults(setup: other, desk: MemoryDefaults()), host: h2)
        let touched = SetupViewModel(service: svc, firstRun: true, hasAccount: { true })
        touched.claudeUsage = false
        touched.continueTapped()
        #expect(SetupStore.load(from: other)?.claudeUsage == false)
    }

    /// The app's usage switch reads the stored setup, so the service must read it before saving or a
    /// change of the usage tick never reaches the switch.
    @Test func continueTellsTheHostUsageChangedEvenWhenItReadsTheSavedSetup() {
        store(SetupSettings(desktop: true, usage: ["claude": true]))
        let m = model(firstRun: false)
        m.claudeUsage = false
        m.continueTapped()
        #expect(host.calls.contains("usage:false"))

        let again = model(firstRun: false)
        again.claudeUsage = true
        again.continueTapped()
        #expect(host.calls.contains("usage:true"))
        // An unchanged tick does not.
        host.calls = []
        let same = model(firstRun: false)
        same.desktop = true
        same.continueTapped()
        #expect(!host.calls.contains { $0.hasPrefix("usage:") })
    }

    @Test func theDesignButtonHasNoActionUntilItemSevenSetsOne() {
        let m = model(firstRun: true)
        #expect(m.onDesignWithChat == nil)
    }
}

@Suite("Where Set up Sanduhr opens")
struct SetupOpensTests {
    private func launch(hasAccount: Bool, justTheDesk: Bool, in store: MemoryDefaults) -> Bool {
        SetupMigration.runOnce(hasAccount: hasAccount, justTheDesk: justTheDesk, silencedReminder: false,
                               deskEnabled: true, in: store)
        return SetupLaunch.opensSetup(SetupStore.load(from: store))
    }

    @Test func aNewInstallOpensIt() { #expect(launch(hasAccount: false, justTheDesk: false, in: MemoryDefaults())) }
    @Test func anUpgradeWithAnAccountDoesNot() { #expect(!launch(hasAccount: true, justTheDesk: false, in: MemoryDefaults())) }
    @Test func anUpgradeWithJustTheDeskDoesNot() { #expect(!launch(hasAccount: false, justTheDesk: true, in: MemoryDefaults())) }
    @Test func anUpgradeWithNeitherOpensIt() {
        // The migration returned nil and stored nothing, so the same rule opens it.
        let s = MemoryDefaults()
        #expect(launch(hasAccount: false, justTheDesk: false, in: s))
        #expect(s.object(forKey: SetupStore.migratedKey) as? Bool == true)
    }

    @Test func notNowThenRelaunchAsksAgainButClosingTheScreenWithContinueDoesNot() {
        let s = MemoryDefaults()
        #expect(launch(hasAccount: false, justTheDesk: false, in: s))
        #expect(launch(hasAccount: false, justTheDesk: false, in: s))   // nothing was saved by Not now
        SetupStore.save(SetupSettings(desktop: true, usage: ["claude": false]), to: s)
        #expect(!SetupLaunch.opensSetup(SetupStore.load(from: s)))
    }

    @Test func theSignInSheetIsOnlyForUsageTickedWithNoKey() {
        let on = SetupSettings(desktop: true, usage: ["claude": true])
        let off = SetupSettings(desktop: true, usage: ["claude": false])
        #expect(SetupLaunch.showsSignIn(on, hasSessionKey: false, notNow: false))
        #expect(!SetupLaunch.showsSignIn(on, hasSessionKey: true, notNow: false))
        #expect(!SetupLaunch.showsSignIn(on, hasSessionKey: false, notNow: true))
        #expect(!SetupLaunch.showsSignIn(off, hasSessionKey: false, notNow: false))
        #expect(!SetupLaunch.showsSignIn(nil, hasSessionKey: false, notNow: false))   // the setup screen is due instead
    }

    @Test func theMenuBarClickOpensItOnlyWhilePendingWithNoAccount() {
        #expect(SetupLaunch.menuBarClickOpensSetup(setupPending: true, hasAccount: false))
        #expect(!SetupLaunch.menuBarClickOpensSetup(setupPending: true, hasAccount: true))
        #expect(!SetupLaunch.menuBarClickOpensSetup(setupPending: false, hasAccount: false))
    }
}

@Suite("Set up Sanduhr, menu and Settings")
struct SetupReachTests {
    @Test func theMenuHasSetUpSanduhrAfterSettings() throws {
        let groups = SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: false, snake: false)
        let entries = groups.flatMap(\.entries)
        let setup = try #require(entries.first { $0.command == .setup })
        #expect(setup.title == "Set up Sanduhr…" && setup.enabled && setup.note == nil && setup.checked == false)
        let settings = try #require(entries.firstIndex { $0.command == .settings })
        #expect(entries.firstIndex { $0.command == .setup } == settings + 1)
        // Every menu that carries the shared items has it, with the widget's Show or Hide left out too.
        #expect(SanduhrMenu.without(.showHide, in: groups).flatMap(\.entries).contains { $0.command == .setup })
    }

    @Test func settingsGeneralHasItsIndexEntry() throws {
        let entry = try #require(SettingsAnchor.all.first { $0.anchor == SettingsAnchor.setup })
        #expect(entry.page == .general && entry.title == "Set up Sanduhr…")
        #expect(SettingsAnchor.exists(SettingsAnchor.setup, on: .general))
        let hit = try #require(SettingsSearch.hits("Set up Sanduhr").first)
        #expect(hit.entry.page == .general && hit.entry.anchor == SettingsAnchor.setup)
        for word in ["first run", "turn off usage", "Tidy my files"] {
            #expect(SettingsSearch.hits(word).contains { $0.entry.anchor == SettingsAnchor.setup })
        }
    }

    @Test func theCopyHasNoEmDash() {
        for s in SetupCopy.all { #expect(!s.contains("\u{2014}"), "\(s)") }
    }
}

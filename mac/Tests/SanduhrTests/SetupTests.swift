import Foundation
import Testing
@testable import Sanduhr

/// The stored setup, its migration and the launch rules (Set up Sanduhr, item 1). In-memory stores only.
@Suite("Set up Sanduhr model")
struct SetupTests {
    private let on = SetupSettings(desktop: true, usage: ["claude": true])
    private let off = SetupSettings(desktop: true, usage: ["claude": false])

    // MARK: stored form

    @Test func roundTripsThroughTheStore() {
        let s = MemoryDefaults()
        #expect(SetupStore.load(from: s) == nil)
        let setup = SetupSettings(desktop: false, usage: ["claude": true, "other": false])
        SetupStore.save(setup, to: s)
        #expect(SetupStore.load(from: s) == setup)
    }

    @Test func damagedEntriesKeepTheFallback() {
        let raw: [String: Any] = ["desktop": "yes", "usage": ["claude": 1, "other": true]]
        let got = SetupSettings.decode(raw, fallback: on)
        #expect(got.desktop == true)
        #expect(got.usage == ["claude": true, "other": true])
        #expect(SetupSettings.decode("junk", fallback: off) == off)
        #expect(SetupSettings.decode(nil, fallback: off) == off)
        #expect(SetupSettings.decode(["desktop": false], fallback: on) == SetupSettings(desktop: false, usage: ["claude": true]))
    }

    @Test func claudeAndUsageHelpers() {
        #expect(on.claudeUsage && on.usageOn)
        #expect(!off.claudeUsage && !off.usageOn)
        #expect(SetupSettings(desktop: true, usage: ["x": true]).usageOn)
        #expect(off.withClaude(true) == on)
    }

    // MARK: choices

    @Test func choices() {
        #expect(SetupChoices.firstRun == SetupChoices(desktop: true, drawers: true, claudeUsage: false))
        #expect(!SetupChoices(desktop: false, drawers: false, claudeUsage: false).anyTicked)
        #expect(SetupChoices(desktop: false, drawers: false, claudeUsage: true).anyTicked)
        #expect(!SetupChoices(desktop: false, drawers: false, claudeUsage: true).deskRuns)
        #expect(SetupChoices(desktop: false, drawers: true, claudeUsage: false).deskRuns)
        #expect(SetupChoices.of(on, drawersOn: false, deskEnabled: false) == SetupChoices(desktop: false, drawers: false, claudeUsage: true))
        #expect(SetupChoices.of(on, drawersOn: true, deskEnabled: true) == SetupChoices(desktop: true, drawers: true, claudeUsage: true))
    }

    @Test func toSettingsKeepsUnknownSources() {
        let before = SetupSettings(desktop: true, usage: ["claude": true, "other": true])
        let got = SetupChoices(desktop: false, drawers: true, claudeUsage: false).toSettings(before: before)
        #expect(got == SetupSettings(desktop: false, usage: ["claude": false, "other": true]))
        #expect(SetupChoices.firstRun.toSettings(before: nil) == SetupSettings(desktop: true, usage: ["claude": false]))
    }

    // MARK: migration

    @Test func accountNeverComesOutUsageOff() {
        for jtd in [false, true] {
            for silenced in [false, true] {
                for desk in [false, true] {
                    let r = SetupMigration.from(hasAccount: true, justTheDesk: jtd, silencedReminder: silenced, deskEnabled: desk)
                    #expect(r?.setup == on)
                    #expect(r?.deskOn == desk)
                    #expect(r?.reminderOn == (jtd && silenced))
                }
            }
        }
    }

    @Test func justTheDeskIsDesktopOnUsageOff() {
        let r = SetupMigration.from(hasAccount: false, justTheDesk: true, silencedReminder: true, deskEnabled: false)
        #expect(r == SetupMigrationResult(setup: off, deskOn: true, reminderOn: true))
        let r2 = SetupMigration.from(hasAccount: false, justTheDesk: true, silencedReminder: false, deskEnabled: true)
        #expect(r2?.reminderOn == false)
    }

    @Test func firstRunMigratesToNothing() {
        #expect(SetupMigration.from(hasAccount: false, justTheDesk: false, silencedReminder: false, deskEnabled: true) == nil)
    }

    @Test func runOnceSavesAndRecords() {
        let s = MemoryDefaults()
        let r = SetupMigration.runOnce(hasAccount: true, justTheDesk: false, silencedReminder: false, deskEnabled: true, in: s)
        #expect(r?.setup == on)
        #expect(SetupStore.load(from: s) == on)
        #expect(s.bool(forKey: SetupStore.migratedKey))
        // A second launch changes nothing, whatever the inputs now say.
        #expect(SetupMigration.runOnce(hasAccount: false, justTheDesk: true, silencedReminder: false, deskEnabled: true, in: s) == nil)
        #expect(SetupStore.load(from: s) == on)
    }

    @Test func runOnceOnAFirstRunStoresNothingButRecords() {
        let s = MemoryDefaults()
        #expect(SetupMigration.runOnce(hasAccount: false, justTheDesk: false, silencedReminder: false, deskEnabled: true, in: s) == nil)
        #expect(SetupStore.load(from: s) == nil)
        #expect(s.bool(forKey: SetupStore.migratedKey))
    }

    @Test func runOnceNeverOverwritesAStoredSetup() {
        let s = MemoryDefaults()
        SetupStore.save(off, to: s)
        #expect(SetupMigration.runOnce(hasAccount: true, justTheDesk: false, silencedReminder: false, deskEnabled: true, in: s) == nil)
        #expect(SetupStore.load(from: s) == off)
    }

    // MARK: launch rules

    @Test func launchRules() {
        #expect(SetupLaunch.opensSetup(nil))
        #expect(!SetupLaunch.opensSetup(on))
        #expect(SetupLaunch.widgetStartsHidden(nil, deskEnabled: false))
        #expect(SetupLaunch.widgetStartsHidden(off, deskEnabled: true))
        #expect(!SetupLaunch.widgetStartsHidden(off, deskEnabled: false))
        #expect(!SetupLaunch.widgetStartsHidden(on, deskEnabled: true))
        #expect(SetupLaunch.menuBarClickOpensSetup(setupPending: true, hasAccount: false))
        #expect(!SetupLaunch.menuBarClickOpensSetup(setupPending: true, hasAccount: true))
        #expect(!SetupLaunch.menuBarClickOpensSetup(setupPending: false, hasAccount: false))
    }

    // MARK: copy and change lines

    @Test func noEmDashInAnyString() {
        for s in SetupCopy.all { #expect(!s.contains("\u{2014}"), "\(s)") }
        #expect(SetupCopy.usageOffSettingsLine == "Usage tracking is off. Turn it on in Set up Sanduhr.")
    }

    private func lines(_ b: SetupChoices, _ a: SetupChoices, files: Int = 3, addons: Bool = true, account: Bool = true) -> [String] {
        SetupChange.lines(before: b, after: a, drawerFiles: files, addonsInstalled: addons, hasAccount: account)
    }

    @Test func changeLinesTakingTicksAway() {
        let all = SetupChoices(desktop: true, drawers: true, claudeUsage: true)
        #expect(lines(all, SetupChoices(desktop: false, drawers: true, claudeUsage: true)) == [SetupCopy.desktopOffLine])
        #expect(lines(all, SetupChoices(desktop: true, drawers: false, claudeUsage: true)) == [SetupCopy.drawersOffLine(3)])
        #expect(lines(all, SetupChoices(desktop: true, drawers: true, claudeUsage: false)) == [SetupCopy.usageOffLine, SetupCopy.addOnsStayLine])
        #expect(lines(all, SetupChoices(desktop: false, drawers: false, claudeUsage: false)).count == 4)
    }

    @Test func changeLinesQualifiers() {
        let all = SetupChoices(desktop: true, drawers: true, claudeUsage: true)
        let noUsage = SetupChoices(desktop: true, drawers: true, claudeUsage: false)
        #expect(lines(all, noUsage, account: false) == [SetupCopy.addOnsStayLine])
        #expect(lines(all, noUsage, addons: false) == [SetupCopy.usageOffLine])
        #expect(lines(all, noUsage, addons: false, account: false).isEmpty)
        #expect(lines(all, SetupChoices(desktop: true, drawers: false, claudeUsage: true), files: 0).isEmpty)
        #expect(SetupCopy.drawersOffLine(1) == "1 drawer file goes back to your Desktop.")
    }

    @Test func changeLinesAddingTicksSayNothing() {
        let none = SetupChoices(desktop: false, drawers: false, claudeUsage: false)
        #expect(lines(none, SetupChoices.firstRun).isEmpty)
        #expect(lines(none, SetupChoices(desktop: true, drawers: true, claudeUsage: true)).isEmpty)
    }

    @Test func blockedOnlyWhenNothingTicked() {
        #expect(SetupChange.blocked(SetupChoices(desktop: false, drawers: false, claudeUsage: false)) == SetupCopy.pickOne)
        #expect(SetupChange.blocked(SetupChoices(desktop: false, drawers: false, claudeUsage: true)) == nil)
    }
}

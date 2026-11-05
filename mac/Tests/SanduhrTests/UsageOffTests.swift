import Foundation
import Testing
@testable import Sanduhr

/// Usage off with the account kept (Set up Sanduhr, item 2). Fakes only: no client, no timer, no
/// Keychain, no file, an in-memory defaults store.
@MainActor
@Suite("Usage off")
struct UsageOffTests {
    /// Records every effect the switch runs, in order.
    final class Fakes {
        var calls: [String] = []
        var status: UsageViewModel.StatusMessage?
        var hasKey = true
        var fetchFails: Bool { !hasKey }

        var effects: UsageSwitch.Effects {
            UsageSwitch.Effects(
                stopFetching: { self.calls.append("stop") },
                deleteSnapshot: { self.calls.append("delete") },
                setStatus: { self.status = $0; self.calls.append("status:\($0.text)") },
                hasKey: { self.hasKey },
                beginFetching: { self.calls.append("fetch"); return self.hasKey },
                pauseSides: { self.calls.append("pause:\($0)") },
                announce: { self.calls.append("announce") })
        }
    }

    private func make(_ defaults: MemoryDefaults = MemoryDefaults(), key: Bool = true) -> (UsageSwitch, Fakes, MemoryDefaults) {
        let fakes = Fakes()
        fakes.hasKey = key
        return (UsageSwitch(defaults: defaults, effects: fakes.effects), fakes, defaults)
    }

    private func usageOffStore() -> MemoryDefaults {
        let d = MemoryDefaults()
        SetupStore.save(SetupSettings(desktop: true, usage: ["claude": false]), to: d)
        return d
    }

    // MARK: off

    @Test func offStopsFetchingDeletesTheSnapshotAndSaves() {
        let (sw, fakes, defaults) = make()
        #expect(sw.isOn)
        sw.set(false)
        #expect(!sw.isOn)
        #expect(fakes.calls == ["pause:true", "stop", "status:\(SetupCopy.usageOffSettingsLine)", "delete", "announce"])
        #expect(fakes.status == .usageOff)
        #expect(SetupStore.load(from: defaults)?.claudeUsage == false)
        #expect(!fakes.calls.contains("fetch"))
    }

    @Test func offLeavesTheAccountsAndTheirSettingsAlone() {
        let defaults = MemoryDefaults()
        defaults.set(["Work", "Personal"], forKey: "accountLabels")
        defaults.set("Work", forKey: "activeAccount")
        defaults.set(true, forKey: "someSetting")
        let before = defaults.values.mapValues { "\($0)" }
        let (sw, _, _) = make(defaults)
        sw.set(false)
        let after = defaults.values.filter { $0.key != SetupSettings.key }.mapValues { "\($0)" }
        #expect(after == before)
    }

    @Test func offKeepsSourcesThisBuildDoesNotShow() {
        let d = MemoryDefaults()
        SetupStore.save(SetupSettings(desktop: false, usage: ["claude": true, "other": true]), to: d)
        let (sw, _, _) = make(d)
        sw.set(false)
        #expect(SetupStore.load(from: d) == SetupSettings(desktop: false, usage: ["claude": false, "other": true]))
    }

    @Test func offBeforeAnySetupMakesOneFromTheFirstRunTicks() {
        let (sw, _, d) = make()
        sw.set(false)
        #expect(SetupStore.load(from: d) == SetupSettings(desktop: true, usage: ["claude": false]))
    }

    // MARK: on

    @Test func onFetchesAtOnceWhenAKeyExists() {
        let (sw, fakes, d) = make(usageOffStore())
        #expect(!sw.isOn)
        sw.set(true)
        #expect(sw.isOn)
        #expect(fakes.calls == ["pause:false", "status:Connecting…", "fetch", "announce"])
        #expect(SetupStore.load(from: d)?.claudeUsage == true)
    }

    @Test func onWithoutAKeyIsTheSignedOutState() {
        let (sw, fakes, _) = make(usageOffStore(), key: false)
        sw.set(true)
        #expect(fakes.calls.contains("fetch"))
        #expect(fakes.status == .notSignedIn)
    }

    @Test func onWhenAlreadyOnIsANoOpAndSavesNothing() {
        let (sw, fakes, d) = make()
        sw.set(true)
        #expect(fakes.calls.isEmpty)
        #expect(SetupStore.load(from: d) == nil)
    }

    @Test func aSavedKeyTurnsUsageOnAndLeavesTheFetchToTheCaller() {
        let (sw, fakes, d) = make(usageOffStore())
        sw.set(true, fetchNow: false)
        #expect(sw.isOn)
        #expect(!fakes.calls.contains("fetch"))
        #expect(fakes.calls.contains("pause:false"))
        #expect(SetupStore.load(from: d)?.claudeUsage == true)
    }

    // MARK: launch

    @Test func launchWithUsageOffNeverFetches() {
        let (sw, fakes, _) = make(usageOffStore())
        #expect(sw.launch() == false)
        #expect(!fakes.calls.contains("fetch"))
        #expect(fakes.status == .usageOff)
        #expect(fakes.calls.contains("pause:true"))
    }

    @Test func launchWithUsageOnOrNoSetupGoesAhead() {
        let (sw, fakes, _) = make()
        #expect(sw.launch())
        #expect(fakes.calls.isEmpty)
        let d = MemoryDefaults()
        SetupStore.save(SetupSettings(desktop: true, usage: ["claude": true]), to: d)
        #expect(make(d).0.launch())
    }

    @Test func aSetupWithoutAClaudeEntryReadsAsOn() {
        let d = MemoryDefaults()
        SetupStore.save(SetupSettings(desktop: true, usage: [:]), to: d)
        #expect(SetupStore.claudeUsageOn(from: d))
        #expect(SetupStore.claudeUsageOn(from: MemoryDefaults()))
        #expect(!SetupStore.claudeUsageOn(from: usageOffStore()))
    }

    // MARK: the surfaces read usageOff

    @Test func usageOffIsNotASignInState() {
        let s = UsageViewModel.StatusMessage.usageOff
        #expect(s != .signedOut && s != .notSignedIn)
        #expect(!s.needsSignIn && !s.isError)
        #expect(s.text == "Usage tracking is off. Turn it on in Set up Sanduhr.")
        #expect(!s.text.contains("\u{2014}"))
    }

    @Test func theWidgetHidesWhileTheDeskRunsAndStaysWhenItDoesNot() {
        for setting in WidgetVisibility.allCases {
            for event in [WidgetVisibilityEvent.launch, .deskChanged, .signedIn, .signedOut, .justTheDesk, .usageChanged] {
                #expect(WidgetVisibilityRule.shouldShow(setting: setting, deskOn: true, hasSessionKey: false,
                                                        event: event, usageOn: false) == false)
            }
            #expect(WidgetVisibilityRule.shouldShow(setting: setting, deskOn: true, hasSessionKey: true,
                                                    event: .choiceChanged, usageOn: false) == nil)
        }
        // Desk off too: the usual rule, so a launch never shows nothing.
        #expect(WidgetVisibilityRule.shouldShow(setting: .whileDeskOff, deskOn: false, hasSessionKey: true,
                                                event: .launch, usageOn: false) == true)
        #expect(WidgetVisibilityRule.resolve(showing: false, setting: .always, deskOn: true, hasSessionKey: true,
                                             event: .launch, usageOn: false) == false)
    }

    @Test func turningUsageOnBringsTheWidgetBack() {
        #expect(WidgetVisibilityRule.shouldShow(setting: .always, deskOn: true, hasSessionKey: true,
                                                event: .usageChanged, usageOn: true) == true)
        #expect(WidgetVisibilityRule.shouldShow(setting: .onRequest, deskOn: true, hasSessionKey: true,
                                                event: .usageChanged, usageOn: true) == false)
        // Unchanged for everything that never passes usageOn.
        #expect(WidgetVisibilityRule.shouldShow(setting: .always, deskOn: true, hasSessionKey: true, event: .launch) == nil)
    }

    @Test func theMenuBarShowsTheHourglassAloneWithUsageOff() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 42, resetsAt: nil)
        for mode in MenuBarMode.allCases {
            #expect(MenuBarText.reading(u, mode: mode, step: 0) != nil)
            #expect(MenuBarText.reading(u, mode: mode, step: 0, usageOn: false) == nil)
        }
    }

    @Test func theDeskDrawsNoClaudePiecesAndNoSignInLineWithUsageOff() {
        var u = UsageResponse()
        u.tiers[.fiveHour] = TierUsage(utilization: 42, resetsAt: nil)
        var input = DeskUsage(usage: u, fetchedAt: Date(), signInNeeded: true, keyPresent: false)
        input.usageOff = true
        let settled = input.settled
        #expect(settled.usage == nil && settled.fetchedAt == nil && !settled.signInNeeded)
        #expect(DeskClaudeText.parts(settled) == nil)
        #expect(DeskClaudeText.line(settled) == nil)
        #expect(DeskClaudeText.compact(settled) == nil)
        #expect(!settled.metersPending(waitingSince: nil, now: Date()))
        // Signed out, by contrast, shows its sign-in line.
        let signedOut = DeskUsage(signInNeeded: true)
        #expect(DeskClaudeText.parts(signedOut) != nil)
    }

    @Test func settingsPagesSayTheUsageOffLineInsteadOfTheSignInLine() {
        let off = UsageViewModel.StatusMessage.usageOff
        #expect(NeedsAccountNote.shows(off))
        for text in [NeedsAccountNote.alerts, NeedsAccountNote.claudeCode] {
            #expect(NeedsAccountNote.line(off, signIn: text) == SetupCopy.usageOffSettingsLine)
            #expect(NeedsAccountNote.line(.signedOut, signIn: text) == text)
        }
        #expect(!NeedsAccountNote.linksToAccounts(off))
        #expect(NeedsAccountNote.linksToAccounts(.notSignedIn))
        #expect(SignedOut.usageState(label: "Work", demo: false, usageOff: true) == .usageOff)
        #expect(SignedOut.usageState(label: nil, demo: false, usageOff: true) == .usageOff)
        #expect(SignedOut.usageState(label: "Work", demo: true, usageOff: true) == .demo)
        #expect(SignedOut.usageState(label: "Work", demo: false) == .account("Work"))
        #expect(SignedOut.usageState(label: nil, demo: false) == .signedOut)
    }
}

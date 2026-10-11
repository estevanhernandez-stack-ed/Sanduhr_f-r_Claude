import Foundation
import Testing
@testable import Sanduhr

/// Settings v2, slice 4: the signed-out forms (Accounts, Usage, the welcome sheet), the dev-build
/// credential caption, empty notch wings and demo mode's coverage. In-memory stores only.
@Suite("Signed out: Accounts, Usage and the welcome sheet")
struct SignedOutTests {
    @Test func theFirstAccountCanBeNamedWork() {
        #expect(SignedOut.defaultLabel == "Personal")
        #expect(AccountRegistry.labelProblem("Work", existing: []) == nil)
    }

    @Test func dataDefaultsReadBeforeSignIn() {
        let rows = SignedOut.dataPreview(label: "Work", in: MemoryDefaults())
        #expect(rows == [
            SignedOut.DataRow(name: "Meter history", value: "30 days"),
            SignedOut.DataRow(name: "Claude Code activity", value: "Not tracked"),
            SignedOut.DataRow(name: "Share with your agents", value: "Off"),
        ])
        // A blank label previews Personal's, the label a blank form would save under.
        #expect(SignedOut.dataPreview(label: "  ", in: MemoryDefaults()) == rows)
    }

    @Test func dataDefaultsShowAChoiceAlreadyKeptUnderThatName() {
        let store = MemoryDefaults()
        AccountData.setShare(.meters, for: "Work", in: store)
        MeterHistory.set(false, for: "Work", in: store)
        let rows = SignedOut.dataPreview(label: "Work", in: store)
        #expect(rows.first { $0.name == "Share with your agents" }?.value == "Meters")
        #expect(rows.first { $0.name == "Meter history" }?.value == "Off")
        // Another name still reads the defaults.
        #expect(SignedOut.dataPreview(label: "Personal", in: store).last?.value == "Off")
    }

    @Test func keyHelpIsPlainChromeAndSafariSteps() {
        #expect(SignedOut.keyHelpTitle == "How do I find this?")
        #expect(SignedOut.chromeSteps.count >= 3 && SignedOut.safariSteps.count >= 3)
        let all = (SignedOut.chromeSteps + SignedOut.safariSteps).joined(separator: " ")
        #expect(all.contains("sessionKey"))
        #expect(all.contains("Show Web Inspector"))
        // The developer shorthand is gone (F19).
        #expect(!all.contains("DevTools") && !all.contains("→") && !all.contains("⌥⌘I"))
    }

    @Test func devBuildsSayTheKeyIsInAFile() {
        #expect(SignedOut.storeCaption(.file) == "Dev build: the key is kept in a file, not the Keychain.")
        #expect(SignedOut.storeCaption(.keychain) == nil)
        #expect(SignedOut.keyHome(.keychain) == "in your macOS Keychain")
        #expect(SignedOut.keyHome(.file).contains("dev build"))
    }

    @Test func usageIsSignedOutWithoutAnAccount() {
        #expect(SignedOut.usageState(label: nil, demo: false) == .signedOut)
        #expect(SignedOut.usageState(label: "Work", demo: false) == .account("Work"))
        // Demo mode wins over both.
        #expect(SignedOut.usageState(label: nil, demo: true) == .demo)
        #expect(SignedOut.usageState(label: "Work", demo: true) == .demo)
        #expect(SignedOut.usageLine.hasPrefix("Sign in to see your usage here"))
    }

    @Test func notNowPutsTheWelcomeSheetAwayForThisLaunch() {
        let s = MemoryDefaults()
        #expect(JustTheDesk.showsWelcome(hasSessionKey: false, in: s))
        #expect(!JustTheDesk.showsWelcome(hasSessionKey: false, notNow: true, in: s))
        // Not Now writes nothing: the next launch (a fresh flag) asks again.
        #expect(s.values.isEmpty)
        #expect(WelcomeNotNow.title == "Not Now")
    }

    @Test func theWelcomeSheetIsNoWiderThanTheWidget() {
        #expect(OnboardingSheet.width <= 340)
    }
}

/// F25: a wing whose content has nothing to show is 0 wide; the chosen width comes back with it.
@Suite("Notch: empty wings collapse")
@MainActor
struct EmptyWingTests {
    let now = Date(timeIntervalSince1970: 1_000_000)

    /// Signed out with the sign-in reminder off: the meters have nothing to show and nothing coming.
    func signedOut() -> DeskModel {
        let model = DeskModel()
        model.signInNeeded = true
        return model
    }

    func layout(_ model: DeskModel, left: NotchContent, right: NotchContent, showText: Bool = true,
                wings: Double = 36) -> NotchWingsView.Layout {
        NotchWingsView.layout(model: model, now: now, wings: wings, showText: showText,
                              left: left, right: right, idle: .automatic, font: "Helvetica", notchHeight: 32)
    }

    @Test func eachContentCollapsesOnlyWhenEmpty() {
        // Signed out with the reminder off, no meetings, no message, nothing playing, no watcher.
        let model = signedOut()
        for content in NotchContent.choices {
            let w = layout(model, left: content, right: content)
            for (width, text, shown) in [(w.left, w.leftText, w.leftContent), (w.right, w.rightText, w.rightContent)] {
                if text == nil && shown != .nothing {
                    #expect(width == 0, "\(content.rawValue) shown as \(shown.rawValue) should collapse")
                } else {
                    #expect(width >= 36, "\(content.rawValue) shown as \(shown.rawValue) keeps its width")
                }
            }
        }
    }

    @Test func claudeMetersWithoutNumbersCollapseAndComeBack() {
        let model = signedOut()
        #expect(layout(model, left: .time, right: .meters).right == 0)
        #expect(layout(model, left: .time, right: .meters).left >= 36)
        model.claudeCompact = "5h 42%  wk 63%"
        #expect(layout(model, left: .time, right: .meters).right >= 36)
        // The sign-in line is content too: the wing keeps it.
        model.claudeCompact = DeskClaudeText.compactSignIn
        #expect(layout(model, left: .time, right: .meters).right >= 36)
    }

    @Test func messageWithNoLineCollapses() {
        let model = DeskModel()
        model.message = nil
        #expect(layout(model, left: .message, right: .nothing).left == 0)
        model.message = "ship small"
        #expect(layout(model, left: .message, right: .nothing).left >= 36)
    }

    @Test func meetingOrMetersWithNeitherCollapses() {
        let model = signedOut()
        #expect(layout(model, left: .meetingOrMeters, right: .time).left == 0)
        model.meetings = DeskModel.demoMeetings(now: now)
        #expect(layout(model, left: .meetingOrMeters, right: .time).left >= 36)
    }

    @Test func emptyWatchersAndNowPlayingFallBackThenCollapse() {
        // No watcher: the right wing shows its default, Claude meters, which has nothing either.
        let model = signedOut()
        let w = layout(model, left: .watchers, right: .watchers)
        #expect(w.rightContent == .meters && w.right == 0)
        // The left default is the time, which always shows.
        #expect(w.leftContent == .meetingOrTime && w.left >= 36)
        let np = layout(model, left: .nowPlaying, right: .nowPlaying)
        #expect(np.right == 0 && np.left >= 36)
    }

    @Test func theMetersWingHoldsItsWidthThroughAnAccountSwitch() {
        // Review 2026-10-11: the old numbers are veiled while a switch fades, so the short meters
        // are briefly nil; the wing keeps its width instead of folding and growing back.
        let model = DeskModel()
        model.update(DeskUsage(usage: DemoMode.usage(for: "Work", now: now), fetchedAt: now, veiled: true))
        model.claudeCompact = nil
        #expect(model.metersPending)
        #expect(layout(model, left: .meetingOrMeters, right: .meters).right >= 36)
        #expect(layout(model, left: .meetingOrMeters, right: .meters).left >= 36)
        // A Message wing still folds: it is empty by its content, not for a moment.
        #expect(layout(model, left: .message, right: .meters).left == 0)
    }

    @Test func theMetersWingHoldsItsWidthBeforeTheFirstFetch() {
        // A key saved, its first fetch still out: no numbers, no sign-in needed.
        let model = DeskModel()
        model.update(DeskUsage())
        #expect(model.metersPending)
        #expect(layout(model, left: .time, right: .meters).right >= 36)
        // Signed out instead (only a sign-in helps): it folds.
        model.signInNeeded = true
        #expect(!model.metersPending)
        model.claudeCompact = nil
        #expect(layout(model, left: .time, right: .meters).right == 0)
        #expect(!NotchWingsView.collapses(nil, .meters, showText: true, metersPending: true))
        #expect(NotchWingsView.collapses(nil, .message, showText: true, metersPending: true))
    }

    @Test func nothingAndTextOffKeepTheChosenWidth() {
        let model = DeskModel()
        // Nothing is chosen on purpose: a plain black wing at the chosen width.
        #expect(layout(model, left: .nothing, right: .nothing, wings: 50).left == 50)
        // Text beside the camera off: the island without text keeps its width, whatever is empty.
        let off = layout(model, left: .message, right: .meters, showText: false, wings: 50)
        #expect(off.left == 50 && off.right == 50)
    }

    @Test func bothEmptyShrinksTheIslandToTheCamera() {
        let w = layout(signedOut(), left: .message, right: .meters)
        #expect(w.totalLeft == 0 && w.totalRight == 0)
        #expect(NotchWingsView.collapses(nil, .meters, showText: true))
        #expect(!NotchWingsView.collapses("", .avIndicators, showText: true))
        #expect(!NotchWingsView.collapses(nil, .nothing, showText: true))
        #expect(!NotchWingsView.collapses(nil, .meters, showText: false))
    }
}

/// F22: demo mode fills every surface with sample data that names no real account or meeting.
@Suite("Demo mode coverage")
@MainActor
struct DemoModeTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func everySurfaceHasASample() {
        let coverage = DemoMode.coverage(now: now)
        for surface in DemoMode.Surface.allCases {
            #expect((coverage[surface] ?? 0) > 0, "\(surface.rawValue) has no sample")
        }
        #expect(coverage[.accounts] == 2)
        #expect(coverage[.widget] == 3)
    }

    @Test func twoSampleAccountsWorkActive() {
        #expect(DemoMode.accounts == ["Work", "Personal"])
        #expect(DemoMode.active == "Work")
        for account in DemoMode.accounts {
            #expect(!DemoMode.usage(for: account, now: now).tiers.isEmpty)
        }
        // The two accounts read differently, so a switch shows.
        #expect(DemoMode.usage(for: "Work", now: now).tiers[.fiveHour] != DemoMode.usage(for: "Personal", now: now).tiers[.fiveHour])
    }

    @Test func theDeskGetsBothClaudePieces() {
        let model = DeskModel()
        model.update(DemoMode.deskUsage(for: "Work", now: now))
        model.refreshClaude(now: now)
        #expect(!model.meters.isEmpty)
        #expect(model.claudeLine?.hasPrefix("Work") == true)
        #expect(model.claudeCompact == "5h 42%  wk 63%")
    }

    @Test func historyEndsAtTheSampleNumbersAndStaysInRange() {
        let h = DemoMode.history(for: "Work", now: now)
        #expect(h[Tier.fiveHour.rawValue]?.last?.v == 42)
        #expect(h[Tier.sevenDay.rawValue]?.last?.v == 63)
        let all = h.values.flatMap { $0 }
        #expect(all.allSatisfy { $0.v >= 0 && $0.v <= 100 })
        #expect((h[Tier.fiveHour.rawValue]?.count ?? 0) == MeterHistory.days * 8)
        // The Usage page draws a row per limit with both accounts.
        let rows = MeterHistoryChart.rows(histories: DemoMode.histories(now: now), accounts: DemoMode.accounts,
                                          window: .month, now: now)
        #expect(rows.count == 3)
    }

    // MARK: DemoSession, the view model's on and off (review 2026-10-11)

    /// A real state unlike the samples in every field.
    var real: DemoShown {
        DemoShown(usage: UsageResponse(tiers: [.fiveHour: TierUsage(utilization: 7, resetsAt: nil)]),
                  lastUpdated: Date(timeIntervalSince1970: 1_700_000_000),
                  status: .error("HTTP 500", isAuth: false), labels: ["Alpha", "Beta"], active: "Beta",
                  signedIn: ["Beta"], history: ["five_hour": [HistoryStore.Point(t: "2026-10-01T00:00:00Z", v: 7)]])
    }

    @Test func onSwitchCycleOffRestoresTheRealStateExactly() {
        var session = DemoSession()
        let on = session.start(real, now: now)
        #expect(session.isOn)
        #expect(on.labels == ["Work", "Personal"] && on.active == "Work" && on.status == .idle)
        #expect(on.signedIn == ["Work", "Personal"] && on.usage == DemoMode.usage(for: "Work", now: now))
        // The chip cycles among the samples; a pick by hand too; a real name never shows.
        let cycled = session.next(after: on.active, now: now)
        #expect(cycled?.active == "Personal" && cycled?.usage == DemoMode.usage(for: "Personal", now: now))
        #expect(session.next(after: "Personal", now: now)?.active == "Work")
        #expect(session.pick("Work", now: now)?.active == "Work")
        #expect(session.pick("Alpha", now: now) == nil)
        // On again keeps the first kept state and the sample showing.
        #expect(session.start(cycled!, now: now).active == "Personal")
        #expect(session.end() == real)
        #expect(!session.isOn && session.end() == nil)
    }

    @Test func anAccountChangeWhileOnLandsInTheKeptState() {
        var session = DemoSession()
        _ = session.start(real, now: now)
        session.accountsChanged(labels: ["Alpha", "Beta", "Gamma"], active: "Gamma", signedIn: ["Beta", "Gamma"])
        var expected = real
        expected.labels = ["Alpha", "Beta", "Gamma"]
        expected.active = "Gamma"
        expected.signedIn = ["Beta", "Gamma"]
        #expect(session.end() == expected)
        // Off, there is nothing to keep.
        var off = DemoSession()
        off.accountsChanged(labels: ["X"], active: "X", signedIn: [])
        #expect(off.end() == nil)
        #expect(off.pick("Work", now: now) == nil && off.next(after: nil, now: now) == nil)
    }

    @Test func aFetchLandingDuringDemoIsDropped() {
        #expect(DemoSession.answerApplies(sameClient: true, demo: false))
        #expect(!DemoSession.answerApplies(sameClient: true, demo: true))
        #expect(!DemoSession.answerApplies(sameClient: false, demo: false))
    }

    @Test func demoMeetingsAreMadeUp() {
        let meetings = DeskModel.demoMeetings(now: now)
        #expect(meetings.count == 3)
        #expect(meetings.allSatisfy { $0.id.hasPrefix("demo-") })
    }
}

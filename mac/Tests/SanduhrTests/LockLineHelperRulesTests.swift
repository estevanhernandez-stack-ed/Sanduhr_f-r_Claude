import Foundation
import ServiceManagement
import Testing
@testable import Sanduhr

/// The lock screen line's root helper (#137): what it accepts from the user-writable file and
/// what it does to LoginwindowText. These are the rules root runs: build.sh compiles the same
/// file into the helper.
@Suite("Lock screen line helper rules")
struct LockLineHelperRulesTests {
    let line = "Desk 3 · Claude 29% · resets 6:10 PM · 1 waiting for you"

    @Test func acceptsTheComposedLineAndOneTrailingNewline() {
        #expect(LockLineHelperRules.accept(Data(line.utf8)) == line)
        #expect(LockLineHelperRules.accept(Data((line + "\n").utf8)) == line)
        #expect(LockLineHelperRules.accept(Data()) == "")
    }

    @Test func refusesControlBidiZeroWidthLongAndNonUTF8() {
        #expect(LockLineHelperRules.accept(Data("a\nb".utf8)) == nil)
        #expect(LockLineHelperRules.accept(Data("a\tb".utf8)) == nil)
        #expect(LockLineHelperRules.accept(Data("a\u{202E}b".utf8)) == nil)
        #expect(LockLineHelperRules.accept(Data("a\u{200B}b".utf8)) == nil)
        #expect(LockLineHelperRules.accept(Data(String(repeating: "x", count: 121).utf8)) == nil)
        #expect(LockLineHelperRules.accept(Data(String(repeating: "x", count: 300).utf8)) == nil)
        #expect(LockLineHelperRules.accept(Data([0xFF, 0xFE, 0x00])) == nil)
    }

    @Test func aNewLineIsSetAndAnOwnersMessageIsKept() {
        let fresh = LockLineHelperRules.plan(line: line, current: nil, lastWritten: nil, savedOriginal: nil)
        #expect(fresh.action == .set(line))
        #expect(fresh.keep == nil)
        let owner = LockLineHelperRules.plan(line: line, current: "If found, call the front desk",
                                             lastWritten: nil, savedOriginal: nil)
        #expect(owner.action == .set(line))
        #expect(owner.keep == "If found, call the front desk")
    }

    @Test func theSameTextIsLeftAlone() {
        let r = LockLineHelperRules.plan(line: line, current: line, lastWritten: line, savedOriginal: nil)
        #expect(r.action == .none)
    }

    @Test func anUpdatedLineReplacesOursWithoutKeepingIt() {
        let r = LockLineHelperRules.plan(line: "Claude 30%", current: line, lastWritten: line, savedOriginal: nil)
        #expect(r.action == .set("Claude 30%"))
        #expect(r.keep == nil)
    }

    @Test func turningOffRestoresTheOwnersMessageOrClears() {
        let restore = LockLineHelperRules.plan(line: "", current: line, lastWritten: line,
                                               savedOriginal: "If found, call the front desk")
        #expect(restore.action == .restore("If found, call the front desk"))
        let clear = LockLineHelperRules.plan(line: "", current: line, lastWritten: line, savedOriginal: nil)
        #expect(clear.action == .clear)
        let missing = LockLineHelperRules.plan(line: nil, current: line, lastWritten: line, savedOriginal: nil)
        #expect(missing.action == .clear)
    }

    @Test func aMessageTheOwnerSetSinceIsNeverCleared() {
        let r = LockLineHelperRules.plan(line: "", current: "my own words", lastWritten: line, savedOriginal: nil)
        #expect(r.action == .none)
        let none = LockLineHelperRules.plan(line: nil, current: nil, lastWritten: nil, savedOriginal: nil)
        #expect(none.action == .none)
    }

    @Test func theNamesLineUpWithTheBundle() {
        #expect(LockLineHelperRules.plistName(LockLineHelperRules.label) == "com.626labs.sanduhr.lockscreen.plist")
        #expect(LockLineHelperRules.devLabel == LockLineHelperRules.label + ".dev")
        #expect(LockLineHelperRules.legacyLabel == "com.626labs.sanduhr.lockline")
        #expect(LockLineHelperRules.relativePath.hasSuffix("Sanduhr/lock-line.txt"))
    }

    /// build.sh writes the plists from one template and names the labels itself (bash can't read
    /// the Swift), so the three spellings are pinned against the rules here.
    @Test func buildScriptAndTemplateUseTheRulesLabels() throws {
        let mac = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
        let template = mac.appendingPathComponent("Resources/LaunchDaemons/lockscreen-helper.plist")
        let plist = try PropertyListSerialization.propertyList(from: Data(contentsOf: template), format: nil) as? [String: Any]
        #expect(plist?["Label"] as? String == LockLineHelperRules.label)
        #expect(plist?["BundleProgram"] as? String == "Contents/MacOS/" + LockLineHelperRules.executable)
        let script = try String(contentsOf: mac.appendingPathComponent("build.sh"), encoding: .utf8)
        for label in [LockLineHelperRules.label, LockLineHelperRules.devLabel, LockLineHelperRules.legacyLabel] {
            #expect(script.contains("\"\(label)\"") || script.contains(" \(label);"), "build.sh lacks \(label)")
        }
    }
}

@Suite("Lock screen line helper in Settings")
@MainActor
struct LockLineDaemonTests {
    @Test func statusMapsToTheCaptionStates() {
        #expect(LockLineDaemon.state(.enabled) == .on)
        #expect(LockLineDaemon.state(.requiresApproval) == .needsApproval)
        #expect(LockLineDaemon.state(.notFound) == .missing)
        #expect(LockLineDaemon.state(.notRegistered) == .off)
        for s in [LockLineDaemon.State.on, .needsApproval, .missing, .off, .stalled] {
            #expect(LockLineDaemon.caption(s).hasSuffix("."))
        }
        #expect(LockLineDaemon.State.needsApproval.rawValue == "needs_approval")
        #expect(LockLineDaemon.State.stalled.rawValue == "stalled")
    }

    /// 2.39.2: a registration from an older build stopped spawning after an update while macOS still
    /// called it enabled. The helper's heartbeat tells the two apart.
    @Test func aHelperWithoutARecentHeartbeatIsStalled() {
        let t0 = Date(timeIntervalSince1970: 1_800_000_000)
        let judge = LockLineDaemon.judgeAfter, age = LockLineDaemon.heartbeatMaxAge
        // Not judged before the app has watched long enough (launch, wake, registration).
        #expect(!LockLineDaemon.stalled(heartbeat: nil, watchingSince: t0, now: t0.addingTimeInterval(judge - 1)))
        // No heartbeat at all once judged: stalled (an old helper never writes one).
        #expect(LockLineDaemon.stalled(heartbeat: nil, watchingSince: t0, now: t0.addingTimeInterval(judge)))
        // A fresh one is fine, an old one is not.
        let now = t0.addingTimeInterval(judge + 600)
        #expect(!LockLineDaemon.stalled(heartbeat: now.addingTimeInterval(-60), watchingSince: t0, now: now))
        #expect(LockLineDaemon.stalled(heartbeat: now.addingTimeInterval(-age - 1), watchingSince: t0, now: now))
        // The helper runs every minute, well inside the age; the judge waits for more than one run.
        #expect(age > 60 && judge > 60)
    }

    /// 2.39.3: a stalled helper and a refused registration both lead to the Login Items steps that
    /// brought it back on 2026-11-02, never to Sanduhr re-registering on its own.
    @Test func aStalledOrRefusedHelperGetsTheLoginItemsSteps() {
        #expect(LockLineDaemon.caption(.stalled).contains("Login Items"))
        #expect(LockLineDaemon.caption(.stalled).contains("turn this switch off"))
        let eperm = NSError(domain: NSPOSIXErrorDomain, code: Int(EPERM))
        #expect(LockLineDaemon.refused(eperm))
        #expect(LockLineDaemon.installProblem(eperm, after: .off)?.contains("Login Items") == true)
        struct Other: LocalizedError { var errorDescription: String? { "The disk is full." } }
        #expect(!LockLineDaemon.refused(Other()))
        #expect(LockLineDaemon.installProblem(Other(), after: .off) == "macOS did not install the lock screen helper: The disk is full.")
        #expect(LockLineDaemon.installProblem(eperm, after: .on) == nil)
        #expect(LockLineDaemon.stalledOff.contains("old line"))
        #expect(LockLineDaemon.stalledOff.hasSuffix("."))
    }

    @Test func theHeartbeatSitsBesideTheHelpersOtherFiles() {
        #expect(LockLineHelperRules.heartbeatPath == "/Library/Application Support/Sanduhr/lockline-alive")
        #expect(LockLineHelperRules.heartbeatPath.hasPrefix(LockLineHelperRules.stateDir + "/"))
    }

    @Test func aBundleWithTheDevPlistRegistersTheDevLabel() {
        let dev = LockLineHelperRules.plistName(LockLineHelperRules.devLabel)
        let release = LockLineHelperRules.plistName(LockLineHelperRules.label)
        let legacy = LockLineHelperRules.plistName(LockLineHelperRules.legacyLabel)
        #expect(LockLineDaemon.label(bundled: [dev, legacy]) == LockLineHelperRules.devLabel)
        #expect(LockLineDaemon.label(bundled: [release, legacy]) == LockLineHelperRules.label)
        #expect(LockLineDaemon.label(bundled: []) == LockLineHelperRules.label)
    }

    @Test func a233HelperStillRegisteredReadsAsOn() {
        #expect(LockLineDaemon.state(.notRegistered, legacy: .enabled) == .on)
        #expect(LockLineDaemon.state(.notRegistered, legacy: .notRegistered) == .off)
        #expect(LockLineDaemon.state(.notRegistered, legacy: .notFound) == .off)
        #expect(LockLineDaemon.state(.requiresApproval, legacy: .enabled) == .needsApproval)
        #expect(LockLineDaemon.state(.notFound, legacy: .notFound) == .missing)
    }

    @Test func aThrownRegisterIsOnlyAProblemWhenTheHelperIsNotThere() {
        struct E: LocalizedError { var errorDescription: String? { "Operation not permitted" } }
        #expect(LockLineDaemon.installProblem(E(), after: .on) == nil)
        #expect(LockLineDaemon.installProblem(E(), after: .needsApproval) == nil)
        // A refusal says what to do in Login Items rather than repeating macOS (2.39.3).
        #expect(LockLineDaemon.installProblem(E(), after: .off)?.contains("Login Items") == true)
    }
}

/// 2.47.1 (#356): the switch on while macOS has no registration read "Off" for 19 days, and nothing
/// said the lock screen kept an old line. The switch now takes part in the state, and a notification
/// says when the line stops updating.
@Suite("Lock screen helper repair")
@MainActor
struct LockLineRepairTests {
    let t0 = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func everySwitchAndStatusCombination() {
        let statuses: [SMAppServiceStatusCase] = [.enabled, .requiresApproval, .notRegistered, .notFound]
        for s in statuses {
            let off = LockLineDaemon.state(enabled: false, status: s.status, legacy: .notRegistered)
            let on = LockLineDaemon.state(enabled: true, status: s.status, legacy: .notRegistered)
            switch s {
            case .enabled: #expect(off == .on); #expect(on == .on)
            case .requiresApproval: #expect(off == .needsApproval); #expect(on == .needsApproval)
            case .notRegistered: #expect(off == .off); #expect(on == .unregistered)
            case .notFound: #expect(off == .missing); #expect(on == .missing)
            }
        }
        // Not found as the legacy one too: still unregistered with the switch on.
        #expect(LockLineDaemon.state(enabled: true, status: .notRegistered, legacy: .notFound) == .unregistered)
        // Needs approval whatever the legacy helper or the switch says.
        #expect(LockLineDaemon.state(enabled: true, status: .requiresApproval, legacy: .enabled) == .needsApproval)
        #expect(LockLineDaemon.state(enabled: false, status: .requiresApproval, legacy: .enabled) == .needsApproval)
        #expect(LockLineDaemon.State.unregistered.rawValue == "unregistered")
    }

    /// 2.33.0's label rule is unchanged: its helper still registered reads On, switch on or off.
    @Test func theLegacyLabelRuleIsUnchanged() {
        #expect(LockLineDaemon.state(enabled: true, status: .notRegistered, legacy: .enabled) == .on)
        #expect(LockLineDaemon.state(enabled: false, status: .notRegistered, legacy: .enabled) == .on)
    }

    @Test func theHeartbeatStillJudgesAnEnabledHelper() {
        let judge = LockLineDaemon.judgeAfter, age = LockLineDaemon.heartbeatMaxAge
        let now = t0.addingTimeInterval(judge + 600)
        #expect(LockLineDaemon.state(enabled: true, status: .enabled, legacy: .notRegistered,
                                     heartbeat: now.addingTimeInterval(-60), watchingSince: t0, now: now) == .on)
        #expect(LockLineDaemon.state(enabled: true, status: .enabled, legacy: .notRegistered,
                                     heartbeat: now.addingTimeInterval(-age - 1), watchingSince: t0, now: now) == .stalled)
        // Inside the judge window a stale heartbeat is not judged yet.
        #expect(LockLineDaemon.state(enabled: true, status: .enabled, legacy: .notRegistered,
                                     heartbeat: nil, watchingSince: t0, now: t0.addingTimeInterval(judge - 1)) == .on)
        // Not registered never reads stalled: it has its own state and button.
        #expect(LockLineDaemon.state(enabled: true, status: .notRegistered, legacy: .notRegistered,
                                     heartbeat: nil, watchingSince: t0, now: now) == .unregistered)
    }

    @Test func theUnregisteredCaptionAndProblem() {
        #expect(LockLineDaemon.caption(.unregistered)
                == "The lock screen helper isn't registered, so the lock screen keeps an old line.")
        #expect(LockLineDaemon.turnOnAgain == "Turn it on again")
        struct E: LocalizedError { var errorDescription: String? { "The disk is full." } }
        // A failed "Turn it on again" shows what macOS said, as the switch does.
        #expect(LockLineDaemon.installProblem(E(), after: .unregistered) == "macOS did not install the lock screen helper: The disk is full.")
        let eperm = NSError(domain: NSPOSIXErrorDomain, code: Int(EPERM))
        #expect(LockLineDaemon.installProblem(eperm, after: .unregistered)?.contains("Login Items") == true)
    }

    func warn(_ state: LockLineDaemon.State, enabled: Bool = true, demo: Bool = false, quiet: Bool = false,
              lastWarned: Date? = nil, now: Date? = nil) -> Bool {
        LockLineDaemon.shouldWarn(state: state, enabled: enabled, demo: demo, quiet: quiet, lastWarned: lastWarned,
                                  now: now ?? t0.addingTimeInterval(LockLineDaemon.judgeAfter), watchingSince: t0)
    }

    @Test func theWarningFiresForStalledAndUnregistered() {
        #expect(warn(.stalled))
        #expect(warn(.unregistered))
        for s in [LockLineDaemon.State.on, .off, .needsApproval, .missing] { #expect(!warn(s)) }
    }

    @Test func noWarningWithTheSwitchOffOrInDemoMode() {
        #expect(!warn(.stalled, enabled: false))
        #expect(!warn(.unregistered, enabled: false))
        #expect(!warn(.stalled, demo: true))
        #expect(!warn(.unregistered, demo: true))
    }

    @Test func theWarningWaitsForTheJudgeWindow() {
        #expect(!warn(.unregistered, now: t0.addingTimeInterval(LockLineDaemon.judgeAfter - 1)))
        #expect(!warn(.stalled, now: t0))
        #expect(warn(.unregistered, now: t0.addingTimeInterval(LockLineDaemon.judgeAfter)))
    }

    @Test func theWarningRepeatsAtMostOnceADay() {
        let now = t0.addingTimeInterval(LockLineDaemon.judgeAfter)
        #expect(!warn(.stalled, lastWarned: now.addingTimeInterval(-60), now: now))
        #expect(!warn(.stalled, lastWarned: now.addingTimeInterval(-LockLineDaemon.warnEvery + 1), now: now))
        #expect(warn(.stalled, lastWarned: now.addingTimeInterval(-LockLineDaemon.warnEvery), now: now))
        #expect(LockLineDaemon.warnEvery == 24 * 60 * 60)
    }

    /// Quiet hours hold it, as they hold agent notifications; it is not recorded, so the first check
    /// after they end fires it.
    @Test func quietHoursHoldTheWarning() {
        #expect(!warn(.stalled, quiet: true))
        #expect(warn(.stalled, quiet: false))
    }

    @Test func theWarningsWordsAndClick() {
        #expect(LockLineDaemon.warningID == "lockline-stopped")
        #expect(LockLineDaemon.warningTitle == "The lock screen line stopped updating.")
        #expect(LockLineDaemon.warningBody.hasSuffix("."))
        #expect(Notifier.clickTarget(for: LockLineDaemon.warningID) == .settings(.lockScreen))
        #expect(LockLineKeys.warnedAt == "lockLineWarnedAt")
    }
}

/// SMAppService.Status's cases, iterable for the combination test.
enum SMAppServiceStatusCase: CaseIterable {
    case enabled, requiresApproval, notRegistered, notFound
    var status: SMAppService.Status {
        switch self {
        case .enabled: .enabled
        case .requiresApproval: .requiresApproval
        case .notRegistered: .notRegistered
        case .notFound: .notFound
        }
    }
}

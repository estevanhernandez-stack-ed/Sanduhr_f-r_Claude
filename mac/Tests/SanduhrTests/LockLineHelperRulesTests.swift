import Foundation
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
        #expect(LockLineDaemon.installProblem(E(), after: .off)?.contains("Operation not permitted") == true)
    }
}

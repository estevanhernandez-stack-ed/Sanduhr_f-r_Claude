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
        #expect(LockLineHelperRules.plistName == LockLineHelperRules.label + ".plist")
        #expect(LockLineHelperRules.relativePath.hasSuffix("Sanduhr/lock-line.txt"))
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
        for s in [LockLineDaemon.State.on, .needsApproval, .missing, .off] {
            #expect(LockLineDaemon.caption(s).hasSuffix("."))
        }
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// "Just the Desk" (BACKLOG #4) folded into Set up Sanduhr in 2.41.0: the type is only the reader of
/// the two keys an older install wrote, and nothing else in the app chooses, ends or settles around it.
@Suite("Just the Desk, folded into Set up Sanduhr")
struct JustTheDeskTests {
    @Test func offUntilAnOlderInstallSetIt() {
        let s = MemoryDefaults()
        #expect(!JustTheDesk.isOn(in: s))
        s.set(true, forKey: JustTheDesk.key)
        #expect(JustTheDesk.isOn(in: s))
        s.set("yes", forKey: JustTheDesk.key)
        #expect(!JustTheDesk.isOn(in: s))
    }

    @Test func theKeysKeepTheirNames() {
        #expect(JustTheDesk.key == "justTheDesk")
        #expect(JustTheDesk.silencedKey == "justTheDeskSilencedReminder")
    }

    @Test func theDeskDrawsNoSignInLine() {
        var u = DeskUsage()
        u.signInNeeded = true
        u.firstSignIn = true
        u.signInReminder = false
        #expect(DeskClaudeText.line(u) == nil)
        #expect(DeskClaudeText.compact(u) == nil)
    }

    /// The welcome sheet's Just the Desk button and sheet path are gone: no source file calls the
    /// removed entry points, and the sheet no longer carries the button or its caption.
    @Test func noCallerOfTheRemovedButtonPathRemains() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Sources/Sanduhr")
        let files = try #require(FileManager.default.enumerator(at: root, includingPropertiesForKeys: nil))
            .compactMap { $0 as? URL }.filter { $0.pathExtension == "swift" }
        #expect(files.count > 50)
        let removed = ["chooseJustTheDesk", "endJustTheDesk", "JustTheDesk.choose", "JustTheDesk.end",
                       "JustTheDesk.endBySignIn", "JustTheDesk.showsWelcome", "JustTheDesk.signInSettled",
                       "JustTheDesk.buttonTitle", "JustTheDesk.caption", "onJustTheDesk", "showsForJustTheDesk",
                       "event: .justTheDesk", "[.justTheDesk"]
        var found: [String] = []
        for file in files {
            for (n, line) in try String(contentsOf: file, encoding: .utf8).components(separatedBy: "\n").enumerated() {
                if line.trimmingCharacters(in: .whitespaces).hasPrefix("//") { continue }
                for name in removed where line.contains(name) { found.append("\(file.lastPathComponent):\(n + 1): \(name)") }
            }
        }
        #expect(found.isEmpty, "\(found)")
        // The type itself reads the keys and does nothing else.
        let source = try String(contentsOf: root.appendingPathComponent("Models/JustTheDesk.swift"), encoding: .utf8)
        #expect(!source.contains("static func choose") && !source.contains("static func end"))
    }
}

/// Alerts and Claude Code say plainly that they start once you sign in (BACKLOG #4).
@Suite("Pages that need an account")
struct NeedsAccountNoteTests {
    @Test func showsOnlyWithoutASignIn() {
        #expect(NeedsAccountNote.shows(.notSignedIn))
        #expect(NeedsAccountNote.shows(.signedOut))
        for s: UsageViewModel.StatusMessage in [.idle, .refreshing, .connecting, .noTiers, .switching,
                                                .error("Session expired — sign in again", isAuth: true)] {
            #expect(!NeedsAccountNote.shows(s))
        }
        #expect(SettingsSection.credentials.linkTitle == "Accounts Settings…")
    }
}

import Foundation

/// The lock screen line's root helper rules (#137, docs/lock-screen-line-spec.md): what the
/// helper accepts from the user-writable `lock-line.txt`, and what it does to `LoginwindowText`.
/// Foundation only, no app types: build.sh compiles this file into the helper beside
/// `Helpers/LockLineHelper/main.swift`, and the app compiles it too, so the suite tests the
/// exact rules the root process runs.
enum LockLineHelperRules {
    /// The launchd job, and the plist in `Contents/Library/LaunchDaemons/` that
    /// `SMAppService.daemon(plistName:)` registers.
    static let label = "com.626labs.sanduhr.lockline"
    static let plistName = "com.626labs.sanduhr.lockline.plist"
    /// The helper's executable in `Contents/MacOS/`.
    static let executable = "SanduhrLockLineHelper"
    /// The file the app writes, under the console user's home.
    static let relativePath = "Library/Application Support/Sanduhr/lock-line.txt"
    /// The one preference the helper writes.
    static let domain = "com.apple.loginwindow"
    static let key = "LoginwindowText"

    /// More than the app's composer ever writes (`LockLine.maxLength`), so a valid line always
    /// fits, and small enough that nothing else rides along.
    static let maxBytes = 256
    static let maxCharacters = 120

    /// The file's text as the helper will use it: nil refuses it (not UTF-8, too long, a control,
    /// bidi or zero-width character), "" means clear. A single trailing newline is allowed.
    static func accept(_ data: Data) -> String? {
        guard data.count <= maxBytes, var text = String(data: data, encoding: .utf8) else { return nil }
        if text.hasSuffix("\n") { text.removeLast() }
        guard text.count <= maxCharacters else { return nil }
        for u in text.unicodeScalars {
            switch u.value {
            case 0x202A...0x202E, 0x2066...0x2069, 0x200E, 0x200F, 0x200B...0x200D, 0x2060, 0xFEFF:
                return nil
            default:
                if u.properties.generalCategory == .control || u.properties.generalCategory == .format { return nil }
            }
        }
        return text.trimmingCharacters(in: .whitespaces)
    }

    /// What the helper does to `LoginwindowText` this run.
    enum Action: Equatable {
        case none
        case set(String)
        /// Put back the message that was there before Sanduhr's first line.
        case restore(String)
        case clear
    }

    /// One run's decision. `line` is the accepted file text (nil when the file is missing or
    /// refused, which counts as clear), `current` the key's value now, `lastWritten` what this
    /// helper wrote last, `savedOriginal` the owner's message kept from before Sanduhr's first
    /// line. Returns the action and, when Sanduhr is about to replace a message it did not
    /// write and none is kept yet, that message to keep.
    static func plan(line: String?, current: String?, lastWritten: String?,
                     savedOriginal: String?) -> (action: Action, keep: String?) {
        let ours = current != nil && current == lastWritten
        guard let line, !line.isEmpty else {
            // Clear only what Sanduhr put there; a message the owner set since stays.
            guard ours else { return (.none, nil) }
            if let savedOriginal, !savedOriginal.isEmpty { return (.restore(savedOriginal), nil) }
            return (.clear, nil)
        }
        if current == line { return (.none, nil) }
        let keep = (current != nil && !ours && savedOriginal == nil) ? current : nil
        return (.set(line), keep)
    }
}

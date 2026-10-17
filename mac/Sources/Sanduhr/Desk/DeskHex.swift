import Foundation

/// What the hex fields take (Desk Look's Advanced, Notch text color): one six-digit hex color,
/// with or without a leading #, or two to four of them with commas for a gradient. Whitespace
/// around the text and the commas is ignored. A bad commit keeps the saved value; `Color.hex`'s
/// gray stays the last line of defence for values that reach the desktop some other way.
enum DeskHex {
    static let hint = "Six hex digits, like 9ad7ff"
    static let gradientHint = "Six hex digits, like 9ad7ff, or a comma list for a gradient"

    /// Why the text is not a color, nil when it is. The gradient wording when a comma is in it.
    static func problem(_ text: String) -> String? {
        let parts = text.split(separator: ",", omittingEmptySubsequences: false)
            .map { $0.trimmingCharacters(in: .whitespaces) }
        if (1...4).contains(parts.count), parts.allSatisfy(isColor) { return nil }
        return parts.count > 1 ? gradientHint : hint
    }

    /// What a commit does: a valid text replaces the saved value; a bad one keeps it and says why.
    static func commit(_ text: String, saved: String) -> (value: String, problem: String?) {
        if let problem = problem(text) { return (saved, problem) }
        return (normalized(text), nil)
    }

    /// The stored form: the text with the whitespace around it dropped, otherwise as typed
    /// ("#9AD7FF" and "5b8cff, a86bff" both render as they are).
    static func normalized(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Six ASCII hex digits, a leading # allowed.
    private static func isColor(_ part: String) -> Bool {
        let h = part.hasPrefix("#") ? part.dropFirst() : Substring(part)
        return h.count == 6 && h.allSatisfy { $0.isASCII && $0.isHexDigit }
    }
}

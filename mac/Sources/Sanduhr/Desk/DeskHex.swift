import Foundation

/// What the hex fields take (Desk Look's Advanced, Notch text color): one hex color of six digits,
/// or three as shorthand, with or without a leading #, or two to four of them with commas for a
/// gradient. Whitespace around the text and the commas is ignored. A shorthand is expanded on
/// commit ("9ad" is saved as "99aadd"), so the saved value is always six digits a part. A bad
/// commit keeps the saved value; `Color.hex`'s gray stays the last line of defence for values
/// that reach the desktop some other way.
enum DeskHex {
    static let hint = "Three or six hex digits, like 9ad7ff"
    static let gradientHint = "Three or six hex digits, like 9ad7ff, or a comma list for a gradient"

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

    /// The stored form: the text with the whitespace around it dropped and every three-digit part
    /// expanded to six, otherwise as typed ("#9AD7FF" and "5b8cff, a86bff" both render as they are).
    static func normalized(_ text: String) -> String {
        text.split(separator: ",", omittingEmptySubsequences: false)
            .map(expandingShorthand)
            .joined(separator: ",")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Three or six ASCII hex digits, a leading # allowed.
    private static func isColor(_ part: String) -> Bool {
        guard let digits = hexDigits(part) else { return false }
        return digits.count == 3 || digits.count == 6
    }

    /// A part's digits after an optional #, nil when any character is not an ASCII hex digit.
    private static func hexDigits(_ part: String) -> Substring? {
        let h = part.hasPrefix("#") ? part.dropFirst() : Substring(part)
        return h.allSatisfy { $0.isASCII && $0.isHexDigit } ? h : nil
    }

    /// A three-digit part with each digit doubled ("9ad" to "99aadd", "#FfF" to "#FFffFF"), its
    /// whitespace and case kept; any other part as typed.
    private static func expandingShorthand(_ part: Substring) -> String {
        let core = part.trimmingCharacters(in: .whitespaces)
        guard let digits = hexDigits(core), digits.count == 3 else { return String(part) }
        let expanded = (core.hasPrefix("#") ? "#" : "") + digits.map { "\($0)\($0)" }.joined()
        return String(part).replacingOccurrences(of: core, with: expanded)
    }
}

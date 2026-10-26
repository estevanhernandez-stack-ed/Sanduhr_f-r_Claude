import Foundation

/// The lock screen line (#137): `prefix · Claude 29% · resets 6:10 PM · 1 waiting for you`.
/// Pure: plain values in, one string out. Every slot, and the joined line, passes through the one
/// sanitizer below, so a task title, a path or a secret cannot reach the lock screen.
enum LockLine {
    static let maxLength = 80
    static let prefixMaxLength = 32
    static let staleText = "Claude usage unavailable"

    struct Slots: Equatable {
        var usage = true
        var reset = true
        var waiting = true
    }

    struct Inputs: Equatable {
        var prefix: String
        var usagePercent: Int?
        var resetsAt: Date?
        var usageStale: Bool
        var waitingCount: Int
        var show: Slots

        init(prefix: String = "", usagePercent: Int? = nil, resetsAt: Date? = nil,
             usageStale: Bool = false, waitingCount: Int = 0, show: Slots = Slots()) {
            self.prefix = prefix
            self.usagePercent = usagePercent
            self.resetsAt = resetsAt
            self.usageStale = usageStale
            self.waitingCount = waitingCount
            self.show = show
        }
    }

    enum SlotKind { case prefix, generated }

    // MARK: Compose

    static func compose(_ inputs: Inputs, now: Date, calendar: Calendar, locale: Locale) -> String {
        var slots: [String] = []
        if let p = sanitize(inputs.prefix, kind: .prefix) { slots.append(p) }

        if inputs.usageStale {
            if inputs.show.usage || inputs.show.reset, let s = sanitize(staleText, kind: .generated) { slots.append(s) }
        } else {
            if inputs.show.usage, let pct = inputs.usagePercent,
               let s = sanitize("Claude \(pct)%", kind: .generated) { slots.append(s) }
            if inputs.show.reset, inputs.usagePercent != nil, let at = inputs.resetsAt,
               let s = sanitize("resets \(clock(at, calendar: calendar, locale: locale))", kind: .generated) { slots.append(s) }
        }
        if inputs.show.waiting, inputs.waitingCount > 0,
           let s = sanitize("\(inputs.waitingCount) waiting for you", kind: .generated) { slots.append(s) }

        // Cut at a slot boundary from the right (waiting, reset, usage); the prefix goes last.
        let separator = " · "
        while slots.count > 1 && slots.joined(separator: separator).count > maxLength { slots.removeLast() }
        let line = slots.joined(separator: separator)
        return sanitize(line, kind: .generated) ?? ""
    }

    private static func clock(_ date: Date, calendar: Calendar, locale: Locale) -> String {
        let f = DateFormatter()
        f.locale = locale
        f.calendar = calendar
        f.timeZone = calendar.timeZone
        f.setLocalizedDateFormatFromTemplate("j:mm")
        return f.string(from: date)
    }

    // MARK: Sanitize

    static func sanitize(_ s: String, kind: SlotKind) -> String? {
        var text = clean(s)
        if kind == .prefix, text.count > prefixMaxLength {
            text = String(text.prefix(prefixMaxLength)).trimmingCharacters(in: .whitespaces)
        }
        guard !text.isEmpty, rejection(of: text) == nil else { return nil }
        return text
    }

    /// One line for Settings under the prefix field: nil when the prefix passes or is empty.
    static func reason(forPrefix prefix: String) -> String? {
        var text = clean(prefix)
        if text.count > prefixMaxLength {
            text = String(text.prefix(prefixMaxLength)).trimmingCharacters(in: .whitespaces)
        }
        guard !text.isEmpty, let r = rejection(of: text) else { return nil }
        switch r {
        case .path: return "Left off the lock screen: it looks like a path."
        case .address: return "Left off the lock screen: it looks like an email or a web address."
        case .secret: return "Left off the lock screen: it looks like a secret or a key."
        case .longWord: return "Left off the lock screen: one word is too long."
        }
    }

    private enum Rejection { case path, address, secret, longWord }

    /// The one place the checks live, so `sanitize` and `reason` cannot disagree.
    private static func rejection(of text: String) -> Rejection? {
        if matches(#"/\w|~/|[A-Za-z]:\\|\b\w+\.(swift|md|json|txt|py|ts|js|yaml|yml|sh|plist)\b"#, text, caseInsensitive: true) { return .path }
        if matches(#"\w@\w|://|www\."#, text, caseInsensitive: true) { return .address }
        if ConfigPack.secretLine(in: text) != nil
            || text.split(separator: " ").contains(where: { ConfigPack.looksRandom(String($0)) }) { return .secret }
        if matches(#"[A-Za-z0-9]{24,}"#, text, caseInsensitive: false) { return .longWord }
        return nil
    }

    private static func matches(_ pattern: String, _ text: String, caseInsensitive: Bool) -> Bool {
        text.range(of: pattern, options: caseInsensitive ? [.regularExpression, .caseInsensitive] : .regularExpression) != nil
    }

    /// Controls, newlines, tabs and odd spaces become spaces; bidi controls and zero-width
    /// characters are removed; runs of spaces collapse and the ends trim.
    private static func clean(_ s: String) -> String {
        var out = String.UnicodeScalarView()
        for u in s.unicodeScalars {
            switch u.value {
            case 0x202A...0x202E, 0x2066...0x2069, 0x200E, 0x200F, 0x200B...0x200D, 0x2060, 0xFEFF:
                continue
            case 0x202F, 0x00A0:
                out.append(" ")
            default:
                if u.properties.generalCategory == .control { out.append(" ") } else { out.append(u) }
            }
        }
        return String(out).split(separator: " ", omittingEmptySubsequences: true).joined(separator: " ")
    }
}

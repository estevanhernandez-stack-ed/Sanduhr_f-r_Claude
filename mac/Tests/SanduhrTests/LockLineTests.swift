import Foundation
import Testing
@testable import Sanduhr

/// The lock screen line (#137): the composer and the one sanitizer.

@Suite("Lock screen line")
struct LockLineTests {
    // 2026-10-25 18:10:00 UTC
    let now = Date(timeIntervalSince1970: 1_792_951_800)
    var resets: Date { now }

    var utc: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    func line(_ i: LockLine.Inputs, locale: String = "en_US") -> String {
        LockLine.compose(i, now: now, calendar: utc, locale: Locale(identifier: locale))
    }

    func full(prefix: String = "Desk 3") -> LockLine.Inputs {
        .init(prefix: prefix, usagePercent: 29, resetsAt: resets, usageStale: false, waitingCount: 1, show: .init())
    }

    @Test func fullLine() {
        #expect(line(full()) == "Desk 3 · Claude 29% · resets 6:10 PM · 1 waiting for you")
    }

    @Test func eachSlotSwitchedOff() {
        var i = full()
        i.show.usage = false
        #expect(line(i) == "Desk 3 · resets 6:10 PM · 1 waiting for you")
        i = full(); i.show.reset = false
        #expect(line(i) == "Desk 3 · Claude 29% · 1 waiting for you")
        i = full(); i.show.waiting = false
        #expect(line(i) == "Desk 3 · Claude 29% · resets 6:10 PM")
        i = full(prefix: "")
        #expect(line(i) == "Claude 29% · resets 6:10 PM · 1 waiting for you")
        i = full(); i.show = .init(usage: false, reset: false, waiting: false)
        #expect(line(i) == "Desk 3")
        i = full(prefix: ""); i.show = .init(usage: false, reset: false, waiting: false)
        #expect(line(i) == "")
    }

    @Test func staleUsageShowsOneGap() {
        var i = full(); i.usageStale = true
        #expect(line(i) == "Desk 3 · Claude usage unavailable · 1 waiting for you")
        i.show.usage = false
        #expect(line(i) == "Desk 3 · Claude usage unavailable · 1 waiting for you")
        i.show.reset = false
        #expect(line(i) == "Desk 3 · 1 waiting for you")
    }

    @Test func waitingCounts() {
        var i = full(); i.waitingCount = 0
        #expect(line(i) == "Desk 3 · Claude 29% · resets 6:10 PM")
        i.waitingCount = 1
        #expect(line(i).hasSuffix("1 waiting for you"))
        i.waitingCount = 3
        #expect(line(i).hasSuffix("3 waiting for you"))
    }

    @Test func twentyFourHourLocales() {
        #expect(line(full(), locale: "en_GB").contains("resets 18:10"))
        #expect(line(full(), locale: "de_DE").contains("resets 18:10"))
    }

    @Test func timeFollowsTheCalendarsZone() {
        var c = utc
        c.timeZone = TimeZone(identifier: "America/Chicago")!
        let s = LockLine.compose(full(), now: now, calendar: c, locale: Locale(identifier: "en_US"))
        #expect(s.contains("resets 1:10 PM"))
    }

    @Test func lengthCapDropsFromTheRight() {
        let p = "abcdefgh ijklmnop qrstuvwx yz"  // 29 chars
        var i = full(prefix: p)
        i.waitingCount = 12
        // 29 + 3 + 10 + 3 + 14 + 3 + 17 = 79: fits
        #expect(line(i).count <= LockLine.maxLength)
        #expect(line(i).hasSuffix("12 waiting for you"))
        i.prefix = "abcdefgh ijklmnop qrstuvwx yzabc"  // 32 chars: 82 total, waiting drops
        #expect(line(i) == "abcdefgh ijklmnop qrstuvwx yzabc · Claude 29% · resets 6:10 PM")
        // Locale with a longer reset slot pushes reset out too.
        i.usagePercent = 100
        let tight = LockLine.Inputs(prefix: String(repeating: "word ", count: 6).trimmingCharacters(in: .whitespaces),
                                    usagePercent: 29, resetsAt: resets, usageStale: true, waitingCount: 5, show: .init())
        #expect(line(tight).count <= LockLine.maxLength)
    }

    @Test func prefixSurvivesWhenAlone() {
        var i = full(prefix: "Desk 3")
        i.show = .init(usage: false, reset: false, waiting: false)
        #expect(line(i) == "Desk 3")
    }

    // MARK: The sanitizer

    static let bad: [(String, String)] = [
        ("fix login in src/auth.swift", "title with a path"),
        ("/Users/someone/notes.md", "absolute path"),
        ("~/projects", "home path"),
        ("a@b.co", "email"),
        ("https://example.com", "url"),
        ("www.example.com", "www"),
        ("aB3dE5gH7jK9mN1pQ3sT5vX7zA9cD1fG3hJ5kL7m", "random token"),
        ("C:\\temp", "drive path"),
        ("notes.md", "file extension"),
        (String(repeating: "a", count: 30), "long word"),
    ]

    @Test(arguments: bad) func badTextNeverReachesTheLine(_ text: String, _ label: String) {
        #expect(LockLine.sanitize(text, kind: .prefix) == nil, "\(label) as prefix")
        #expect(LockLine.sanitize(text, kind: .generated) == nil, "\(label) as generated")
        #expect(!line(full(prefix: text)).contains(text), "\(label) in the line")
        #expect(line(full(prefix: text)) == "Claude 29% · resets 6:10 PM · 1 waiting for you")
    }

    @Test func bidiOverrideIsStrippedThenJudged() {
        #expect(LockLine.sanitize("De\u{202E}sk 3", kind: .prefix) == "Desk 3")
        #expect(LockLine.sanitize("zero\u{200B}width\u{FEFF}", kind: .prefix) == "zerowidth")
        #expect(LockLine.sanitize("a\u{202E}@b.co", kind: .prefix) == nil)
        #expect(LockLine.sanitize("\u{202E}\u{200B}", kind: .prefix) == nil)
        #expect(LockLine.sanitize("Desk\n\t 3", kind: .prefix) == "Desk 3")
        #expect(!line(full(prefix: "Desk\u{202E} 3")).contains("\u{202E}"))
    }

    @Test func longPrefixIsCappedTo32() {
        let long = String(repeating: "word ", count: 40)
        let out = LockLine.sanitize(long, kind: .prefix)
        #expect(out != nil)
        #expect((out?.count ?? 99) <= 32)
        #expect(LockLine.sanitize(long, kind: .generated)?.count == 199)
    }

    @Test func goodPrefixSurvives() {
        #expect(LockLine.sanitize("Desk 3", kind: .prefix) == "Desk 3")
        #expect(LockLine.sanitize("   ", kind: .prefix) == nil)
        #expect(LockLine.sanitize("", kind: .prefix) == nil)
    }

    @Test func reasons() {
        #expect(LockLine.reason(forPrefix: "Desk 3") == nil)
        #expect(LockLine.reason(forPrefix: "") == nil)
        #expect(LockLine.reason(forPrefix: "~/projects") == "Left off the lock screen: it looks like a path.")
        #expect(LockLine.reason(forPrefix: "C:\\temp") == "Left off the lock screen: it looks like a path.")
        #expect(LockLine.reason(forPrefix: "a@b.co") == "Left off the lock screen: it looks like an email or a web address.")
        #expect(LockLine.reason(forPrefix: "www.example.com") == "Left off the lock screen: it looks like an email or a web address.")
        #expect(LockLine.reason(forPrefix: "aB3dE5gH7jK9mN1pQ3sT5vX7zA9cD1fG3hJ5kL7m") == "Left off the lock screen: it looks like a secret or a key.")
        #expect(LockLine.reason(forPrefix: String(repeating: "a", count: 30)) == "Left off the lock screen: one word is too long.")
    }
}

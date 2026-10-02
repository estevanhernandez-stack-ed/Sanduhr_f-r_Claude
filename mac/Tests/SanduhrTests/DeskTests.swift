import Foundation
import SwiftUI
import Testing
@testable import Sanduhr

/// Pure Desk logic: the message pick, the layout string, ink specs and the one-time import of
/// the standalone apps' settings. Nothing here touches the real defaults or messages.txt.

@Suite("Message pick")
struct MessagePickTests {
    /// Gregorian in UTC so weekdays and day boundaries do not move with the test Mac's zone.
    let cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }()

    func date(_ y: Int, _ m: Int, _ d: Int, hour: Int = 12) -> Date {
        cal.date(from: DateComponents(year: y, month: m, day: d, hour: hour))!
    }

    func pick(_ text: String, _ now: Date, hourly: Bool = false) -> String? {
        MessageEngine.pick(from: text, now: now, hourly: hourly, calendar: cal)
    }

    // 2026-10-05 is a Monday, 2026-10-31 a Saturday.

    @Test func plainLinesWhenNothingMoreSpecific() {
        #expect(pick("keep building.", date(2026, 10, 6)) == "keep building.")
    }

    @Test func weekdayBeatsPlain() {
        let text = "keep building.\nMon: one thing at a time.\nFri: showtime."
        #expect(pick(text, date(2026, 10, 5)) == "one thing at a time.")
        #expect(pick(text, date(2026, 10, 9)) == "showtime.")
        #expect(pick(text, date(2026, 10, 6)) == "keep building.")
    }

    @Test func dateBeatsWeekdayAndPlain() {
        let text = "keep building.\nSat: rest.\n10-31: happy halloween."
        #expect(pick(text, date(2026, 10, 31)) == "happy halloween.")
        #expect(pick(text, date(2026, 10, 24)) == "rest.")
    }

    @Test func tagsForOtherDaysNeverFallThroughToPlain() {
        // A Monday line on a Tuesday is not a plain line; with no plain lines there is no message.
        #expect(pick("Mon: one thing at a time.\n12-25: merry.", date(2026, 10, 6)) == nil)
    }

    @Test func commentsBlankLinesAndEmptyBodiesAreSkipped() {
        let text = "# a comment\n\n   \nMon:\n10-05:   \nkeep building."
        #expect(pick(text, date(2026, 10, 5)) == "keep building.")
        #expect(pick("# only a comment\n", date(2026, 10, 5)) == nil)
        #expect(pick("", date(2026, 10, 5)) == nil)
    }

    @Test func colonsThatAreNotTagsStayInTheLine() {
        #expect(pick("note: ship it", date(2026, 10, 5)) == "note: ship it")
        #expect(pick("10:30 standup", date(2026, 10, 5)) == "10:30 standup")
        // Tags are case sensitive: "mon:" is an ordinary line.
        #expect(pick("mon: lower", date(2026, 10, 6)) == "mon: lower")
    }

    @Test func surroundingWhitespaceIsTrimmed() {
        #expect(pick("  Mon :  spaced out.  ", date(2026, 10, 5)) == "spaced out.")
    }

    @Test func rotatesOncePerDayThroughThePool() {
        let text = "a\nb\nc"
        let picks = (5...7).map { pick(text, date(2026, 10, $0)) }
        #expect(Set(picks.compactMap { $0 }) == ["a", "b", "c"])
        // Consecutive days step through the pool in order and wrap after three.
        #expect(pick(text, date(2026, 10, 8)) == picks[0])
    }

    @Test func steadyWithinADayUnlessHourly() {
        let text = "a\nb\nc"
        #expect(pick(text, date(2026, 10, 5, hour: 1)) == pick(text, date(2026, 10, 5, hour: 23)))
        #expect(pick(text, date(2026, 10, 5, hour: 9), hourly: true)
                != pick(text, date(2026, 10, 5, hour: 10), hourly: true))
    }

    @Test func starterFileParses() {
        #expect(pick(MessageEngine.starter, date(2026, 10, 5)) == "one thing at a time.")
        #expect(pick(MessageEngine.starter, date(2026, 10, 9)) == "showtime.")
        #expect(pick(MessageEngine.starter, date(2026, 10, 7)) == "keep building.")
    }
}

@Suite("Desk layout string")
struct DeskLayoutTests {
    let standard = "message:tl clock:bl claude:bl meetings:bl"

    @Test func parsesTheDefault() {
        #expect(DeskLayout.parse(standard)
                == ["message": "tl", "clock": "bl", "claude": "bl", "meetings": "bl"])
    }

    @Test func skipsMalformedWordsAndExtraSpaces() {
        #expect(DeskLayout.parse("  clock   message:tr a:b:c :bl claude: ") == ["message": "tr"])
        #expect(DeskLayout.parse("") == [:])
    }

    @Test func repeatedWidgetKeepsItsLastSlot() {
        #expect(DeskLayout.parse("clock:bl clock:tr") == ["clock": "tr"])
    }

    @Test func placingMovesOneWidget() {
        #expect(DeskLayout.placing("clock", in: "tr", layout: standard)
                == "message:tl clock:tr claude:bl meetings:bl")
    }

    @Test func placingInNoSlotHides() {
        #expect(DeskLayout.placing("meetings", in: "", layout: standard)
                == "message:tl clock:bl claude:bl")
    }

    @Test func placingRewritesInCanonicalOrder() {
        // A hand-written order (defaults write) comes back in the order the tab lists.
        #expect(DeskLayout.placing("message", in: "br", layout: "meetings:bl clock:bl")
                == "message:br clock:bl meetings:bl")
    }

    @Test func placingDropsWordsTheTabDoesNotKnow() {
        #expect(DeskLayout.placing("clock", in: "bl", layout: "weather:tr clock:tl") == "clock:bl")
    }
}

@Suite("Ink spec")
struct InkSpecTests {
    @Test func oneColorIsDoubled() {
        #expect(Color.inkStops("ffffff", fallback: "000000") == [.hex("ffffff"), .hex("ffffff")])
    }

    @Test func severalColorsKeepTheirOrder() {
        #expect(Color.inkStops("531b93,012089,00fdff", fallback: "ffffff")
                == [.hex("531b93"), .hex("012089"), .hex("00fdff")])
    }

    @Test func spacesAroundCommasAreAllowed() {
        // The message ink used to draw " 012089" as gray.
        #expect(Color.inkStops("9ad7ff, 012089", fallback: "ffffff") == [.hex("9ad7ff"), .hex("012089")])
    }

    @Test func emptySpecUsesTheFallback() {
        #expect(Color.inkStops("", fallback: "9ad7ff") == [.hex("9ad7ff"), .hex("9ad7ff")])
        #expect(Color.inkStops(" , ", fallback: "9ad7ff") == [.hex("9ad7ff"), .hex("9ad7ff")])
    }

    @Test func hashAndShortHexWork() {
        #expect(Color.inkStops("#fff", fallback: "000000") == [.hex("ffffff"), .hex("ffffff")])
    }

    @Test func badHexIsGray() {
        #expect(Color.inkStops("nothex", fallback: "ffffff") == [.gray, .gray])
    }
}

/// In-memory stand-in for the Desk suite; nothing reaches ~/Library/Preferences.
final class MemoryDefaults: DefaultsStore {
    var values: [String: Any] = [:]
    func object(forKey key: String) -> Any? { values[key] }
    func bool(forKey key: String) -> Bool { values[key] as? Bool ?? false }
    func string(forKey key: String) -> String? { values[key] as? String }
    func set(_ value: Any?, forKey key: String) { values[key] = value }
}

/// The Desk suite plus two "legacy" domains, newer first, all in memory.
final class ScratchDefaults {
    let defaults = MemoryDefaults()
    let newer = "newer"
    let older = "older"
    private var domains: [String: [String: Any]] = [:]

    func seed(_ domain: String, _ values: [String: Any]) { domains[domain] = values }

    func migrate() {
        DeskMigration.run(into: defaults, from: [newer, older], reading: { self.domains[$0] })
    }
}

@Suite("Desk migration")
struct DeskMigrationTests {
    @Test func freshInstallLeavesDeskOff() {
        let s = ScratchDefaults()
        s.migrate()
        #expect(s.defaults.bool(forKey: DeskController.enabledKey) == false)
        #expect(s.defaults.object(forKey: "font") == nil)
        #expect(s.defaults.object(forKey: DeskController.notchKey) == nil)
        #expect(s.defaults.bool(forKey: "migrated"))
    }

    @Test func importsTheOldSettingsAndSwitchesDeskOn() {
        let s = ScratchDefaults()
        s.seed(s.older, ["layout": "clock:tr", "inkColor": "ff0000", "loginItemSet": true])
        s.migrate()
        #expect(s.defaults.bool(forKey: DeskController.enabledKey))
        #expect(s.defaults.string(forKey: "layout") == "clock:tr")
        #expect(s.defaults.string(forKey: "inkColor") == "ff0000")
        #expect(s.defaults.object(forKey: "loginItemSet") == nil)
        // The standalone apps drew in EsteFont when no font was set.
        #expect(s.defaults.string(forKey: "font") == "EsteFont 2.1")
        // Their notch defaulted on, so it stays on.
        #expect(s.defaults.bool(forKey: DeskController.notchKey))
    }

    @Test func newerAppWinsAndAKeptFontStays() {
        let s = ScratchDefaults()
        s.seed(s.newer, ["layout": "clock:bl", "font": "Avenir", "migratedFromDeskAndSanduhr": true])
        s.seed(s.older, ["layout": "clock:tr", "inkColor": "00ff00"])
        s.migrate()
        #expect(s.defaults.string(forKey: "layout") == "clock:bl")
        #expect(s.defaults.string(forKey: "inkColor") == "00ff00")
        #expect(s.defaults.string(forKey: "font") == "Avenir")
        #expect(s.defaults.object(forKey: "migratedFromDeskAndSanduhr") == nil)
    }

    @Test func aNotchSwitchedOffStaysOff() {
        let s = ScratchDefaults()
        s.seed(s.newer, [DeskController.notchKey: false])
        s.migrate()
        #expect(s.defaults.object(forKey: DeskController.notchKey) as? Bool == false)
    }

    @Test func leavesSanduhrDesksAlertSettingsBehind() {
        let s = ScratchDefaults()
        s.seed(s.newer, ["layout": "clock:bl", "alertsEnabled": true, "alertSessionPct": 50.0,
                         "alertWeeklyPct": 75.0, "remindSessionEnd": true, "alertSessionReset": true,
                         "alertsFired": ["seven_day|x|line"]])
        s.migrate()
        #expect(s.defaults.string(forKey: "layout") == "clock:bl")
        #expect(s.defaults.values.keys.filter { $0.hasPrefix("alert") || $0 == "remindSessionEnd" }.isEmpty)
    }

    @Test func neverOverwritesWhatIsAlreadySet() {
        let s = ScratchDefaults()
        s.defaults.set("message:br", forKey: "layout")
        s.seed(s.older, ["layout": "clock:tr"])
        s.migrate()
        #expect(s.defaults.string(forKey: "layout") == "message:br")
    }

    @Test func runsOnlyOnce() {
        let s = ScratchDefaults()
        s.migrate()
        s.seed(s.older, ["layout": "clock:tr"])
        s.migrate()
        #expect(s.defaults.object(forKey: "layout") == nil)
        #expect(s.defaults.bool(forKey: DeskController.enabledKey) == false)
    }

    @Test func turningDeskOffAfterMigrationSticks() {
        let s = ScratchDefaults()
        s.seed(s.older, ["layout": "clock:tr"])
        s.migrate()
        s.defaults.set(false, forKey: DeskController.enabledKey)
        s.migrate()
        #expect(s.defaults.bool(forKey: DeskController.enabledKey) == false)
    }
}

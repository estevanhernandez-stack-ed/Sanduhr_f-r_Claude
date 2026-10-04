import Foundation
import SwiftUI
import Testing
import EventKit
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

    @Test func metersIsAPieceAndStacksAfterTheClaudeLine() {
        #expect(DeskLayout.widgets.contains { $0.key == "meters" })
        #expect(DeskLayout.placing("meters", in: "br", layout: standard)
                == "message:tl clock:bl claude:bl meters:br meetings:bl")
    }
}

@Suite("Desk meters")
struct DeskMeterTests {
    /// Local noon, so "Today" in the reset text does not depend on when the suite runs.
    let now = Calendar.current.date(from: DateComponents(year: 2026, month: 10, day: 6, hour: 12))!

    func iso(_ offset: TimeInterval) -> String {
        ISO8601DateFormatter().string(from: now.addingTimeInterval(offset))
    }

    func usage(_ tiers: [Tier: TierUsage]) -> UsageResponse {
        UsageResponse(tiers: tiers, extraUsage: nil)
    }

    @Test func normalTierCarriesLabelFillPaceAndReset() {
        // Two hours left of five: three fifths of the window has gone by.
        let rows = DeskMeterRow.rows(from: usage([.fiveHour: TierUsage(utilization: 42, resetsAt: iso(2 * 3600))]), now: now)
        #expect(rows.count == 1)
        let row = rows[0]
        #expect(row.label == "Session (5hr)")
        #expect(row.percent == 42)
        #expect(abs(row.fill - 0.42) < 1e-9)
        #expect(abs((row.pace ?? -1) - 0.6) < 1e-6)
        #expect(row.pace == paceFrac(iso(2 * 3600), tier: .fiveHour, now: now))
        #expect(row.reset == resetDateTimeStr(iso(2 * 3600), now: now))
        #expect(row.reset.hasPrefix("Today "))
    }

    @Test func missingResetTimeHasNoPaceAndNoResetText() {
        let row = DeskMeterRow.rows(from: usage([.sevenDay: TierUsage(utilization: 63, resetsAt: nil)]), now: now)[0]
        #expect(row.percent == 63)
        #expect(row.pace == nil)
        #expect(row.reset == "")
    }

    @Test func overOneHundredClampsTheFillButKeepsThePercent() {
        let row = DeskMeterRow.rows(from: usage([.sevenDayOpus: TierUsage(utilization: 112.4, resetsAt: iso(86400))]), now: now)[0]
        #expect(row.percent == 112)
        #expect(row.fill == 1)
    }

    @Test func rowsFollowTheWidgetOrderAndSkipTiersWithoutUtilization() {
        let rows = DeskMeterRow.rows(from: usage([
            .sevenDayOpus: TierUsage(utilization: 5, resetsAt: nil),
            .sevenDay: TierUsage(utilization: 63, resetsAt: nil),
            .sevenDaySonnet: TierUsage(utilization: nil, resetsAt: nil),
            .fiveHour: TierUsage(utilization: 7, resetsAt: nil),
        ]), now: now)
        #expect(rows.map(\.tier) == [.fiveHour, .sevenDay, .sevenDayOpus])
        #expect(rows.map(\.label) == [Tier.fiveHour.label, Tier.sevenDay.label, Tier.sevenDayOpus.label])
    }

    @Test func noUsageMeansNoRows() {
        #expect(DeskMeterRow.rows(from: nil, now: now).isEmpty)
    }

    @Test func textLineAndNotchFollowTheSameNumbers() {
        let fresh = DeskUsage(usage: usage([.fiveHour: TierUsage(utilization: 7, resetsAt: nil),
                                            .sevenDay: TierUsage(utilization: 63, resetsAt: nil)]),
                              fetchedAt: now.addingTimeInterval(-60))
        #expect(DeskClaudeText.line(fresh) == "claude   7% session   63% week")
        #expect(DeskClaudeText.compact(fresh, now: now) == "5h 7%  wk 63%")
        #expect(!fresh.isStale(now: now))

        // Twenty minutes without a successful fetch: the line stays, dimmed; the notch drops it.
        #expect(fresh.isStale(now: now.addingTimeInterval(20 * 60)))
        #expect(DeskClaudeText.compact(fresh, now: now.addingTimeInterval(20 * 60)) == nil)

        var refused = fresh
        refused.signInNeeded = true
        #expect(DeskClaudeText.line(refused) == "claude   sign in again in Sanduhr")
        #expect(DeskClaudeText.compact(refused, now: now) == "sign in to Sanduhr")
        #expect(refused.isStale(now: now))
    }

    @Test func withTwoOrMoreAccountsTheLineStartsWithTheLabel() {
        var input = DeskUsage(usage: usage([.fiveHour: TierUsage(utilization: 7, resetsAt: nil),
                                            .sevenDay: TierUsage(utilization: 63, resetsAt: nil)]),
                              fetchedAt: now.addingTimeInterval(-60), account: "Work")
        #expect(DeskClaudeText.line(input) == "Work   7% session   63% week")
        input.account = "Work (in use)"
        #expect(DeskClaudeText.line(input) == "Work (in use)   7% session   63% week")
        input.signInNeeded = true
        #expect(DeskClaudeText.line(input) == "Work (in use)   sign in again in Sanduhr")
        // The notch has no room: it never shows the label.
        #expect(DeskClaudeText.compact(input, now: now) == "sign in to Sanduhr")
        input.signInNeeded = false
        #expect(DeskClaudeText.compact(input, now: now) == "5h 7%  wk 63%")
    }

    /// The Desk draws the label as its own clickable element (it cycles accounts), the rest apart.
    @Test func theLineSplitsIntoTheLabelAndTheRest() {
        var input = DeskUsage(usage: usage([.fiveHour: TierUsage(utilization: 7, resetsAt: nil),
                                            .sevenDay: TierUsage(utilization: 63, resetsAt: nil)]),
                              fetchedAt: now.addingTimeInterval(-60), account: "Work")
        #expect(DeskClaudeText.parts(input) == DeskClaudeText.Parts(account: "Work", rest: "7% session   63% week"))
        input.signInNeeded = true
        #expect(DeskClaudeText.parts(input) == DeskClaudeText.Parts(account: "Work", rest: "sign in again in Sanduhr"))
        // One account: no label to click, the line starts with "claude".
        input.account = nil
        #expect(DeskClaudeText.parts(input)?.account == nil)
        #expect(DeskClaudeText.line(input) == "claude   sign in again in Sanduhr")
        // No numbers: no line at all.
        #expect(DeskClaudeText.parts(DeskUsage(usage: nil, fetchedAt: nil, account: "Work")) == nil)
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

    /// The first-launch choice over the same in-memory domains, with the widget's defaults beside them.
    @discardableResult
    func firstRun(widget: MemoryDefaults) -> DeskFirstRun.Outcome {
        DeskFirstRun.run(widget: widget, desk: defaults, from: [newer, older], reading: { self.domains[$0] })
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

@Suite("Desk first run")
struct DeskFirstRunTests {
    /// Launch order in AppDelegate: the first-run choice, then the import.
    func launch(_ s: ScratchDefaults, _ widget: MemoryDefaults) -> DeskFirstRun.Outcome {
        let outcome = s.firstRun(widget: widget)
        s.migrate()
        return outcome
    }

    @Test func freshInstallMakesDeskHome() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        #expect(launch(s, widget) == .fresh)
        #expect(s.defaults.bool(forKey: DeskController.enabledKey))
        #expect(DeskLayout.parse(s.defaults.string(forKey: "layout") ?? "")["meters"] != nil)
        #expect(s.defaults.object(forKey: "showMeetings") as? Bool == false)
        #expect(s.defaults.bool(forKey: DeskController.notchKey) == false)
        #expect(WidgetVisibility.saved(in: widget) == .whileDeskOff)
        #expect(widget.object(forKey: DeskFirstRun.tuckKey) == nil)
        // The widget shows for sign-in: nothing hides it before the first fetch.
        #expect(widget.object(forKey: "panelHidden") == nil)
    }

    @Test func freshInstallShowsForSignInThenTucksOnceSignedIn() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        _ = launch(s, widget)
        let setting = WidgetVisibility.saved(in: widget)
        let deskOn = s.defaults.bool(forKey: DeskController.enabledKey)
        // AppDelegate treats a fresh install's first launch as not signed in yet.
        #expect(WidgetVisibilityRule.shouldShow(setting: setting, deskOn: deskOn, hasSessionKey: false, event: .launch) == true)
        #expect(WidgetVisibilityRule.shouldShow(setting: setting, deskOn: deskOn, hasSessionKey: true, event: .signedIn) == false)
    }

    @Test func aPendingTuckFrom22IsAdoptedAsHiddenWhileDeskIsOn() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        widget.set(true, forKey: DeskFirstRun.doneKey)
        widget.set(true, forKey: DeskFirstRun.tuckKey)
        #expect(s.firstRun(widget: widget) == .alreadyDecided)
        #expect(widget.object(forKey: DeskFirstRun.tuckKey) == nil)
        #expect(WidgetVisibility.saved(in: widget) == .whileDeskOff)
        // A choice already made wins.
        widget.set(true, forKey: DeskFirstRun.tuckKey)
        widget.set(WidgetVisibility.onRequest.rawValue, forKey: WidgetVisibility.key)
        s.firstRun(widget: widget)
        #expect(WidgetVisibility.saved(in: widget) == .onRequest)
        #expect(widget.object(forKey: DeskFirstRun.tuckKey) == nil)
    }

    @Test func existingWidgetUserIsLeftAlone() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        widget.set("{{0, 0}, {340, 520}}", forKey: "windowFrame")
        widget.set("obsidian", forKey: "theme")
        #expect(s.firstRun(widget: widget) == .existing)
        #expect(s.defaults.values.isEmpty)
        #expect(Set(widget.values.keys) == ["windowFrame", "theme", DeskFirstRun.doneKey])
        // No choice saved: Always shown, today's behavior.
        #expect(WidgetVisibility.saved(in: widget) == .always)
    }

    @Test func compactModeUserFrom204IsLeftAlone() {
        // Never moved the window and never touched a setting, but Sparkle has checked for updates.
        let s = ScratchDefaults(), widget = MemoryDefaults()
        widget.set(Date(), forKey: "SULastCheckTime")
        widget.set(true, forKey: "SUHasLaunchedBefore")
        #expect(s.firstRun(widget: widget) == .existing)
        #expect(s.defaults.values.isEmpty)
    }

    @Test func sparklesFirstLaunchKeyAloneIsStillFresh() {
        // Sparkle writes SUHasLaunchedBefore before the first-run check, even on a fresh install.
        let s = ScratchDefaults(), widget = MemoryDefaults()
        widget.set(true, forKey: "SUHasLaunchedBefore")
        #expect(s.firstRun(widget: widget) == .fresh)
    }

    @Test func userWhoRan210IsLeftAlone() {
        // 2.1.0 ran DeskMigration on every launch, so its suite is marked even with Desk off.
        let s = ScratchDefaults(), widget = MemoryDefaults()
        s.migrate()
        let before = s.defaults.values.keys.sorted()
        #expect(launch(s, widget) == .existing)
        #expect(s.defaults.values.keys.sorted() == before)
        #expect(s.defaults.bool(forKey: DeskController.enabledKey) == false)
        #expect(WidgetVisibility.saved(in: widget) == .always)
    }

    @Test func sanduhrDeskMigrantKeepsTheImportedLayout() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        s.seed(s.newer, ["layout": "clock:tr message:bl", "showMeetings": true, "notch": false])
        #expect(launch(s, widget) == .migrant)
        #expect(s.defaults.string(forKey: "layout") == "clock:tr message:bl")
        #expect(s.defaults.object(forKey: "showMeetings") as? Bool == true)
        #expect(s.defaults.object(forKey: DeskController.notchKey) as? Bool == false)
        #expect(s.defaults.bool(forKey: DeskController.enabledKey))
        #expect(WidgetVisibility.saved(in: widget) == .always)
    }

    @Test func decidedOnceThenANoOp() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        _ = launch(s, widget)
        // The user then turns Desk off, puts the Claude line back and turns meetings on.
        s.defaults.set(false, forKey: DeskController.enabledKey)
        s.defaults.set("clock:bl claude:bl", forKey: "layout")
        s.defaults.set(true, forKey: "showMeetings")
        widget.set(WidgetVisibility.always.rawValue, forKey: WidgetVisibility.key)
        #expect(launch(s, widget) == .alreadyDecided)
        #expect(s.defaults.bool(forKey: DeskController.enabledKey) == false)
        #expect(s.defaults.string(forKey: "layout") == "clock:bl claude:bl")
        #expect(s.defaults.bool(forKey: "showMeetings"))
        #expect(WidgetVisibility.saved(in: widget) == .always)
    }
}

@Suite("Widget beside the meters")
struct DeskPanelPlacementTests {
    let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
    let visible = CGRect(x: 0, y: 0, width: 1512, height: 944)   // under a 38-point menu bar
    let size = CGSize(width: 340, height: 520)

    @Test func rightOfMetersInTheBottomLeftCorner() {
        let meters = CGRect(x: 52, y: 60, width: 360, height: 120)
        let f = DeskPanelPlacement.frame(beside: meters, size: size, screen: screen, visible: visible)
        #expect(f.minX == meters.maxX + DeskPanelPlacement.gap)
        #expect(f.minY == meters.minY)
    }

    @Test func leftOfMetersInTheTopRightCorner() {
        let meters = CGRect(x: 1100, y: 830, width: 360, height: 120)
        let f = DeskPanelPlacement.frame(beside: meters, size: size, screen: screen, visible: visible)
        #expect(f.maxX == meters.minX - DeskPanelPlacement.gap)
        // Level with the meters' top would run past the menu bar, so it is pulled down.
        #expect(f.maxY == visible.maxY)
    }

    @Test func staysOnTheVisibleScreen() {
        // Wide meters in the bottom right leave no room to their left at full width.
        let meters = CGRect(x: 200, y: 10, width: 1260, height: 80)
        let f = DeskPanelPlacement.frame(beside: meters, size: size, screen: screen, visible: visible)
        #expect(visible.contains(f))
    }
}

@Suite("Calendar access")
struct CalendarAccessTests {
    @Test func notAskedYetAsksAndSaysNothing() {
        #expect(CalendarAccess.shouldRequest(.notDetermined))
        #expect(CalendarAccess.note(for: .notDetermined) == nil)
    }

    @Test func fullAccessClearsTheNote() {
        #expect(!CalendarAccess.shouldRequest(.fullAccess))
        #expect(CalendarAccess.note(for: .fullAccess) == nil)
    }

    @Test func deniedKeepsTheAllowLine() {
        #expect(!CalendarAccess.shouldRequest(.denied))
        #expect(CalendarAccess.note(for: .denied) == "Allow Sanduhr in Settings, Privacy, Calendars")
    }

    @Test func addEventsOnlyAsksForFullAccess() {
        #expect(!CalendarAccess.shouldRequest(.writeOnly))
        #expect(CalendarAccess.note(for: .writeOnly) == CalendarAccess.writeOnlyNote)
        #expect(CalendarAccess.writeOnlyNote.contains("Full Access"))
    }

    @Test func restrictedIsAPlainLine() {
        #expect(!CalendarAccess.shouldRequest(.restricted))
        #expect(CalendarAccess.note(for: .restricted) == CalendarAccess.restrictedNote)
    }

    @Test func notePointsAtTheCalendarsPrivacyPage() {
        #expect(CalendarAccess.settingsURL.absoluteString
                == "x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars")
    }
}

@Suite("Notch content")
struct NotchContentTests {
    let utc = TimeZone(identifier: "UTC")!
    /// 2026-10-03 14:05 UTC.
    let now = Date(timeIntervalSince1970: 1_791_036_300)
    let meters = "5h 7%  wk 63%"

    func meeting(_ title: String, inMinutes mins: Double, length: Double = 30) -> Meeting {
        let start = now.addingTimeInterval(mins * 60)
        return Meeting(id: title, time: "", title: title, start: start,
                       end: start.addingTimeInterval(length * 60), link: nil, service: nil)
    }

    func text(_ c: NotchContent, _ place: NotchContent.Place, meetings: [Meeting] = [],
              meters: String? = nil, message: String? = nil) -> String? {
        c.text(at: place, meetings: meetings, meters: meters, message: message, now: now, timeZone: utc)
    }

    @Test func defaultsReproduceTheIslandAsItWas() {
        #expect(NotchContent.Place.left.fallback == .meetingOrTime)
        #expect(NotchContent.Place.right.fallback == .meters)
        #expect(NotchContent.Place.strip.fallback == .meetingOrMeters)
        #expect(NotchContent.Place.left.key == "notchLeft")
        #expect(NotchContent.Place.right.key == "notchRight")
        #expect(NotchContent.Place.strip.key == "notchStrip")
    }

    @Test func unsetOrUnknownKeysMeanTheDefault() {
        #expect(NotchContent.resolve(.left, raw: nil) == .meetingOrTime)
        #expect(NotchContent.resolve(.right, raw: nil) == .meters)
        #expect(NotchContent.resolve(.strip, raw: nil) == .meetingOrMeters)
        #expect(NotchContent.resolve(.right, raw: "message") == .message)
        #expect(NotchContent.resolve(.left, raw: "sparkles") == .meetingOrTime)
        #expect(NotchContent.resolve(.strip, raw: "nothing") == .nothing)
    }

    @Test func meetingWithinTheHourBeatsTheFallback() {
        let soon = [meeting("standup", inMinutes: 12)]
        #expect(text(.meetingOrTime, .left, meetings: soon) == "standup 12m")
        #expect(text(.meetingOrTime, .right, meetings: soon) == "standup 12m")
        #expect(text(.meetingOrMeters, .strip, meetings: soon, meters: meters) == "standup in 12m")
        #expect(text(.meetingOrTime, .strip, meetings: soon) == "standup in 12m")
        let under = [meeting("standup", inMinutes: -5)]
        #expect(text(.meetingOrTime, .left, meetings: under) == "now standup")
        #expect(text(.meetingOrMeters, .strip, meetings: under, meters: meters) == "now  standup")
        // Under a minute away still reads 1m.
        #expect(text(.meetingOrTime, .left, meetings: [meeting("x", inMinutes: 0.3)]) == "x 1m")
    }

    @Test func laterOrEndedMeetingsFallBack() {
        let later = [meeting("review", inMinutes: 90)]
        #expect(text(.meetingOrTime, .left, meetings: later) == "2:05")
        #expect(text(.meetingOrMeters, .strip, meetings: later, meters: meters) == meters)
        let ended = [meeting("done", inMinutes: -40, length: 30)]
        #expect(text(.meetingOrTime, .left, meetings: ended) == "2:05")
        #expect(text(.meetingOrMeters, .strip, meetings: ended) == nil)
    }

    @Test func wingsClipLongTitlesTheStripKeepsThem() {
        let long = [meeting("quarterly planning review", inMinutes: 5)]
        #expect(text(.meetingOrTime, .left, meetings: long) == "quarterly plannin… 5m")
        #expect(text(.meetingOrMeters, .strip, meetings: long) == "quarterly planning review in 5m")
    }

    @Test func timeMetersMessageAndNothing() {
        let soon = [meeting("standup", inMinutes: 12)]
        #expect(text(.time, .left, meetings: soon) == "2:05")
        #expect(text(.meters, .right, meetings: soon, meters: meters) == meters)
        #expect(text(.message, .right, message: "keep building.") == "keep building.")
        #expect(text(.message, .strip, message: "  ship it \n") == "ship it")
        for place in [NotchContent.Place.left, .right, .strip] {
            #expect(text(.nothing, place, meetings: soon, meters: meters, message: "hi") == nil)
        }
    }

    @Test func staleMetersAndEmptyMessageLeaveThePlaceBlack() {
        // Stale numbers reach the notch as nil (DeskClaudeText.compact drops them).
        #expect(text(.meters, .right, meters: nil) == nil)
        #expect(text(.meters, .right, meters: "") == nil)
        #expect(text(.meetingOrMeters, .strip, meters: nil) == nil)
        #expect(text(.message, .left, message: nil) == nil)
        #expect(text(.message, .left, message: "   ") == nil)
    }

    @Test func everyChoiceHasALabel() {
        #expect(NotchContent.allCases.count == 6)
        #expect(Set(NotchContent.allCases.map(\.label)).count == 6)
    }
}

@Suite("Desk meter menu")
struct DeskPointerMenuTests {
    /// The plate must leave a drawn pixel in an 8-bit backing after the Desk ink's 0.92 opacity,
    /// and stay far too faint to see.
    @Test func hitPlateIsDrawnButFaint() {
        let alpha = DeskPointerMenu.hitPlateOpacity * 0.92 * 255
        #expect(alpha.rounded() >= 3)
        #expect(DeskPointerMenu.hitPlateOpacity <= 0.02)
    }

    @Test func fallbackOnlyOverUncoveredMetersWithNoOtherMenu() {
        #expect(DeskPointerMenu.fallbackOpens(overMeters: true, appWindowCovers: false, otherMenuOpen: false))
        #expect(!DeskPointerMenu.fallbackOpens(overMeters: false, appWindowCovers: false, otherMenuOpen: false))
        #expect(!DeskPointerMenu.fallbackOpens(overMeters: true, appWindowCovers: true, otherMenuOpen: false))
        // The desktop (the Finder) opened its own menu: never a second one.
        #expect(!DeskPointerMenu.fallbackOpens(overMeters: true, appWindowCovers: false, otherMenuOpen: true))
    }

    @Test func fallbackWaitsLessThanAMenuWouldFeelLate() {
        #expect(DeskPointerMenu.fallbackDelay > 0 && DeskPointerMenu.fallbackDelay <= 0.25)
    }
}

@Suite("Account switch fade")
struct AccountSwitchFadeTests {
    let start = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func timings() {
        // Item 41: a slower, gentler departure; the arrival a touch quicker.
        #expect(AccountSwitchFade.fadeOut == 0.6)
        #expect(AccountSwitchFade.fadeIn == 0.45)
        #expect(AccountSwitchFade.outAnimation(reduceMotion: false) == .easeInOut(duration: 0.6))
        #expect(AccountSwitchFade.inAnimation(reduceMotion: false) == .easeOut(duration: 0.45))
        #expect(AccountSwitchFade.nameAnimation(reduceMotion: false) == .easeInOut(duration: AccountSwitchFade.nameFade))
        // The note comes up only once the old meters are gone.
        #expect(AccountSwitchFade.noteDelay >= AccountSwitchFade.fadeOut)
        #expect(AccountSwitchFade.noteOpacity > 0 && AccountSwitchFade.noteOpacity < 1)
    }

    @Test func reduceMotionIsAPlainSwap() {
        #expect(AccountSwitchFade.outAnimation(reduceMotion: true) == nil)
        #expect(AccountSwitchFade.inAnimation(reduceMotion: true) == nil)
        #expect(AccountSwitchFade.nameAnimation(reduceMotion: true) == nil)
        #expect(AccountSwitchFade.outAnimation(reduceMotion: false) != nil)
        #expect(AccountSwitchFade.inAnimation(reduceMotion: false) != nil)
        #expect(AccountSwitchFade.holdBack(since: start, now: start, reduceMotion: true) == 0)
    }

    @Test func newNumbersWaitOutTheFadeOut() {
        // An answer 0.1 s after the switch waits the other 0.5 s; one after the fade shows at once.
        let early = AccountSwitchFade.holdBack(since: start, now: start.addingTimeInterval(0.1), reduceMotion: false)
        #expect(abs(early - 0.5) < 1e-6)
        #expect(AccountSwitchFade.holdBack(since: start, now: start.addingTimeInterval(0.6), reduceMotion: false) == 0)
        #expect(AccountSwitchFade.holdBack(since: start, now: start.addingTimeInterval(3), reduceMotion: false) == 0)
        // Outside a switch nothing waits.
        #expect(AccountSwitchFade.holdBack(since: nil, now: start, reduceMotion: false) == 0)
    }

    @Test func theNameStaysUntilTheOldNumbersAreGone() {
        var hold = AccountSwitchFade.NameHold()
        #expect(hold.shown(active: "Work") == "Work")
        hold.begin(leaving: "Personal", reduceMotion: false)
        // Fading out: the old name over the old numbers, though Work is already active.
        #expect(hold.shown(active: "Work") == "Personal")
        // A second switch mid-fade keeps the first one's name.
        hold.begin(leaving: "Work", reduceMotion: false)
        #expect(hold.shown(active: "Team") == "Personal")
        hold.release()
        #expect(hold.shown(active: "Team") == "Team")
        #expect(hold == AccountSwitchFade.NameHold())
    }

    @Test func theNameHoldsThroughTheWholeFadeOut() {
        // The name gives way when the note's wait ends, which is the fade out's end, and the new
        // numbers never show before it (holdBack).
        #expect(AccountSwitchFade.noteDelay == AccountSwitchFade.fadeOut)
        #expect(AccountSwitchFade.holdBack(since: start, now: start, reduceMotion: false) == AccountSwitchFade.fadeOut)
        #expect(AccountSwitchFade.nameFade < AccountSwitchFade.fadeOut)
    }

    @Test func reduceMotionSwapsTheNameAtOnce() {
        var hold = AccountSwitchFade.NameHold()
        hold.begin(leaving: "Personal", reduceMotion: true)
        #expect(hold.shown(active: "Work") == "Work")
        #expect(!hold.holding)
    }

    @Test func aHeldNameCanBeNone() {
        // Leaving an account the chip did not name (one account then): the chip stays away until
        // the fade out is over.
        var hold = AccountSwitchFade.NameHold()
        hold.begin(leaving: nil, reduceMotion: false)
        #expect(hold.shown(active: "Work") == nil)
    }

    @Test func theNotchNeverShowsTheOldAccountsNumbers() {
        let u = UsageResponse(tiers: [.fiveHour: TierUsage(utilization: 7, resetsAt: nil)], extraUsage: nil)
        var input = DeskUsage(usage: u, fetchedAt: start)
        #expect(DeskClaudeText.compact(input, now: start) == "5h 7%")
        input.veiled = true
        #expect(DeskClaudeText.compact(input, now: start) == nil)
    }
}

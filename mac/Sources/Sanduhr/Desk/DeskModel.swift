import Foundation
import EventKit
import Observation
import CoreGraphics
import AppKit

struct Meeting: Identifiable {
    let id: String
    let time: String
    let title: String
    let start: Date
    let end: Date
    /// Join link found in the event (Teams, Zoom, Google Meet, Webex), if any.
    let link: URL?
    /// Short name for the link's service, shown after the title on the desktop.
    let service: String?
}

/// What the Desk says about Calendar access, by authorization status. Pure, so each status is
/// tested; DeskModel reads the status and draws the note.
enum CalendarAccess {
    static let deniedNote = "Allow Sanduhr in Settings, Privacy, Calendars"
    static let writeOnlyNote = "Sanduhr needs Full Access in Settings, Privacy, Calendars"
    static let restrictedNote = "Calendar access is restricted on this Mac"

    /// Privacy & Security, Calendars in System Settings.
    static let settingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars")!

    /// The note under the meetings, or nil when there is nothing to explain (full access, or
    /// not asked yet: the request is on its way).
    static func note(for status: EKAuthorizationStatus) -> String? {
        switch status {
        case .fullAccess, .notDetermined: return nil
        case .writeOnly: return writeOnlyNote
        case .restricted: return restrictedNote
        case .denied: return deniedNote
        @unknown default: return deniedNote
        }
    }

    /// macOS shows its prompt only while the status is not determined; asking later does nothing.
    static func shouldRequest(_ status: EKAuthorizationStatus) -> Bool { status == .notDetermined }
}

/// Today's remaining timed meetings, read straight from macOS Calendar (no icalBuddy).
/// Refreshes every 5 minutes and whenever Calendar reports a change.
@Observable
final class DeskModel {
    var meetings: [Meeting] = []
    var calendarNote: String?
    /// One row per Claude limit for the meters piece, in the widget's order, hidden limits left out.
    var meters: [DeskMeterRow] = []
    /// Every limit the server reported with a utilization, hidden or not, in the widget's order:
    /// Settings, Desk, Meters lists these, so a hidden limit can be shown again.
    var reportedTiers: [Tier] = []
    /// One line of Claude usage, or nil when there is none.
    var claudeLine: String?
    /// The line in two pieces (DeskClaudeText.parts): with two or more accounts the Desk draws the
    /// label as its own clickable element, which cycles to the next account.
    var claudeParts: DeskClaudeText.Parts?
    /// The numbers are older than 15 minutes or the sign-in was refused; the meters and the
    /// line draw dimmed.
    var claudeLineIsStale = false
    /// The widget's session key or Cloudflare clearance was refused.
    var signInNeeded = false
    /// Today's line from MessageEngine (messages.txt), or nil when there is none.
    var message: String?
    /// Height of the menu bar strip at the top of the screen, so top slots sit below it.
    var topInset: CGFloat = 0
    /// The camera notch in window coordinates (top-left origin), or nil on screens without one.
    var notchRect: CGRect?
    /// The Claude meters, short enough for the notch: "5h 7%  wk 63%".
    var claudeCompact: String?
    /// Where the meeting list sits in the window (SwiftUI global coordinates, top-left origin).
    /// The app delegate lets clicks through everywhere except here, so the rows can be clicked.
    @ObservationIgnored var meetingsFrame: CGRect = .zero
    /// Each meeting row's frame, same coordinates, keyed by meeting id. Clicks are matched here.
    @ObservationIgnored var rowFrames: [String: CGRect] = [:]
    /// Where the meters sit in the window, same coordinates, or .zero when they are not drawn.
    /// The meters take clicks here; a click shows the widget beside them.
    @ObservationIgnored var metersFrame: CGRect = .zero
    /// Each meter row's frame, same coordinates: a two-finger click opens that limit's menu.
    @ObservationIgnored var meterRowFrames: [Tier: CGRect] = [:]
    /// Where the calendar note sits, same coordinates, or .zero when it is not drawn. A click
    /// here opens System Settings at Privacy & Security, Calendars.
    @ObservationIgnored var noteFrame: CGRect = .zero
    /// Where the account label at the start of the claude line sits, same coordinates, or .zero
    /// with one account or no line. A click here switches to the next account.
    @ObservationIgnored var accountFrame: CGRect = .zero
    /// Alert pulses so far, per limit (Settings, Alerts, Where alerts show). A meter row pulses
    /// when its count goes up.
    var pulses: [Tier: Int] = [:]
    /// Every pulse so far, whatever the limit; the notch island pulses when it goes up.
    var pulseCount = 0
    /// The one-time hint under the meters (DeskMeterHint) is still due.
    var meterHintVisible = false
    @ObservationIgnored private let meterHint = DeskMeterHint()

    /// The widget's last numbers (see `update`).
    @ObservationIgnored private var usage = DeskUsage()

    /// Replaced when access turns full: a store made before the grant can keep the old answer.
    @ObservationIgnored private var store = EKEventStore()
    /// The status seen at the last check, so a change to full access is noticed once.
    @ObservationIgnored private var calendarStatus: EKAuthorizationStatus?
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var claudeTimer: Timer?
    @ObservationIgnored private var storeObserver: NSObjectProtocol?
    @ObservationIgnored private var defaultsObserver: NSObjectProtocol?

    func start() {
        // Calendar access is asked only when meetings are on (Desk settings, General).
        if Self.meetingsOn { requestCalendar() }
        observeStore()
        observeMeterSettings()
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.refreshEvents()
        }
        MessageEngine.ensureFile()
        refreshClaude()
        message = MessageEngine.current()
        claudeTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.refreshClaude()
            if self?.demo != true { self?.message = MessageEngine.current() }   // picks up messages.txt edits within a minute
            // A grant made in System Settings shows within a minute, no relaunch.
            if self?.calendarStatus != .fullAccess { self?.recheckCalendar() }
        }
    }

    private static var meetingsOn: Bool { UserDefaults.desk.object(forKey: "showMeetings") as? Bool ?? true }

    /// A Meters setting changed in Settings: restyle the rows now, not at the next minute.
    /// Any in-process defaults change counts (cheap: unchanged rows are not reassigned). A
    /// `defaults write` from another process posts nothing; the minute refresh picks that up.
    private func observeMeterSettings() {
        guard defaultsObserver == nil else { return }
        defaultsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.refreshMeterWarnings() }
    }

    private func observeStore() {
        if let storeObserver { NotificationCenter.default.removeObserver(storeObserver) }
        storeObserver = NotificationCenter.default.addObserver(
            forName: .EKEventStoreChanged, object: store, queue: .main
        ) { [weak self] _ in self?.refreshEvents() }
    }

    /// Asks for Calendar access (macOS shows its prompt only the first time) and loads today's
    /// meetings. Runs when Desk starts with meetings on, and when meetings are switched on later:
    /// new installs start with them off.
    func requestCalendar() {
        let status = EKEventStore.authorizationStatus(for: .event)
        guard CalendarAccess.shouldRequest(status) else { applyCalendar(status); return }
        store.requestFullAccessToEvents { [weak self] _, _ in
            DispatchQueue.main.async {
                self?.applyCalendar(EKEventStore.authorizationStatus(for: .event))
            }
        }
    }

    /// Reads the authorization again: when Sanduhr becomes active, when Settings opens and each
    /// minute while access is not full. Only while meetings are on. Asks if never asked.
    func recheckCalendar() {
        guard Self.meetingsOn else { return }
        requestCalendar()
    }

    /// Shows the note for `status`, or, on full access, loads the meetings. A change to full
    /// access gets a fresh store, so the old one's stale answer cannot hide the meetings.
    private func applyCalendar(_ status: EKAuthorizationStatus) {
        let previous = calendarStatus
        calendarStatus = status
        if status == .fullAccess {
            if previous != nil, previous != .fullAccess {
                store = EKEventStore()
                if storeObserver != nil { observeStore() }
            }
            refreshEvents()
        } else {
            meetings = []
            let note = CalendarAccess.note(for: status)
            if calendarNote != note { calendarNote = note }
        }
    }

    /// The calendar note was clicked: Privacy & Security, Calendars in System Settings.
    func openCalendarSettings() {
        NSWorkspace.shared.open(CalendarAccess.settingsURL)
    }

    func stop() {
        timer?.invalidate(); timer = nil
        claudeTimer?.invalidate(); claudeTimer = nil
        // A block observer is removed by its token; removeObserver(self) would leave it behind
        // and every Desk off and on would add another.
        if let storeObserver { NotificationCenter.default.removeObserver(storeObserver) }
        storeObserver = nil
    }

    /// Demo data for screenshots (`smoke do demo on`): while on, the calendar refresh and the
    /// message timer leave these alone. Nothing is written to the calendar or to defaults.
    @ObservationIgnored private(set) var demo = false

    func setDemo(_ on: Bool, now: Date = Date()) {
        demo = on
        if on {
            meetings = Self.demoMeetings(now: now)
            message = "ship small. ship often. sleep anyway."
            calendarNote = nil
        } else {
            meetings = []
            refreshEvents()
            message = MessageEngine.current()
        }
    }

    /// Three made-up meetings: one in 12 minutes (so the notch counts it down), two later today.
    static func demoMeetings(now: Date) -> [Meeting] {
        let fmt = DateFormatter()
        fmt.dateFormat = "h:mm"
        let minute: TimeInterval = 60
        let plan: [(id: String, title: String, inMinutes: Double, length: Double, link: String?)] = [
            ("demo-1", "Design review", 12, 30, "https://zoom.us/j/1234567890"),
            ("demo-2", "Pairing: notch glow", 75, 45, "https://meet.google.com/abc-defg-hij"),
            ("demo-3", "Ship 2.3", 180, 30, nil),
        ]
        return plan.map { p in
            let start = now.addingTimeInterval(p.inMinutes * minute)
            let link = p.link.flatMap(URL.init(string:))
            return Meeting(id: p.id, time: fmt.string(from: start), title: p.title, start: start,
                           end: start.addingTimeInterval(p.length * minute), link: link,
                           service: link.flatMap(serviceName))
        }
    }

    func refreshEvents() {
        guard !demo else { return }
        guard EKEventStore.authorizationStatus(for: .event) == .fullAccess else { return }
        let now = Date()
        let endOfDay = Calendar.current.startOfDay(for: now).addingTimeInterval(24 * 60 * 60)
        let predicate = store.predicateForEvents(withStart: now, end: endOfDay, calendars: nil)
        let fmt = DateFormatter()
        fmt.dateFormat = "h:mm"
        let upcoming = store.events(matching: predicate)
            .filter { !$0.isAllDay && $0.endDate > now }
            .sorted { $0.startDate < $1.startDate }
        meetings = upcoming.prefix(3).map { event in
            let link = Self.joinLink(event)
            return Meeting(id: event.eventIdentifier ?? UUID().uuidString,
                           time: fmt.string(from: event.startDate),
                           title: event.title ?? "",
                           start: event.startDate,
                           end: event.endDate,
                           link: link,
                           service: link.flatMap(Self.serviceName))
        }
        calendarNote = nil
    }

    /// Takes the widget's latest numbers. AppDelegate calls this after every refresh, whether
    /// or not Desk or the widget is showing, so Desk has them the moment it starts.
    func update(_ input: DeskUsage) {
        usage = input
        refreshClaude()
    }

    /// Rebuilds the meters and the text lines from the last numbers the widget handed over.
    /// Runs on every update and once a minute, so pace ticks and staleness move with the clock.
    func refreshClaude(now: Date = Date()) {
        meters = Self.meterRows(usage.usage, now: now)
        let reported = Tier.allCases.filter { usage.usage?.tiers[$0]?.utilization != nil }
        if reported != reportedTiers { reportedTiers = reported }
        signInNeeded = usage.signInNeeded
        claudeLine = DeskClaudeText.line(usage)
        let parts = DeskClaudeText.parts(usage)
        if parts != claudeParts { claudeParts = parts }
        claudeCompact = DeskClaudeText.compact(usage, now: now)
        claudeLineIsStale = usage.isStale(now: now)
        let hint = meterHint.isVisible(now: now)
        if meterHintVisible != hint { meterHintVisible = hint }
    }

    /// The meter rows for the limits that show, with the saved warning settings (Settings, Desk, Meters).
    private static func meterRows(_ usage: UsageResponse?, now: Date) -> [DeskMeterRow] {
        let desk = UserDefaults.desk
        let shown = MeterVisibility.visible(usage, hidden: MeterVisibility.hidden(in: desk))
        return DeskMeterRow.rows(from: shown, now: now) { MeterWarningSettings.saved($0, in: desk) }
    }

    /// Re-applies the warning settings at once: a Meters setting changed. Rows that already match
    /// are left alone, so the Desk only redraws when a row turns red or back.
    func refreshMeterWarnings(now: Date = Date()) {
        let rows = Self.meterRows(usage.usage, now: now)
        if rows != meters { meters = rows }
    }

    /// An alert chose the Desk: pulse these limits' meters once. `pulseCount` counts every pulse;
    /// the notch glow that goes with it is fired by `DeskController.pulse`.
    func pulse(_ tiers: Set<Tier>) {
        for tier in tiers { pulses[tier, default: 0] += 1 }
        pulseCount += 1
    }

    /// The hint was drawn under the meters; its three days start now if they have not already.
    func meterHintShown() { meterHint.markShown(now: Date()) }

    /// The meters were clicked: the hint has done its job.
    func meterHintDismissed() {
        meterHint.dismiss()
        meterHintVisible = false
    }

    /// The first meeting still to come (or in progress) that has a join link.
    var nextJoinable: Meeting? { meetings.first { $0.link != nil } }

    /// Looks for a join link in the event's URL, location and notes. Notes are only searched
    /// here, on this Mac; nothing but the time, title and service name is ever shown.
    static func joinLink(_ event: EKEvent) -> URL? {
        let haystack = [event.url?.absoluteString, event.location, event.notes]
            .compactMap { $0 }
            .joined(separator: " ")
        let patterns = [
            #"https://teams\.microsoft\.com/(l/meetup-join|meet)/[^\s<>"]+"#,
            #"https://teams\.live\.com/meet/[^\s<>"]+"#,
            #"https://[\w.-]*zoom\.us/j/[^\s<>"]+"#,
            #"https://meet\.google\.com/[a-z-]+"#,
            #"https://[\w.-]+\.webex\.com/[^\s<>"]+"#,
        ]
        for pattern in patterns {
            if let range = haystack.range(of: pattern, options: .regularExpression) {
                return URL(string: String(haystack[range]))
            }
        }
        return nil
    }

    static func serviceName(_ url: URL) -> String? {
        let host = url.host ?? ""
        if host.contains("teams") { return "teams" }
        if host.contains("zoom") { return "zoom" }
        if host.contains("meet.google") { return "meet" }
        if host.contains("webex") { return "webex" }
        return nil
    }
}

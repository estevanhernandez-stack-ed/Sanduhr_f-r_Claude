import Foundation
import EventKit
import Observation
import CoreGraphics

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

/// Today's remaining timed meetings, read straight from macOS Calendar (no icalBuddy).
/// Refreshes every 5 minutes and whenever Calendar reports a change.
@Observable
final class DeskModel {
    var meetings: [Meeting] = []
    var calendarNote: String?
    /// One row per Claude limit for the meters piece, in the widget's order.
    var meters: [DeskMeterRow] = []
    /// One line of Claude usage, or nil when there is none.
    var claudeLine: String?
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
    /// The one-time hint under the meters (DeskMeterHint) is still due.
    var meterHintVisible = false
    @ObservationIgnored private let meterHint = DeskMeterHint()

    /// The widget's last numbers (see `update`).
    @ObservationIgnored private var usage = DeskUsage()

    @ObservationIgnored private let store = EKEventStore()
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var claudeTimer: Timer?
    @ObservationIgnored private var storeObserver: NSObjectProtocol?

    func start() {
        // Calendar access is asked only when meetings are on (Desk settings, General).
        if UserDefaults.desk.object(forKey: "showMeetings") as? Bool ?? true { requestCalendar() }
        storeObserver = NotificationCenter.default.addObserver(
            forName: .EKEventStoreChanged, object: store, queue: .main
        ) { [weak self] _ in self?.refreshEvents() }
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.refreshEvents()
        }
        MessageEngine.ensureFile()
        refreshClaude()
        message = MessageEngine.current()
        claudeTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.refreshClaude()
            self?.message = MessageEngine.current()   // picks up messages.txt edits within a minute
        }
    }

    /// Asks for Calendar access (macOS shows its prompt only the first time) and loads today's
    /// meetings. Runs when Desk starts with meetings on, and when meetings are switched on later:
    /// new installs start with them off.
    func requestCalendar() {
        store.requestFullAccessToEvents { [weak self] granted, _ in
            DispatchQueue.main.async {
                guard let self else { return }
                if granted {
                    self.refreshEvents()
                } else {
                    self.calendarNote = "Allow Sanduhr in Settings, Privacy, Calendars"
                }
            }
        }
    }

    func stop() {
        timer?.invalidate(); timer = nil
        claudeTimer?.invalidate(); claudeTimer = nil
        // A block observer is removed by its token; removeObserver(self) would leave it behind
        // and every Desk off and on would add another.
        if let storeObserver { NotificationCenter.default.removeObserver(storeObserver) }
        storeObserver = nil
    }

    func refreshEvents() {
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
        meters = DeskMeterRow.rows(from: usage.usage, now: now)
        signInNeeded = usage.signInNeeded
        claudeLine = DeskClaudeText.line(usage)
        claudeCompact = DeskClaudeText.compact(usage, now: now)
        claudeLineIsStale = usage.isStale(now: now)
        let hint = meterHint.isVisible(now: now)
        if meterHintVisible != hint { meterHintVisible = hint }
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

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
    /// One line of Claude usage from Sanduhr's snapshot.json, or nil when there is none.
    var claudeLine: String?
    var claudeLineIsStale = false
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

    @ObservationIgnored private let store = EKEventStore()
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var claudeTimer: Timer?

    func start() {
        // Calendar access is asked only when meetings are on (Desk settings, General).
        if UserDefaults.desk.object(forKey: "showMeetings") as? Bool ?? true {
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
        NotificationCenter.default.addObserver(
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

    func stop() {
        timer?.invalidate(); timer = nil
        claudeTimer?.invalidate(); claudeTimer = nil
        NotificationCenter.default.removeObserver(self)
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

    /// Reads ~/Library/Application Support/Sanduhr/snapshot.json, which the Sanduhr widget
    /// rewrites after every fetch (about every 5 minutes). Older than 15 minutes counts as stale.
    func refreshClaude() {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Sanduhr/snapshot.json")
        guard let data = try? Data(contentsOf: url),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            claudeLine = nil
            claudeCompact = nil
            return
        }
        let tiers = (obj["tiers"] as? [[String: Any]]) ?? []
        func util(_ key: String) -> Int? {
            tiers.first { ($0["key"] as? String) == key }?["utilization"] as? Int
        }
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        func date(_ s: Any?) -> Date? {
            guard let s = s as? String else { return nil }
            return iso.date(from: s) ?? ISO8601DateFormatter().date(from: s)
        }
        let captured = date(obj["captured_at"])
        let age = captured.map { Date().timeIntervalSince($0) } ?? .infinity

        if (obj["status"] as? String) == "error", ["auth", "session_expired", "cloudflare"].contains(obj["error_kind"] as? String ?? "") {
            claudeLine = "claude   sign in again in Sanduhr"
            claudeCompact = nil
            claudeLineIsStale = true
            return
        }
        var parts: [String] = []
        if let s = util("five_hour") {
            var part = "\(s)% session"
            if let reset = date(tiers.first { ($0["key"] as? String) == "five_hour" }?["resets_at"]) {
                let f = DateFormatter()
                f.dateFormat = "h:mm"
                part += ", resets \(f.string(from: reset))"
            }
            parts.append(part)
        }
        if let w = util("seven_day") { parts.append("\(w)% week") }
        claudeCompact = [util("five_hour").map { "5h \($0)%" }, util("seven_day").map { "wk \($0)%" }]
            .compactMap { $0 }.joined(separator: "  ")
        if claudeCompact?.isEmpty == true || age > 15 * 60 { claudeCompact = nil }
        guard !parts.isEmpty else { claudeLine = nil; return }
        claudeLine = "claude   " + parts.joined(separator: "   ")
        claudeLineIsStale = age > 15 * 60
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

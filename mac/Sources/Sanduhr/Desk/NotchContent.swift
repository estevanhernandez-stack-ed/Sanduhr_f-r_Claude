import Foundation

/// What one place on the notch island shows: the left wing, the right wing or the strip under
/// the camera. Settings, Desk, Notch picks one per place; a key that was never set means that
/// place's default, which reproduces the island as it was before the choice existed.
///
///   defaults write com.626labs.sanduhr.desk notchLeft -string time       (left wing)
///   defaults write com.626labs.sanduhr.desk notchRight -string message   (right wing)
///   defaults write com.626labs.sanduhr.desk notchStrip -string nothing   (strip under the camera)
///
/// `notchText` and `notchChinText` still decide whether the wings and the strip show any text.
enum NotchContent: String, CaseIterable, Identifiable {
    /// The next meeting when one is under way or starts within the hour, otherwise the time.
    case meetingOrTime
    /// The next meeting within the hour, otherwise the Claude meters: the strip's own default.
    case meetingOrMeters
    case time
    /// The compact Claude meters ("5h 7%  wk 63%"), absent while the numbers are stale; "sign in
    /// to Sanduhr" while signed out or refused.
    case meters
    /// Today's Desk message line.
    case message
    /// What plays on the Mac ("▶ Title · Artist", item 53), absent when nothing plays or, with
    /// Hide while paused, while paused. Choosing it is enough: now playing runs while placed
    /// somewhere (NowPlayingPlacement).
    case nowPlaying
    case nothing

    var id: String { rawValue }

    var label: String {
        switch self {
        case .meetingOrTime: "Next meeting, or the time"
        case .meetingOrMeters: "Next meeting, or the Claude meters"
        case .time: "Time"
        case .meters: "Claude meters"
        case .message: "Message"
        case .nowPlaying: "Now playing"
        case .nothing: "Nothing"
        }
    }

    /// The three places on the island, with their desk keys and defaults.
    enum Place {
        case left, right, strip

        var key: String {
            switch self {
            case .left: "notchLeft"
            case .right: "notchRight"
            case .strip: "notchStrip"
            }
        }

        var fallback: NotchContent {
            switch self {
            case .left: .meetingOrTime
            case .right: .meters
            case .strip: .meetingOrMeters
            }
        }
    }

    /// The saved choice for `place`, or its default when unset or unreadable.
    static func saved(_ place: Place, in defaults: UserDefaults) -> NotchContent {
        resolve(place, raw: defaults.string(forKey: place.key))
    }

    /// A saved raw value for `place` as a choice: unset or unknown means the default.
    static func resolve(_ place: Place, raw: String?) -> NotchContent {
        raw.flatMap(NotchContent.init(rawValue:)) ?? place.fallback
    }

    /// The text for this choice at `place`, or nil for none (the place stays plain black).
    /// The wings are short of room, so their meeting line clips long titles; the strip has the
    /// width under the camera and keeps them whole.
    func text(at place: Place, meetings: [Meeting], meters: String?, message: String?,
              nowPlaying: NowPlayingInfo? = nil, now: Date, timeZone: TimeZone = .current) -> String? {
        switch self {
        case .meetingOrTime:
            return Self.meeting(meetings, place: place, now: now) ?? Self.time(now, timeZone)
        case .meetingOrMeters:
            return Self.meeting(meetings, place: place, now: now) ?? Self.nonEmpty(meters)
        case .time:
            return Self.time(now, timeZone)
        case .meters:
            return Self.nonEmpty(meters)
        case .message:
            return Self.nonEmpty(message?.trimmingCharacters(in: .whitespacesAndNewlines))
        case .nowPlaying:
            return NowPlayingText.line(nowPlaying, at: place)
        case .nothing:
            return nil
        }
    }

    /// The next meeting that has not ended, when it is under way or starts within the hour.
    private static func meeting(_ meetings: [Meeting], place: Place, now: Date) -> String? {
        guard let next = meetings.first(where: { $0.end > now }) else { return nil }
        let mins = Int(next.start.timeIntervalSince(now) / 60)
        switch place {
        case .left, .right:
            let title = next.title.count > 18 ? String(next.title.prefix(17)) + "…" : next.title
            if next.start <= now { return "now \(title)" }
            if mins < 60 { return "\(title) \(max(1, mins))m" }
        case .strip:
            if next.start <= now { return "now  \(next.title)" }
            if mins < 60 { return "\(next.title) in \(max(1, mins))m" }
        }
        return nil
    }

    private static func time(_ now: Date, _ zone: TimeZone) -> String {
        let f = DateFormatter()
        f.timeZone = zone
        f.dateFormat = "h:mm"
        return f.string(from: now)
    }

    private static func nonEmpty(_ s: String?) -> String? {
        guard let s, !s.isEmpty else { return nil }
        return s
    }
}

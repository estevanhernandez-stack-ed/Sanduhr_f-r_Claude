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
    /// What plays on the Mac ("▶ Title · Artist", item 53). When nothing plays or, with Hide
    /// while paused, while paused, the place shows the When nothing is playing choice instead
    /// (`effective`, NowPlayingIdle). Choosing it is enough: now playing runs while placed
    /// somewhere (NowPlayingPlacement).
    case nowPlaying
    /// The most urgent watcher with a count of the rest (item 66): its full line once when it
    /// changes (WatcherIntro), then "PR 140 · 4/12 +1". With no watcher the place shows its own default (`effective`), as now playing stands aside.
    case watchers
    /// The camera and mic indicators (item 67): the red dot and the mic glyph while they are in use
    /// and switched on; no text. With nothing to show the place shows its own default (`effective`).
    /// Choosing it for a place moves them there from beside the camera (AVIndicatorPlacement).
    case avIndicators
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
        case .watchers: "Watchers"
        case .avIndicators: "Camera and mic"
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

    /// What `place` actually shows. A place on Now playing with no now playing line (nothing
    /// plays, Hide while paused while paused, the app switched off, now playing unavailable)
    /// shows the When nothing is playing choice instead, and behaves fully like that content: its
    /// text, its width, its clicks. Every other choice is itself. The saved choice still places
    /// now playing (NowPlayingPlacement), so it keeps running and comes back with the next track.
    /// A place on Watchers with no watcher to show (item 66) shows the place's own default, and so
    /// does a place on Camera and mic with neither indicator showing (item 67).
    static func effective(_ content: NotchContent, at place: Place, hasLine: Bool,
                          idle: NowPlayingIdle, hasWatcher: Bool = false,
                          hasIndicators: Bool = false) -> NotchContent {
        if content == .avIndicators { return hasIndicators ? .avIndicators : place.fallback }
        if content == .watchers { return hasWatcher ? .watchers : place.fallback }
        guard content == .nowPlaying, !hasLine else { return content }
        return idle.content(at: place)
    }

    /// `effective` with the line taken from what plays and the watchers that show.
    static func effective(_ content: NotchContent, at place: Place, nowPlaying: NowPlayingInfo?,
                          idle: NowPlayingIdle, watchers: [Watcher] = [],
                          indicators: AVIndicators = AVIndicators()) -> NotchContent {
        effective(content, at: place, hasLine: NowPlayingText.line(nowPlaying, at: place) != nil, idle: idle,
                  hasWatcher: !watchers.isEmpty, hasIndicators: indicators.any)
    }

    /// The text for this choice at `place`, or nil for none (the place stays plain black).
    /// The wings are short of room, so their meeting line clips long titles; the strip has the
    /// width under the camera and keeps them whole.
    func text(at place: Place, meetings: [Meeting], meters: String?, message: String?,
              nowPlaying: NowPlayingInfo? = nil, watchers: [Watcher] = [], watcherIntro: Bool = false, now: Date,
              timeZone: TimeZone = .current) -> String? {
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
            // The line as it reads: its effect tags gone, a Unicode letter style applied (item 65).
            return Self.nonEmpty(message.map { MessageTypography.characters(MessageMarkup.parse($0.trimmingCharacters(in: .whitespacesAndNewlines))) })
        case .nowPlaying:
            return NowPlayingText.line(nowPlaying, at: place)
        case .watchers:
            return WatcherText.notchLine(watchers, intro: watcherIntro, now: now)
        case .avIndicators:
            // Drawn, not written (AVIndicatorView).
            return nil
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

/// What a notch place set to Now playing shows while there is no now playing line (Settings,
/// Desk, Now Playing, When nothing is playing). Unset or unknown means `automatic`: the place's
/// own default content (NotchContent.Place.fallback). `nothing` leaves the place plain black, as
/// before the choice existed. The Desk line has no stand-in: it simply hides.
///
///   defaults write com.626labs.sanduhr.desk nowPlayingIdle -string time
enum NowPlayingIdle: String, CaseIterable, Identifiable {
    case automatic
    case meetingOrTime
    case meetingOrMeters
    case time
    case meters
    case message
    case nothing

    static let key = "nowPlayingIdle"

    var id: String { rawValue }

    /// The content shown at `place` in now playing's stead.
    func content(at place: NotchContent.Place) -> NotchContent {
        self == .automatic ? place.fallback : NotchContent(rawValue: rawValue) ?? place.fallback
    }

    var label: String {
        self == .automatic ? "What that spot shows by default"
            : NotchContent(rawValue: rawValue)?.label ?? rawValue
    }

    /// The Notch page's line under a place set to Now playing: "When nothing plays: Time."; for
    /// automatic, the place's own default ("When nothing plays: Claude meters (its default).").
    func caption(at place: NotchContent.Place) -> String {
        let shown = content(at: place).label
        return self == .automatic ? "When nothing plays: \(shown) (its default)." : "When nothing plays: \(shown)."
    }

    /// A saved raw value as a choice: unset or unknown means automatic.
    static func resolve(raw: String?) -> NowPlayingIdle {
        raw.flatMap(NowPlayingIdle.init(rawValue:)) ?? .automatic
    }

    static func saved(in defaults: UserDefaults) -> NowPlayingIdle {
        resolve(raw: defaults.string(forKey: key))
    }
}

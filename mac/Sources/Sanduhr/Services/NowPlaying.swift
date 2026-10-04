import Foundation

// Now playing (item 53): the pure half. The adapter's stream lines, the merged track, the state
// machine that keeps a skip from blinking the wing, the stream's restart policy, the hide rules
// and the Music/Spotify fallback's mapping, all testable without a player. NowPlayingController
// runs the processes and feeds these. Titles and artists live only in memory: never on disk, in
// a log or in state.yaml.

/// What plays, as far as Sanduhr cares.
enum NowPlayingState: String, Equatable {
    case playing, paused, none
}

/// Where the track comes from (state.yaml `now_playing.source`, Settings' status line).
enum NowPlayingSource: String, Equatable {
    /// mediaremote-adapter's stream: every app that publishes now playing, browsers included.
    case adapter
    /// Music's and Spotify's change notifications (and, behind its switch, AppleScript to them).
    case fallback
    /// Switched off, Desk off, or the adapter's self-test still running.
    case off
}

/// One value in a stream payload. JSON null is how a diff removes a key.
enum NowPlayingValue: Equatable {
    case string(String)
    case number(Double)
    case bool(Bool)
    case null

    var string: String? { if case .string(let s) = self { return s }; return nil }
    var number: Double? {
        switch self {
        case .number(let n): return n
        case .string(let s): return Double(s)
        default: return nil
        }
    }
    var bool: Bool? {
        switch self {
        case .bool(let b): return b
        case .number(let n): return n != 0
        default: return nil
        }
    }

    init(_ any: Any) {
        switch any {
        case let n as NSNumber:
            self = CFGetTypeID(n) == CFBooleanGetTypeID() ? .bool(n.boolValue) : .number(n.doubleValue)
        case let s as String: self = .string(s)
        default: self = .null
        }
    }
}

/// The adapter's `stream` output: one JSON object per line,
/// `{"type":"data","diff":true,"payload":{...}}`. A full payload (`diff` false) replaces the
/// track; an empty one means nothing plays. A diff changes only the keys it names.
enum NowPlayingStream {
    struct Update: Equatable {
        var diff: Bool
        var payload: [String: NowPlayingValue]
    }

    /// The update on `line`, or nil for anything else: blank, malformed, another type, no payload.
    static func parse(_ line: String) -> Update? {
        let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let data = trimmed.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              object["type"] as? String == "data",
              let payload = object["payload"] as? [String: Any] else { return nil }
        let diff = (object["diff"] as? NSNumber)?.boolValue ?? false
        return Update(diff: diff, payload: payload.mapValues(NowPlayingValue.init))
    }

    /// Splits a chunk of output into complete lines; the unfinished tail stays in `buffer`.
    static func lines(appending chunk: Data, to buffer: inout Data) -> [String] {
        buffer.append(chunk)
        var out: [String] = []
        while let nl = buffer.firstIndex(of: 0x0A) {
            let line = buffer[buffer.startIndex..<nl]
            out.append(String(decoding: line, as: UTF8.self))
            buffer.removeSubrange(buffer.startIndex...nl)
        }
        // A runaway line without a newline (never seen) is dropped rather than kept growing.
        if buffer.count > 1 << 20 { buffer.removeAll() }
        return out
    }
}

/// The track, merged from the stream's full and diff payloads or made by the fallback.
struct NowPlayingInfo: Equatable {
    var title: String?
    var artist: String?
    var album: String?
    /// Seconds.
    var duration: Double?
    /// Seconds into the track at `elapsedAt`.
    var elapsed: Double?
    var elapsedAt: Date?
    var rate: Double?
    var playing: Bool?
    /// The app playing it (com.google.Chrome, com.apple.Music).
    var bundleID: String?
    var pid: Int?
    var itemID: String?

    var state: NowPlayingState {
        guard let title, !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return .none }
        if let playing { return playing ? .playing : .paused }
        return (rate ?? 0) > 0 ? .playing : .paused
    }

    /// Where playback is at `now`: the last elapsed time, moved on by the wall clock times the rate
    /// while playing, within 0 and the duration. nil without an elapsed time.
    func position(at now: Date) -> Double? {
        guard let elapsed else { return nil }
        var p = elapsed
        if state == .playing, let at = elapsedAt {
            let speed = (rate ?? 1) > 0 ? (rate ?? 1) : 1
            p += max(0, now.timeIntervalSince(at)) * speed
        }
        if let duration, duration > 0 { p = min(p, duration) }
        return max(0, p)
    }

    /// The position as a fraction of the duration, nil without both.
    func progress(at now: Date) -> Double? {
        guard let duration, duration > 0, let p = position(at: now) else { return nil }
        return min(1, max(0, p / duration))
    }

    /// Applies a stream update: a full payload starts from nothing, a diff keeps what it does not
    /// name, null removes. Elapsed time is anchored at the payload's timestamp, else at
    /// `receivedAt`; a play/pause change without a new elapsed time re-anchors where it was, so
    /// the position bar neither jumps back nor runs on while paused.
    mutating func apply(_ update: NowPlayingStream.Update, receivedAt: Date) {
        let wasPlaying = state == .playing
        let before = position(at: receivedAt)
        if !update.diff { self = NowPlayingInfo() }
        var elapsedChanged = false
        var stamp: Date?
        for (key, value) in update.payload {
            switch key {
            case "title": title = value.string
            case "artist": artist = value.string
            case "album": album = value.string
            case "duration": duration = value.number
            case "durationMicros": duration = value.number.map { $0 / 1_000_000 }
            case "elapsedTime": elapsed = value.number; elapsedChanged = true
            case "elapsedTimeMicros": elapsed = value.number.map { $0 / 1_000_000 }; elapsedChanged = true
            case "timestamp": stamp = value.string.flatMap(Self.isoDate)
            case "timestampEpochMicros": stamp = value.number.map { Date(timeIntervalSince1970: $0 / 1_000_000) }
            case "playbackRate": rate = value.number
            case "playing": playing = value.bool
            case "bundleIdentifier": bundleID = value.string
            case "processIdentifier": pid = value.number.map { Int($0) }
            case "contentItemIdentifier": itemID = value.string
            default: break
            }
        }
        if let stamp {
            elapsedAt = stamp
        } else if elapsedChanged {
            elapsedAt = receivedAt
        } else if update.diff, wasPlaying != (state == .playing), let before {
            elapsed = before
            elapsedAt = receivedAt
        }
    }

    private static func isoDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.date(from: s)
    }
}

/// The track Sanduhr shows. Players clear now playing for a moment between tracks (an empty
/// payload, then the next song), so a change to nothing only counts after `clearDelay` without
/// a track coming back; a track shows at once. Pure: the controller schedules the `settle`.
struct NowPlayingTracker: Equatable {
    static let clearDelay: TimeInterval = 1.5

    /// The merged stream state, as of the last update.
    private(set) var latest = NowPlayingInfo()
    /// What shows: nil when nothing plays (after the delay).
    private(set) var shown: NowPlayingInfo?
    private(set) var clearingSince: Date?

    var state: NowPlayingState { shown?.state ?? .none }

    /// A stream update; returns how long to wait before `settle`, nil when nothing is pending.
    @discardableResult
    mutating func receive(_ update: NowPlayingStream.Update, now: Date) -> TimeInterval? {
        latest.apply(update, receivedAt: now)
        return settle(now: now)
    }

    /// A whole track from the fallback (or nothing).
    @discardableResult
    mutating func replace(_ info: NowPlayingInfo, now: Date) -> TimeInterval? {
        latest = info
        return settle(now: now)
    }

    @discardableResult
    mutating func settle(now: Date) -> TimeInterval? {
        if latest.state != .none {
            shown = latest
            clearingSince = nil
            return nil
        }
        guard shown != nil else { return nil }
        let since = clearingSince ?? now
        clearingSince = since
        let left = Self.clearDelay - now.timeIntervalSince(since)
        if left <= 0 {
            shown = nil
            clearingSince = nil
            return nil
        }
        return left
    }

    mutating func reset() { self = NowPlayingTracker() }
}

/// What to do when the adapter's stream process exits on its own: restart with a growing wait,
/// and after several quick exits in a row give up on it for the fallback. A run that lasted a
/// while (a wake, a Perl hiccup) starts the count again.
struct NowPlayingSupervisor: Equatable {
    static let maxQuickExits = 4
    static let healthyRun: TimeInterval = 60
    static let maxBackoff: TimeInterval = 30

    enum Next: Equatable {
        case restart(after: TimeInterval)
        case fallback
    }

    private(set) var quickExits = 0

    mutating func streamExited(ranFor: TimeInterval) -> Next {
        if ranFor >= Self.healthyRun { quickExits = 0 }
        quickExits += 1
        if quickExits > Self.maxQuickExits { return .fallback }
        return .restart(after: Self.backoff(quickExits))
    }

    /// 1, 2, 4, 8 … seconds, at most `maxBackoff`.
    static func backoff(_ attempt: Int) -> TimeInterval {
        min(maxBackoff, pow(2, Double(max(0, attempt - 1))))
    }

    mutating func reset() { quickExits = 0 }
}

/// The Now Playing settings that decide whether a track shows (UserDefaults.desk). Where it shows
/// is NowPlayingPlacement's: the notch choices and the Desk layout.
struct NowPlayingPrefs: Equatable {
    /// Item 53's switch and Desk line. Read only by NowPlayingPlacement.upgrade, once; ignored after.
    static let enabledKey = "nowPlaying"
    static let deskLineKey = "nowPlayingDesk"
    static let hidePausedKey = "nowPlayingHidePaused"
    static let excludedKey = "nowPlayingExcluded"
    static let askAppsKey = "nowPlayingAskApps"

    var hideWhilePaused = false
    /// Bundle ids whose playback never shows.
    var excluded: Set<String> = []
    /// AppleScript to Music and Spotify while on the fallback (raises the Automation prompt).
    var askApps = false

    static func saved(in defaults: UserDefaults) -> NowPlayingPrefs {
        NowPlayingPrefs(
            hideWhilePaused: defaults.bool(forKey: hidePausedKey),
            excluded: Set(defaults.stringArray(forKey: excludedKey) ?? []),
            askApps: defaults.bool(forKey: askAppsKey))
    }

    /// The track to draw, or nil: nothing plays, its app is excluded, or it is paused and paused
    /// tracks hide.
    func visible(_ info: NowPlayingInfo?) -> NowPlayingInfo? {
        guard let info else { return nil }
        switch info.state {
        case .none: return nil
        case .paused where hideWhilePaused: return nil
        default: break
        }
        if let app = info.bundleID, excluded.contains(app) { return nil }
        return info
    }
}

/// The apps seen playing this session, for Settings' app list: first seen first, no repeats.
/// Kept in memory only, plus whatever is excluded (so an exclusion can be undone).
enum NowPlayingApps {
    static func seen(_ list: [String], adding app: String?) -> [String] {
        guard let app, !app.isEmpty, !list.contains(app) else { return list }
        return list + [app]
    }

    /// The rows Settings lists: the apps seen, then excluded ones not seen this session.
    static func rows(seen: [String], excluded: Set<String>) -> [String] {
        seen + excluded.subtracting(seen).sorted()
    }
}

/// "Title · Artist" with a play-state glyph. Whole everywhere: a wing or the strip that is too
/// short scrolls it through once (NowPlayingScroll), and the Desk line truncates to its width.
enum NowPlayingText {
    static let playingGlyph = "▶\u{FE0E}"
    static let pausedGlyph = "\u{23F8}\u{FE0E}"

    /// The line for a notch place, nil when there is no track.
    static func line(_ info: NowPlayingInfo?, at place: NotchContent.Place) -> String? {
        guard let info, info.state != .none, let title = clean(info.title) else { return nil }
        let glyph = info.state == .playing ? playingGlyph : pausedGlyph
        guard let artist = clean(info.artist) else { return "\(glyph) \(title)" }
        return "\(glyph) \(title) · \(artist)"
    }

    /// A line split into its play-state glyph and the rest, so a wing can keep the glyph still
    /// while the title scrolls. A line without a leading glyph comes back whole.
    static func splitGlyph(_ line: String) -> (glyph: String?, rest: String) {
        for g in [playingGlyph, pausedGlyph] where line.hasPrefix(g + " ") {
            return (g, String(line.dropFirst(g.count + 1)))
        }
        return (nil, line)
    }

    /// The Desk line: the same text (the view truncates to its width).
    static func desk(_ info: NowPlayingInfo?) -> String? {
        line(info, at: .strip)
    }

    private static func clean(_ s: String?) -> String? {
        guard let s = s?.trimmingCharacters(in: .whitespacesAndNewlines), !s.isEmpty else { return nil }
        return s
    }
}

/// The fallback: Music and Spotify post a distributed notification on every change, no prompt.
/// Push only (nothing until something changes), those two apps only, no browsers.
enum NowPlayingFallback {
    static let music = "com.apple.Music"
    static let spotify = "com.spotify.client"
    static let notifications: [(name: String, app: String)] = [
        ("com.apple.Music.playerInfo", music),
        ("com.spotify.client.PlaybackStateChanged", spotify),
    ]

    /// The track a notification describes; a stopped player is no track.
    static func info(from userInfo: [AnyHashable: Any], app: String, now: Date) -> NowPlayingInfo {
        let state = (userInfo["Player State"] as? String)?.lowercased() ?? ""
        guard state == "playing" || state == "paused" else { return NowPlayingInfo() }
        var info = NowPlayingInfo()
        info.title = userInfo["Name"] as? String
        info.artist = userInfo["Artist"] as? String
        info.album = userInfo["Album"] as? String
        // Music: Total Time in ms. Spotify: Duration in ms.
        let ms = (userInfo["Total Time"] as? NSNumber) ?? (userInfo["Duration"] as? NSNumber)
        info.duration = ms.map { $0.doubleValue / 1000 }
        if let position = userInfo["Playback Position"] as? NSNumber ?? userInfo["Player Position"] as? NSNumber {
            info.elapsed = position.doubleValue
            info.elapsedAt = now
        }
        info.playing = state == "playing"
        info.rate = state == "playing" ? 1 : 0
        info.bundleID = app
        return info
    }

    /// The AppleScript that asks a running app (never launches it) for its track: lines of
    /// state, name, artist, album, duration, position. Spotify's duration is in milliseconds.
    static func script(for app: String) -> String {
        let duration = app == spotify ? "((duration of t) / 1000)" : "(duration of t)"
        return """
        if application id "\(app)" is running then
          tell application id "\(app)"
            set s to (player state as text)
            if s is "stopped" then return "stopped"
            set t to current track
            return s & linefeed & (name of t) & linefeed & (artist of t) & linefeed & (album of t) & linefeed & (\(duration) as text) & linefeed & (player position as text)
          end tell
        end if
        return ""
        """
    }

    /// The script's answer as a track; nothing for a stopped or quiet app or an unreadable
    /// answer. Numbers may use a decimal comma (the user's locale).
    static func info(fromScript output: String, app: String, now: Date) -> NowPlayingInfo {
        let lines = output.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
        guard lines.count >= 6 else { return NowPlayingInfo() }
        let state = lines[0].trimmingCharacters(in: .whitespaces).lowercased()
        guard state == "playing" || state == "paused" else { return NowPlayingInfo() }
        func number(_ s: String) -> Double? {
            Double(s.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: "."))
        }
        var info = NowPlayingInfo()
        info.title = lines[1]
        info.artist = lines[2]
        info.album = lines[3]
        info.duration = number(lines[4])
        info.elapsed = number(lines[5])
        info.elapsedAt = info.elapsed == nil ? nil : now
        info.playing = state == "playing"
        info.rate = state == "playing" ? 1 : 0
        info.bundleID = app
        return info
    }
}

/// Settings' source line and state.yaml's `source`, from what the controller knows.
enum NowPlayingStatus {
    static let notPlaced = "Not placed anywhere"

    static func text(source: NowPlayingSource, placed: Bool, deskRunning: Bool, checking: Bool,
                     adapterFailed: Bool) -> String {
        if !placed { return notPlaced }
        if !deskRunning { return "Off (needs Desk)" }
        if checking { return "Checking…" }
        switch source {
        case .adapter: return "Adapter working"
        case .fallback:
            return adapterFailed
                ? "Fallback (Music and Spotify only): the system now playing isn't available on this macOS"
                : "Fallback (Music and Spotify only)"
        case .off: return "Off"
        }
    }
}

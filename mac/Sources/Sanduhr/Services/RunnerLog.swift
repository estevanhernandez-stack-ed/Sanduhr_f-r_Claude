import Foundation

/// What one line of a GitHub Actions runner's console says, when it says anything Sanduhr uses.
/// `at` is the runner's own stamp (whole seconds, UTC).
enum RunnerLine: Equatable, Sendable {
    /// "Running job: <name>".
    case jobStarted(job: String, at: Date)
    /// "Job <name> completed with result: <word>".
    case jobEnded(job: String, result: WatcherState, at: Date)
    /// "Listening for Jobs": the runner (re)started and holds no job.
    case online(at: Date)

    var at: Date {
        switch self {
        case let .jobStarted(_, at), let .jobEnded(_, _, at), let .online(at): at
        }
    }
}

/// A runner container's log line as Docker hands it over with timestamps on (Windows' RunnerLog):
/// `<Docker RFC 3339 stamp> <runner stamp "yyyy-MM-dd HH:mm:ssZ">: <text>`. Pure. Every other line
/// (reconnect noise, the version banner, "Runner reconnected.", which can come mid-job) is nothing.
enum RunnerLog {
    /// Longer lines are not the runner's own and are ignored.
    static let maxLine = 4096

    private static let linePattern = try! NSRegularExpression(
        pattern: #"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z (\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})Z: (.*)$"#)
    private static let startPattern = try! NSRegularExpression(pattern: #"^Running job: (.+)$"#)
    private static let endPattern = try! NSRegularExpression(pattern: #"^Job (.+) completed with result: ([A-Za-z]+)$"#)

    static func parse(_ line: String) -> RunnerLine? {
        guard !line.isEmpty, line.utf16.count <= maxLine else { return nil }
        var scalars = Substring(line).unicodeScalars
        while let last = scalars.last, last == "\r" || last == "\n" { scalars.removeLast() }
        let trimmed = String(scalars)
        guard let m = linePattern.firstMatch(in: trimmed, range: NSRange(trimmed.startIndex..., in: trimmed)),
              let at = stamp(m, in: trimmed), let text = group(m, 7, in: trimmed) else { return nil }
        if let s = match(startPattern, text) {
            return WatcherLimits.line(s[0], cap: WatcherLimits.title).map { .jobStarted(job: $0, at: at) }
        }
        if let e = match(endPattern, text) {
            return WatcherLimits.line(e[0], cap: WatcherLimits.title).map { .jobEnded(job: $0, result: result(e[1]), at: at) }
        }
        return text == "Listening for Jobs" ? .online(at: at) : nil
    }

    /// The runner's result word as a watcher state: passed, failed, or finished (ended, nothing to
    /// celebrate or mourn, including any word this build does not know).
    static func result(_ word: String) -> WatcherState {
        switch word {
        case "Succeeded", "SucceededWithIssues": .passed
        case "Failed", "Abandoned": .failed
        default: .finished
        }
    }

    private static let utc: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }()

    /// The runner stamp, nil for an impossible date or time.
    private static func stamp(_ m: NSTextCheckingResult, in s: String) -> Date? {
        let v = (1...6).compactMap { group(m, $0, in: s).flatMap { Int($0) } }
        guard v.count == 6 else { return nil }
        let parts = DateComponents(calendar: utc, timeZone: utc.timeZone, year: v[0], month: v[1], day: v[2],
                                   hour: v[3], minute: v[4], second: v[5])
        guard v[0] >= 1, (0..<24).contains(v[3]), (0..<60).contains(v[4]), (0..<60).contains(v[5]),
              let date = utc.date(from: parts) else { return nil }
        let back = utc.dateComponents([.year, .month, .day], from: date)
        return back.year == v[0] && back.month == v[1] && back.day == v[2] ? date : nil
    }

    private static func group(_ m: NSTextCheckingResult, _ i: Int, in s: String) -> String? {
        Range(m.range(at: i), in: s).map { String(s[$0]) }
    }

    private static func match(_ re: NSRegularExpression, _ s: String) -> [String]? {
        guard let m = re.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) else { return nil }
        return (1..<m.numberOfRanges).compactMap { group(m, $0, in: s) }
    }
}

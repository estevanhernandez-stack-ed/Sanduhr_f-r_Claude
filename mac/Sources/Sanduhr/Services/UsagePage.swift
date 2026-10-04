import Foundation

/// The Claude Usage page's tabs (item 48). The raw values are state.yaml's `usage_page.tab` and
/// the `usage` debug action's argument.
enum UsageTab: String, CaseIterable, Identifiable, Sendable {
    case overview, trends, sessions

    var id: String { rawValue }

    var title: String {
        switch self {
        case .overview: "Overview"
        case .trends: "Trends"
        case .sessions: "Sessions"
        }
    }
}

/// What the page can show for an account, from its data choices: nothing until a folder is
/// linked and activity tracked; the live reader alone with Live only; the record (Trends and
/// Sessions too) with Keep a record.
enum UsageSource: Equatable, Sendable {
    case notTracked
    case noFolder
    case live(folder: String)
    case record(folder: String, id: String)

    static func of(_ c: AccountDataChoices) -> UsageSource {
        guard c.activity != .off else { return .notTracked }
        guard let folder = c.folder, !folder.isEmpty else { return .noFolder }
        return c.activity == .record ? .record(folder: folder, id: VaultFolderID.of(folder)) : .live(folder: folder)
    }

    var folder: String? {
        switch self {
        case .live(let f), .record(let f, _): f
        default: nil
        }
    }

    var recordID: String? {
        if case .record(_, let id) = self { return id }
        return nil
    }
}

/// A name and its tokens, largest first in every list the page shows.
struct UsageRanked: Equatable, Sendable, Identifiable {
    let name: String
    let total: Int64
    var id: String { name }

    static func ranked(_ totals: [String: Int64], top: Int) -> [UsageRanked] {
        VaultReader.ranked(totals, top: top).map { UsageRanked(name: $0.name, total: $0.total) }
    }
}

/// Project names as the page shows live days, under the account's choice: the folded project
/// name (worktrees and subfolders fold into the repo) for Names and Full paths, its `p-` code for
/// Hidden, `(none)` without a cwd, exactly as the record would store it. A code is never turned
/// back into a name: the page has no way to, and no code path tries.
enum UsageProjectNames {
    static let none = "(none)"

    static func name(_ cwd: String, choice: ProjectNamesChoice,
                     displayName: (String) -> String = CCProjectName.displayName) -> String {
        guard !cwd.isEmpty else { return none }
        let name = displayName(cwd)
        return choice == .hidden ? VaultHiddenName.of(name) : name
    }

    /// Whether any of these names is a Hidden code, for the caption explaining them.
    static func anyHidden(_ names: [String]) -> Bool {
        names.contains(where: VaultHiddenName.isHidden)
    }

    static let hiddenCaption = "Codes like p-3f2a91cc0d stand for projects whose names this account keeps hidden. Sanduhr keeps only the code, so it can't show the name."
}

// MARK: Overview

/// The record's part of the Overview, read off the main thread: when it last finished a pass,
/// its closed days (`[windowStart, hotStart)` only) and the days it covers.
struct UsageRecordFacts: Equatable, Sendable {
    var lastIngest: Date?
    var window = VaultWindow()
    var covered: Set<CCLocalDay> = []
}

/// The Overview (Windows `LocalCcViewModel`): today from the live reader, closed days from the
/// record, a 30-day strip, top projects and skills.
///
/// **Hot-day rule.** A day is served by the record only when the record's last finished pass
/// came after that day ended: days before the local date of the last pass come from the record,
/// that day and later from the live reader, never both for one day. With a fresh pass that is
/// just today; just after midnight it is yesterday too, until the next pass lands.
///
/// **Degraded mode.** A record whose last pass is older than three refresh cycles (15 minutes),
/// or that never finished one, is not trusted for any day: the whole window comes from the live
/// reader with a status line saying so. Live only always reads live, with its own line.
struct UsageOverview: Equatable, Sendable {
    enum Mode: Equatable, Sendable {
        /// Live only: Claude Code's logs, no record.
        case live
        /// The record serves closed days.
        case record
        /// Degraded: the record's last pass is too old.
        case paused
        /// Degraded: the record has never finished a pass.
        case starting
    }

    struct Day: Equatable, Sendable, Identifiable {
        let day: CCLocalDay
        let tokens: Int64
        /// No record of this day (before the record began, or a gap), drawn as "no record",
        /// never as zero.
        let noRecord: Bool
        /// Served by the live reader (a hot day, or every day without a trusted record).
        let live: Bool
        var id: CCLocalDay { day }
    }

    static let windowDays = 30
    static let degradedAfter: TimeInterval = 15 * 60
    static let topCount = 10

    var mode: Mode = .live
    /// Oldest first, today last.
    var strip: [Day] = []
    var today: Int64 = 0
    var todayInput: Int64 = 0
    var todayOutput: Int64 = 0
    var windowTotal: Int64 = 0
    var projects: [UsageRanked] = []
    var skills: [UsageRanked] = []

    var status: String? {
        switch mode {
        case .record: nil
        case .live: "Live only: Claude Code's own logs, which it deletes after about 30 days. Nothing is kept."
        case .paused: "Record paused: showing Claude Code's live logs only until the next pass."
        case .starting: "Record starting: showing Claude Code's live logs until its first pass finishes."
        }
    }

    static func windowStart(_ today: CCLocalDay) -> CCLocalDay { today.adding(days: -(windowDays - 1)) }

    static func isDegraded(lastIngest: Date?, now: Date) -> Bool {
        guard let lastIngest else { return true }
        return now.timeIntervalSince(lastIngest) > degradedAfter
    }

    /// The first day the live reader serves: the local date of the last finished pass, never
    /// after today (a clock moved back) and never before the window.
    static func hotStart(lastIngest: Date, today: CCLocalDay, calendar: Calendar) -> CCLocalDay {
        let d = CCLocalDay(lastIngest, calendar: calendar)
        return max(windowStart(today), min(d, today))
    }

    /// The record's closed days to read for this pass time: `[windowStart, hotStart)`, nil when
    /// the record isn't trusted (degraded) or has no closed day in the window.
    static func closedRange(lastIngest: Date?, today: CCLocalDay, now: Date,
                            calendar: Calendar) -> (from: CCLocalDay, toExclusive: CCLocalDay)? {
        guard !isDegraded(lastIngest: lastIngest, now: now), let lastIngest else { return nil }
        let hot = hotStart(lastIngest: lastIngest, today: today, calendar: calendar)
        let start = windowStart(today)
        return hot > start ? (start, hot) : nil
    }

    /// The Overview from the live days and, with Keep a record, the record's facts (nil for
    /// Live only). `name` turns a live cwd into what the page shows (`UsageProjectNames`).
    static func build(today: CCLocalDay, now: Date, calendar: Calendar,
                      live: [CCLocalDay: CCLiveDay], record: UsageRecordFacts?,
                      name: (String) -> String) -> UsageOverview {
        var o = UsageOverview()
        let start = windowStart(today)
        var hot = start
        if let record {
            if isDegraded(lastIngest: record.lastIngest, now: now) {
                o.mode = record.lastIngest == nil ? .starting : .paused
            } else if let last = record.lastIngest {
                o.mode = .record
                hot = hotStart(lastIngest: last, today: today, calendar: calendar)
            }
        }
        var projects: [String: Int64] = [:]
        var skills: [String: Int64] = [:]
        if o.mode == .record, let record {
            for (n, v) in record.window.byProjectName { projects[n, default: 0] += v }
            for (s, v) in record.window.bySkill { skills[s, default: 0] += v }
        }
        var d = start
        while d <= today {
            if d < hot, let record {
                let tokens = record.window.byDay[d] ?? 0
                o.strip.append(Day(day: d, tokens: tokens,
                                   noRecord: tokens == 0 && !record.covered.contains(d), live: false))
            } else {
                let day = live[d] ?? CCLiveDay()
                o.strip.append(Day(day: d, tokens: day.total, noRecord: false, live: true))
                for (cwd, v) in day.byCwd { projects[name(cwd), default: 0] += v }
                for (s, v) in day.bySkill { skills[s, default: 0] += v }
            }
            d = d.adding(days: 1)
        }
        let todayLive = live[today] ?? CCLiveDay()
        o.today = todayLive.total
        o.todayInput = todayLive.input
        o.todayOutput = todayLive.output
        o.windowTotal = o.strip.reduce(0) { $0 + $1.tokens }
        o.projects = UsageRanked.ranked(projects, top: topCount)
        o.skills = UsageRanked.ranked(skills, top: topCount)
        return o
    }
}

// MARK: Trends

/// Trends (Windows `CcTrendsViewModel`, `CcTrendsControl`): weekly totals over 4, 12 or 26
/// weeks and the top projects of that span, from the record only. A week with no tokens and a
/// day the record doesn't cover (before it began, or a gap) is "no record", never a zero bar;
/// a covered week with nothing in it is a real zero. The current week is drawn distinct.
struct UsageTrends: Equatable, Sendable {
    enum Kind: Equatable, Sendable {
        case bar
        /// Covered and empty: a hairline at the baseline.
        case zero
        /// Nothing recorded and some day not covered: the "no record" texture.
        case noRecord
    }

    struct Bar: Equatable, Sendable, Identifiable {
        let weekStart: CCLocalDay
        let total: Int64
        let kind: Kind
        let isCurrent: Bool
        /// Tokens, but some day of the week isn't covered: the bar gets a "no record" edge.
        let partialGap: Bool
        var id: CCLocalDay { weekStart }
    }

    static let ranges = [4, 12, 26]
    static let defaultWeeks = 12
    static let topCount = 5

    var bars: [Bar] = []
    var top: [UsageRanked] = []
    var birth: CCLocalDay?
    var today: CCLocalDay?

    var maxTotal: Int64 { bars.map(\.total).max() ?? 0 }

    /// "History kept since July 12, 2026".
    var footer: String? { birth.map { "History kept since \(UsageDates.long($0))" } }

    /// The first day or two: the backfill seeded about four weeks.
    var freshNote: String? {
        guard let birth, let today, today.epochDay - birth.epochDay <= 1 else { return nil }
        return "A new record: its first pass kept about four weeks from Claude Code's logs. Longer trends fill in from here."
    }

    static func build(weeks: [VaultWeek], top: [(name: String, total: Int64)], birth: CCLocalDay?,
                      today: CCLocalDay) -> UsageTrends {
        var t = UsageTrends()
        t.birth = birth
        t.today = today
        t.bars = weeks.map { w in
            let kind: Kind = w.total > 0 ? .bar : (w.hasNoRecordGap ? .noRecord : .zero)
            return Bar(weekStart: w.weekStart, total: w.total, kind: kind, isCurrent: w.isCurrent,
                       partialGap: w.total > 0 && w.hasNoRecordGap)
        }
        t.top = top.map { UsageRanked(name: $0.name, total: $0.total) }
        return t
    }
}

// MARK: Sessions

/// The ledger's date scope (Windows `CcLedgerViewModel`): the token column and its sort count
/// only the days in scope, from each session's `by_day`, so "what ate 800k yesterday" ranks
/// yesterday's sessions, not last week's long ones.
enum LedgerScope: String, CaseIterable, Identifiable, Sendable {
    case today, yesterday, week, all

    var id: String { rawValue }
    static let `default` = LedgerScope.week

    var title: String {
        switch self {
        case .today: "Today"
        case .yesterday: "Yesterday"
        case .week: "7d"
        case .all: "All"
        }
    }

    /// Inclusive day range.
    func range(today: CCLocalDay) -> (from: CCLocalDay, through: CCLocalDay) {
        switch self {
        case .today: (today, today)
        case .yesterday: (today.adding(days: -1), today.adding(days: -1))
        case .week: (today.adding(days: -6), today)
        case .all: (CCLocalDay(year: 1, month: 1, day: 1), CCLocalDay(year: 9999, month: 12, day: 31))
        }
    }
}

enum LedgerColumn: String, CaseIterable, Sendable {
    case lastActive, project, tokens

    /// Text sorts ascending first, numbers and dates descending (Windows).
    var startsDescending: Bool { self != .project }
}

/// One ledger row: one logical session, its scoped tokens and its display texts.
struct LedgerRow: Identifiable, Equatable, Sendable {
    /// `root|uuid`: stable across reloads, so a reload keeps the list's scroll and expansion.
    let id: String
    let session: VaultSessionInfo
    let projectText: String
    let scoped: Int64
    let badge: String
}

/// The ledger's pure logic (Windows `LedgerRowViewModel`, `CcLedgerViewModel`, `LedgerSort`):
/// rows, scope, sort, the expansion detail and the CSV export through `VaultLedgerCsv`.
enum UsageLedger {
    /// Display names shared by different project keys, shown with the key's last 8 characters.
    static func ambiguousNames(_ sessions: [VaultSessionInfo]) -> Set<String> {
        var keys: [String: Set<String>] = [:]
        for s in sessions { keys[s.projectName, default: []].insert(s.projectKey) }
        return Set(keys.filter { $0.value.count > 1 }.keys)
    }

    static func projectText(_ s: VaultSessionInfo, ambiguous: Set<String>) -> String {
        guard ambiguous.contains(s.projectName) else { return s.projectName }
        return "\(s.projectName) ~\(s.projectKey.suffix(8))"
    }

    static func rows(_ sessions: [VaultSessionInfo], scope: LedgerScope, today: CCLocalDay) -> [LedgerRow] {
        let ambiguous = ambiguousNames(sessions)
        let r = scope.range(today: today)
        return sessions.map { s in
            LedgerRow(id: "\(s.root)|\(s.uuid)", session: s, projectText: projectText(s, ambiguous: ambiguous),
                      scoped: VaultReader.tokensInScope(s, from: r.from, through: r.through),
                      badge: badge(s.byModel))
        }
    }

    /// The rows with their scoped tokens recomputed for another scope (a chip click: no reread).
    static func rescoped(_ rows: [LedgerRow], scope: LedgerScope, today: CCLocalDay) -> [LedgerRow] {
        let r = scope.range(today: today)
        return rows.map {
            LedgerRow(id: $0.id, session: $0.session, projectText: $0.projectText,
                      scoped: VaultReader.tokensInScope($0.session, from: r.from, through: r.through),
                      badge: $0.badge)
        }
    }

    /// Sorted by the column, ties by last active, both flipped when descending (Windows), then
    /// by id so equal rows keep one order.
    static func sorted(_ rows: [LedgerRow], by column: LedgerColumn, descending: Bool) -> [LedgerRow] {
        rows.sorted { a, b in
            var order = compare(a, b, column)
            if order == .orderedSame { order = compareDates(a.session.lastTs, b.session.lastTs) }
            if order != .orderedSame { return descending ? order == .orderedDescending : order == .orderedAscending }
            return a.id < b.id
        }
    }

    private static func compare(_ a: LedgerRow, _ b: LedgerRow, _ column: LedgerColumn) -> ComparisonResult {
        switch column {
        case .lastActive: compareDates(a.session.lastTs, b.session.lastTs)
        case .project: a.projectText.caseInsensitiveCompare(b.projectText)
        case .tokens: a.scoped == b.scoped ? .orderedSame : (a.scoped < b.scoped ? .orderedAscending : .orderedDescending)
        }
    }

    private static func compareDates(_ a: Date, _ b: Date) -> ComparisonResult {
        a == b ? .orderedSame : (a < b ? .orderedAscending : .orderedDescending)
    }

    /// The column header with its direction glyph when it sorts (direction by glyph, not color).
    static func header(_ column: LedgerColumn, sortedBy: LedgerColumn, descending: Bool,
                       scope: LedgerScope) -> String {
        let label: String
        switch column {
        case .lastActive: label = "Last active"
        case .project: label = "Project"
        case .tokens: label = scope == .all ? "Tokens" : "Tokens (\(scope.title))"
        }
        guard column == sortedBy else { return label }
        return "\(label) \(descending ? "▼" : "▲")"
    }

    /// The scoped tokens as the column shows them: a dash outside the scope.
    static func tokensText(_ n: Int64) -> String { n > 0 ? TokenFormat.compact(n) : "—" }

    // MARK: Texts

    /// The two largest models with their share, tier names for mapped models and the trimmed
    /// raw name otherwise, so an unmapped model never disappears.
    static func badge(_ byModel: [String: Int64]) -> String {
        let total = byModel.values.reduce(0, +)
        guard total > 0 else { return "" }
        return VaultReader.ranked(byModel, top: 2)
            .map { "\(shortModel($0.name)) \($0.total * 100 / total)%" }
            .joined(separator: " · ")
    }

    static func shortModel(_ model: String) -> String {
        switch CCLogReader.tierForModel(model) {
        case Tier.sevenDayOpus.rawValue: return "opus"
        case Tier.sevenDaySonnet.rawValue: return "sonnet"
        case Tier.sevenDay.rawValue: return "haiku"
        default: break
        }
        var name = model.hasPrefix("claude-") ? String(model.dropFirst(7)) : model
        // A trailing -yyyymmdd build stamp.
        if name.count > 9 {
            let stamp = name.suffix(9)
            if stamp.first == "-", stamp.dropFirst().allSatisfy(\.isASCII), stamp.dropFirst().allSatisfy(\.isNumber) {
                name = String(name.dropLast(9))
            }
        }
        return name
    }

    /// "just now", "12m ago", "3h ago", "5d ago", then "Jul 2".
    static func relative(_ ts: Date, now: Date, calendar: Calendar = .current) -> String {
        let delta = now.timeIntervalSince(ts)
        if delta < 120 { return "just now" }
        if delta < 3600 { return "\(Int(delta / 60))m ago" }
        if delta < 86_400 { return "\(Int(delta / 3600))h ago" }
        if delta < 14 * 86_400 { return "\(Int(delta / 86_400))d ago" }
        return UsageDates.short(CCLocalDay(ts, calendar: calendar))
    }

    /// The expansion: the wall-clock span (labeled as such: no made-up active time), lifetime
    /// tokens, agents, each day with its models, the skill split, the folder path when the
    /// record kept it (Full paths), the record and the session id.
    static func detail(_ s: VaultSessionInfo, recordTitle: String) -> [String] {
        var lines: [String] = []
        let span = max(0, s.lastTs.timeIntervalSince(s.firstTs))
        let spanText = span >= 3600
            ? "\(Int(span / 3600))h \(Int(span.truncatingRemainder(dividingBy: 3600) / 60))m"
            : "\(max(1, Int(span / 60)))m"
        lines.append("Span (wall clock): \(spanText)  ·  Lifetime: \(TokenFormat.compact(s.total)) tokens")
        if s.agentCount > 0 {
            lines.append("Agents: \(s.agentCount) · \(TokenFormat.compact(s.agentTokens)) tokens")
        }
        for key in s.byDay.keys.sorted() {
            guard let bucket = s.byDay[key] else { continue }
            var line = "\(key): \(TokenFormat.compact(bucket.total))"
            let models = VaultReader.ranked(bucket.byModel, top: bucket.byModel.count)
                .map { "\(shortModel($0.name)) \(TokenFormat.compact($0.total))" }
            if !models.isEmpty { line += "  (\(models.joined(separator: ", ")))" }
            lines.append(line)
        }
        if let skills = s.bySkill, !skills.isEmpty {
            let parts = VaultReader.ranked(skills, top: skills.count)
                .map { "\($0.name) \(TokenFormat.compact($0.total))" }
            lines.append("Skills: \(parts.joined(separator: ", "))")
        }
        if let cwd = s.cwd, !cwd.isEmpty { lines.append("Folder: \(cwd)") }
        lines.append("Record: \(recordTitle)  ·  Session: \(s.uuid)")
        return lines
    }

    // MARK: CSV

    /// The export (Windows `BuildCsvRows`): session rows in the order the list shows them, every
    /// session (not only those in scope), with the scoped and lifetime tokens; `root` is the
    /// record's title and `project` the shown project text.
    static func csv(_ sortedRows: [LedgerRow], recordTitle: String) -> (text: String, rowCount: Int) {
        VaultLedgerCsv.build(sortedRows.map {
            VaultLedgerCsv.row($0.session, root: recordTitle, project: $0.projectText, tokensInScope: $0.scoped)
        })
    }

    /// `sanduhr-sessions-20261004.csv`.
    static func exportName(_ today: CCLocalDay) -> String {
        String(format: "sanduhr-sessions-%04d%02d%02d.csv", today.year, today.month, today.day)
    }
}

// MARK: Dates

enum UsageDates {
    static let months = ["January", "February", "March", "April", "May", "June", "July", "August",
                         "September", "October", "November", "December"]

    /// "July 12, 2026".
    static func long(_ d: CCLocalDay) -> String {
        guard (1...12).contains(d.month) else { return d.key }
        return "\(months[d.month - 1]) \(d.day), \(d.year)"
    }

    /// "Jul 12".
    static func short(_ d: CCLocalDay) -> String {
        guard (1...12).contains(d.month) else { return d.key }
        return "\(months[d.month - 1].prefix(3)) \(d.day)"
    }
}

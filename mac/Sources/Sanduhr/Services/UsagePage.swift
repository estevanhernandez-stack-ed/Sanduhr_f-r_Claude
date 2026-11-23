import Foundation

/// The Claude Usage page's tabs (item 48). The raw values are state.yaml's `usage_page.tab` and
/// the `usage` debug action's argument.
enum UsageTab: String, CaseIterable, Identifiable, Sendable {
    case overview, trends, sessions, meters

    var id: String { rawValue }

    var title: String {
        switch self {
        case .overview: "Overview"
        case .trends: "Trends"
        case .sessions: "Sessions"
        case .meters: "Meters"
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
    /// Each agent's share, only for a project an agent other than Claude Code touched (Claude's
    /// part included); empty otherwise, so Claude Code's own rows read as they always did.
    var byAgent: [AgentSource: Int64] = [:]
    var id: String { name }

    /// "13.0M", "13.0M (Codex)" or "1.2M (Claude Code 900k, Codex 300k)".
    var valueText: String { byAgent.isEmpty ? TokenFormat.compact(total) : AgentShares.describe(total, byAgent) }

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
        /// No Claude Code folder is read; only an agent's rows show.
        case agentsOnly
    }

    struct Day: Equatable, Sendable, Identifiable {
        let day: CCLocalDay
        let tokens: Int64
        /// No record of this day (before the record began, or a gap), drawn as "no record",
        /// never as zero.
        let noRecord: Bool
        /// Served by the live reader (a hot day, or every day without a trusted record).
        let live: Bool
        /// The day's tokens from the agents other than Claude Code (`tokens` is Claude Code's).
        var agents: [AgentSource: Int64] = [:]
        var id: CCLocalDay { day }

        /// Every agent's tokens for the day: the bar's height.
        var total: Int64 { tokens + agents.values.reduce(0, +) }

        /// The stack, bottom up: Claude Code, then each agent with tokens, in agent order.
        var segments: [(agent: AgentSource, tokens: Int64)] {
            var out: [(agent: AgentSource, tokens: Int64)] = []
            if tokens > 0 { out.append((.claude, tokens)) }
            for a in AgentSource.burnRoots { if let t = agents[a], t > 0 { out.append((a, t)) } }
            return out
        }
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
    /// The 30 days' tokens per agent with any, Claude Code included; empty with Claude Code alone.
    var agentTotals: [AgentSource: Int64] = [:]

    /// Whether some agent other than Claude Code has tokens in the window: only then does the
    /// strip stack by agent.
    var stacked: Bool { !agentTotals.isEmpty }

    /// The strip's accessible name: unchanged with Claude Code alone, each agent's 30-day total
    /// otherwise, so color is never the only cue.
    var stripName: String { stacked ? AgentShares.stripName(agentTotals) : "Claude Code tokens, last 30 days" }

    var status: String? {
        switch mode {
        case .agentsOnly: nil
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
                      agents: [AgentSource: VaultWindow] = [:],
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
            let others = agentDay(agents, d)
            if d < hot, let record {
                let tokens = record.window.byDay[d] ?? 0
                o.strip.append(Day(day: d, tokens: tokens,
                                   noRecord: tokens == 0 && others.isEmpty && !record.covered.contains(d),
                                   live: false, agents: others))
            } else {
                let day = live[d] ?? CCLiveDay()
                o.strip.append(Day(day: d, tokens: day.total, noRecord: false, live: true, agents: others))
                for (cwd, v) in day.byCwd { projects[name(cwd), default: 0] += v }
                for (s, v) in day.bySkill { skills[s, default: 0] += v }
            }
            d = d.adding(days: 1)
        }
        let todayLive = live[today] ?? CCLiveDay()
        o.today = todayLive.total + (o.strip.last?.agents.values.reduce(0, +) ?? 0)
        o.todayInput = todayLive.input + agents.values.reduce(0) { $0 + ($1.byDayInput[today] ?? 0) }
        o.todayOutput = todayLive.output + agents.values.reduce(0) { $0 + ($1.byDayOutput[today] ?? 0) }
        o.windowTotal = o.strip.reduce(0) { $0 + $1.total }
        o.projects = rankedProjects(projects, agents: agents)
        o.skills = UsageRanked.ranked(skills, top: topCount)
        o.agentTotals = agentTotals(o.strip)
        return o
    }

    /// The day's tokens per agent other than Claude Code, from their Vault windows.
    private static func agentDay(_ agents: [AgentSource: VaultWindow], _ day: CCLocalDay) -> [AgentSource: Int64] {
        var out: [AgentSource: Int64] = [:]
        for (agent, w) in agents { if let t = w.byDay[day], t > 0 { out[agent] = t } }
        return out
    }

    /// Per agent over the strip, Claude Code included, and only when some other agent has tokens.
    private static func agentTotals(_ strip: [Day]) -> [AgentSource: Int64] {
        var totals: [AgentSource: Int64] = [:]
        for day in strip {
            for (a, t) in day.agents { totals[a, default: 0] += t }
        }
        guard !totals.isEmpty else { return [:] }
        totals[.claude] = strip.reduce(0) { $0 + $1.tokens }
        return totals.filter { $0.value > 0 }
    }

    /// The projects, the agents' merged in by name, each row of an agent-touched project carrying
    /// its shares (Claude Code's part is what the agents leave).
    static func rankedProjects(_ claude: [String: Int64], agents: [AgentSource: VaultWindow],
                               top: Int = topCount) -> [UsageRanked] {
        var totals = claude
        var shares: [String: [AgentSource: Int64]] = [:]
        for (agent, w) in agents {
            for (n, v) in w.byProjectName where v > 0 {
                totals[n, default: 0] += v
                shares[n, default: [:]][agent, default: 0] += v
            }
        }
        return UsageRanked.ranked(totals, top: top).map { row in
            guard var split = shares[row.name] else { return row }
            split[.claude] = max(0, row.total - split.values.reduce(0, +))
            return UsageRanked(name: row.name, total: row.total, byAgent: split)
        }
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
        /// The week's tokens from the agents other than Claude Code (`total` counts them too).
        var agents: [AgentSource: Int64] = [:]
        var id: CCLocalDay { weekStart }

        /// Claude Code's part of the week.
        var claudeTokens: Int64 { total - agents.values.reduce(0, +) }

        /// The stack, bottom up: Claude Code, then each agent with tokens, in agent order.
        var segments: [(agent: AgentSource, tokens: Int64)] {
            var out: [(agent: AgentSource, tokens: Int64)] = []
            if claudeTokens > 0 { out.append((.claude, claudeTokens)) }
            for a in AgentSource.burnRoots { if let t = agents[a], t > 0 { out.append((a, t)) } }
            return out
        }

        /// "400", or with an agent in the week "1.4k (Claude Code 400, Codex 1.0k)".
        var valueText: String {
            agents.isEmpty ? TokenFormat.compact(total) : AgentShares.describe(total, agents.merging([.claude: claudeTokens]) { $1 })
        }
    }

    static let ranges = [4, 12, 26]
    static let defaultWeeks = 12
    static let topCount = 5

    var bars: [Bar] = []
    var top: [UsageRanked] = []
    var birth: CCLocalDay?
    var today: CCLocalDay?
    /// Whether the account keeps a Claude Code record (the fresh-record note speaks of its logs).
    var claudeRecord = true
    /// The span's tokens per agent with any, Claude Code included; empty with Claude Code alone.
    var agentTotals: [AgentSource: Int64] = [:]

    var maxTotal: Int64 { bars.map(\.total).max() ?? 0 }

    /// Whether some agent other than Claude Code has tokens in the span: only then do the bars
    /// stack by agent.
    var stacked: Bool { !agentTotals.isEmpty }

    /// The chart's accessible name: unchanged with Claude Code alone, each agent's span total
    /// otherwise, so color is never the only cue (each bar's value gives its own shares).
    var chartName: String {
        stacked ? "Tokens by week, " + AgentShares.list(agentTotals) : "Claude Code tokens by week"
    }

    /// "History kept since July 12, 2026".
    var footer: String? { birth.map { "History kept since \(UsageDates.long($0))" } }

    /// The first day or two: the backfill seeded about four weeks.
    var freshNote: String? {
        guard claudeRecord, let birth, let today, today.epochDay - birth.epochDay <= 1 else { return nil }
        return "A new record: its first pass kept about four weeks from Claude Code's logs. Longer trends fill in from here."
    }

    static func build(weeks: [VaultWeek], agents: [AgentSource: [VaultWeek]] = [:],
                      top: [(name: String, total: Int64)], birth: CCLocalDay?, today: CCLocalDay) -> UsageTrends {
        build(weeks: weeks, agents: agents, ranked: top.map { UsageRanked(name: $0.name, total: $0.total) },
              birth: birth, today: today)
    }

    /// Trends from the Claude Code record's weeks (empty without one) and each agent root's weeks.
    /// An agent with no tokens in the span is left out, so a Mac with Claude Code alone reads as
    /// 2.47.1 whatever else sits in the Vault.
    static func build(weeks: [VaultWeek], agents: [AgentSource: [VaultWeek]], ranked: [UsageRanked],
                      birth: CCLocalDay?, today: CCLocalDay) -> UsageTrends {
        var t = UsageTrends()
        t.birth = birth
        t.today = today
        t.claudeRecord = !weeks.isEmpty
        let live = agents.filter { $0.value.contains { $0.total > 0 } }
        t.bars = bars(weeks: weeks, agents: live)
        t.top = ranked
        t.agentTotals = agentTotals(t.bars)
        return t
    }

    /// One bar per week any source has. A week is "no record" or edged only when every source
    /// has a gap in it: a source that covers the whole week proves its zero is real.
    private static func bars(weeks: [VaultWeek], agents: [AgentSource: [VaultWeek]]) -> [Bar] {
        let claude = Dictionary(weeks.map { ($0.weekStart, $0) }) { $1 }
        let byAgent = agents.mapValues { list in Dictionary(list.map { ($0.weekStart, $0) }) { $1 } }
        var starts = Set(claude.keys)
        for list in byAgent.values { starts.formUnion(list.keys) }
        return starts.sorted().map { start in
            var others: [AgentSource: Int64] = [:]
            var gaps: [Bool] = weeks.isEmpty ? [] : [claude[start]?.hasNoRecordGap ?? true]
            var current = claude[start]?.isCurrent ?? false
            for (agent, list) in byAgent {
                let w = list[start]
                if let t = w?.total, t > 0 { others[agent] = t }
                gaps.append(w?.hasNoRecordGap ?? true)
                current = current || (w?.isCurrent ?? false)
            }
            let total = (claude[start]?.total ?? 0) + others.values.reduce(0, +)
            let gap = gaps.allSatisfy { $0 }
            let kind: Kind = total > 0 ? .bar : (gap ? .noRecord : .zero)
            return Bar(weekStart: start, total: total, kind: kind, isCurrent: current,
                       partialGap: total > 0 && gap, agents: others)
        }
    }

    /// Per agent over the bars, Claude Code included, and only when some other agent has tokens.
    private static func agentTotals(_ bars: [Bar]) -> [AgentSource: Int64] {
        var totals: [AgentSource: Int64] = [:]
        for bar in bars {
            for (a, t) in bar.agents { totals[a, default: 0] += t }
        }
        guard !totals.isEmpty else { return [:] }
        totals[.claude] = bars.reduce(0) { $0 + $1.claudeTokens }
        return totals.filter { $0.value > 0 }
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

    var agent: AgentSource { session.agent }
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

    // MARK: Agents

    /// The agents with a session in the rows, in agent order.
    static func agentsPresent(_ rows: [LedgerRow]) -> [AgentSource] {
        AgentSource.burn.filter { a in rows.contains { $0.agent == a } }
    }

    /// The agent filter shows only when more than one agent has sessions.
    static func showsAgentFilter(_ rows: [LedgerRow]) -> Bool { agentsPresent(rows).count > 1 }

    /// The per-row agent label shows once an agent other than Claude Code is in the list; Claude
    /// Code alone reads as it did in 2.45.0.
    static func showsAgentLabels(_ rows: [LedgerRow]) -> Bool { rows.contains { $0.agent != .claude } }

    /// The rows of one agent, or all of them.
    static func filtered(_ rows: [LedgerRow], agent: AgentSource?) -> [LedgerRow] {
        guard let agent else { return rows }
        return rows.filter { $0.agent == agent }
    }

    /// What a screen reader says for a row; the agent follows the project once labels show.
    static func spoken(_ row: LedgerRow, now: Date, showsAgent: Bool) -> String {
        let agent = showsAgent ? ", \(row.agent.burnName)" : ""
        return "\(row.projectText)\(agent), \(tokensText(row.scoped)) tokens, \(relative(row.session.lastTs, now: now))"
    }

    /// What the CSV's `root` column and the expansion call the row's record: an agent's root is
    /// its id (`codex`), a Claude Code folder is the account's folder name.
    static func recordText(_ row: LedgerRow, recordTitle: String) -> String {
        row.agent == .claude ? recordTitle : row.agent.rawValue
    }

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
            VaultLedgerCsv.row($0.session, root: recordText($0, recordTitle: recordTitle), project: $0.projectText,
                               tokensInScope: $0.scoped)
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

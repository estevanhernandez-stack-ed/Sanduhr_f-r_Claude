import Foundation

/// Closed days of one or more recorded folders, merged (Windows `VaultWindow`). Projects merge by
/// display name; `byModel` (raw model strings) is a Mac addition for the MCP tools and the Claude
/// Usage page. A day at 0 input and 0 output with a total is WS-C-era (unsplit), not zero traffic.
struct VaultWindow: Equatable, Sendable {
    var byDay: [CCLocalDay: Int64] = [:]
    var byProjectName: [String: Int64] = [:]
    var bySkill: [String: Int64] = [:]
    var byModel: [String: Int64] = [:]
    var byDayInput: [CCLocalDay: Int64] = [:]
    var byDayOutput: [CCLocalDay: Int64] = [:]
    /// Distinct logical sessions per day, summed over folders.
    var sessionsByDay: [CCLocalDay: Int] = [:]

    var total: Int64 { byDay.values.reduce(0, +) }

    /// `byModel` through the tier map (`CCLogReader.tierForModel`): a read-time projection, so a
    /// map update heals all history. Unmapped models are left out here.
    var byTier: [String: Int64] {
        var out: [String: Int64] = [:]
        for (model, v) in byModel {
            if let tier = CCLogReader.tierForModel(model) { out[tier, default: 0] += v }
        }
        return out
    }
}

/// One Trends bar. `hasNoRecordGap`: some day of the week (up to today) isn't covered by every
/// folder asked about, drawn as "no record", never as a zero bar.
struct VaultWeek: Equatable, Sendable {
    let weekStart: CCLocalDay
    let total: Int64
    let isCurrent: Bool
    let hasNoRecordGap: Bool
}

/// One logical session: the main transcript and its nested subagent transcripts
/// (`parent_session ?? uuid`), each member's month slices merged first. `agentCount` and
/// `agentTokens` sum the members other than the main one. `root` is the folder's vault id.
struct VaultSessionInfo: Equatable, Sendable {
    let uuid: String
    let root: String
    let projectKey: String
    let projectName: String
    let cwd: String?
    let firstTs: Date
    let lastTs: Date
    let total: Int64
    let byModel: [String: Int64]
    let bySkill: [String: Int64]?
    let byDay: [String: VaultDayBucket]
    let cache: VaultCacheTokens?
    let agentCount: Int
    let agentTokens: Int64
}

/// The read side of the vault (item 46), a port of Windows `VaultReader`, for the MCP tools
/// (item 47) and the Claude Usage page (item 48): by day, by week, by project, by model, by
/// skill, the sessions ledger and the coverage and birth facts. Never writes: a missing or
/// unreadable shard reads as empty and is left for the next ingest to quarantine.
final class VaultReader: @unchecked Sendable {
    private let store: VaultStore

    init(store: VaultStore) { self.store = store }

    /// The display name in a `project_key` (`api~3f2a91cc` is `api`); Hidden keys have no `~`.
    static func projectNameOf(_ projectKey: String) -> String {
        guard let i = projectKey.lastIndex(of: "~"), i > projectKey.startIndex else { return projectKey }
        return String(projectKey[..<i])
    }

    // MARK: Days, projects, models

    /// Days in `[from, toExclusive)` from the rollups.
    func readWindow(_ roots: [String], from: CCLocalDay, toExclusive: CCLocalDay) -> VaultWindow {
        var w = VaultWindow()
        for root in roots {
            for month in Self.months(from: from, toExclusive: toExclusive) {
                let (result, shard) = store.loadRollupShard(root, month)
                guard result == .ok else { continue }
                for (key, day) in shard.days {
                    guard let date = CCLocalDay(key: key), date >= from, date < toExclusive else { continue }
                    w.byDay[date, default: 0] += day.total
                    w.byDayInput[date, default: 0] += day.input
                    w.byDayOutput[date, default: 0] += day.output
                    w.sessionsByDay[date, default: 0] += day.sessions
                    for (k, v) in day.byProject { w.byProjectName[Self.projectNameOf(k), default: 0] += v }
                    for (s, v) in day.bySkill { w.bySkill[s, default: 0] += v }
                    for (m, v) in day.byModel { w.byModel[m, default: 0] += v }
                }
            }
        }
        return w
    }

    /// `weeks` Monday-start weeks ending with the one holding `today`.
    func readWeeks(_ roots: [String], weeks: Int, today: CCLocalDay) -> [VaultWeek] {
        guard weeks > 0 else { return [] }
        let current = today.weekStart
        let first = current.adding(days: -7 * (weeks - 1))
        let window = readWindow(roots, from: first, toExclusive: today.adding(days: 1))
        let metas = roots.map { store.loadMeta($0) }
        return (0..<weeks).map { i in
            let start = first.adding(days: 7 * i)
            var total: Int64 = 0
            var gap = false
            for d in 0..<7 {
                let day = start.adding(days: d)
                if day > today { break }
                total += window.byDay[day] ?? 0
                if !Self.isDayCovered(metas, day) { gap = true }
            }
            return VaultWeek(weekStart: start, total: total, isCurrent: start == current, hasNoRecordGap: gap)
        }
    }

    /// The `top` projects by tokens in `[from, toExclusive)`, largest first (ties by name).
    func topProjects(_ roots: [String], from: CCLocalDay, toExclusive: CCLocalDay, top: Int) -> [(name: String, total: Int64)] {
        Self.ranked(readWindow(roots, from: from, toExclusive: toExclusive).byProjectName, top: top)
    }

    /// The `top` raw model strings by tokens in `[from, toExclusive)`, largest first.
    func topModels(_ roots: [String], from: CCLocalDay, toExclusive: CCLocalDay, top: Int) -> [(name: String, total: Int64)] {
        Self.ranked(readWindow(roots, from: from, toExclusive: toExclusive).byModel, top: top)
    }

    static func ranked(_ totals: [String: Int64], top: Int) -> [(name: String, total: Int64)] {
        totals.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }
            .prefix(max(0, top))
            .map { (name: $0.key, total: $0.value) }
    }

    // MARK: Sessions

    /// Every logical session in the folders, from the session shards.
    func readSessions(_ roots: [String]) -> [VaultSessionInfo] {
        var result: [VaultSessionInfo] = []
        for root in roots {
            var byUuid: [String: [VaultSessionRow]] = [:]
            for month in store.listSessionShardMonths(root) {
                let (status, shard) = store.loadSessionShard(root, month)
                guard status == .ok else { continue }
                for (uuid, row) in shard.sessions { byUuid[uuid, default: []].append(row) }
            }
            // A session = its main transcript + nested transcripts. Identity comes from the main
            // member; when it aged out, the first member by uuid speaks for the group.
            var byLogical: [String: [(file: String, row: VaultSessionRow)]] = [:]
            for (uuid, rows) in byUuid {
                let merged = VaultRowMath.merge(rows)
                byLogical[merged.parentSession ?? uuid, default: []].append((uuid, merged))
            }
            for (logical, unsorted) in byLogical {
                let members = unsorted.sorted { $0.file < $1.file }
                let identity = members.first { $0.file == logical }?.row ?? members[0].row
                let fold = VaultRowMath.merge(members.map(\.row))
                let firsts = members.compactMap { VaultTime.parse($0.row.firstTs) }
                let lasts = members.compactMap { VaultTime.parse($0.row.lastTs) }
                guard let firstTs = firsts.min(), let lastTs = lasts.max() else { continue }
                let cacheRead = members.reduce(Int64(0)) { $0 + ($1.row.cacheTokens?.read ?? 0) }
                let cacheCreation = members.reduce(Int64(0)) { $0 + ($1.row.cacheTokens?.creation ?? 0) }
                let agents = members.filter { $0.file != logical }
                result.append(VaultSessionInfo(
                    uuid: logical, root: root, projectKey: identity.projectKey,
                    projectName: identity.projectName, cwd: identity.cwd,
                    firstTs: firstTs, lastTs: lastTs, total: fold.total, byModel: fold.byModel,
                    bySkill: fold.bySkill, byDay: fold.byDay,
                    cache: cacheRead + cacheCreation > 0
                        ? VaultCacheTokens(read: cacheRead, creation: cacheCreation) : nil,
                    agentCount: agents.count,
                    agentTokens: agents.reduce(Int64(0)) { $0 + $1.row.total }))
            }
        }
        return result
    }

    /// A session's tokens on days in `[from, through]` (the ledger's date scope).
    static func tokensInScope(_ s: VaultSessionInfo, from: CCLocalDay, through: CCLocalDay) -> Int64 {
        var total: Int64 = 0
        for (key, bucket) in s.byDay {
            if let day = CCLocalDay(key: key), day >= from, day <= through { total += bucket.total }
        }
        return total
    }

    // MARK: Meta

    /// The earliest birth date among the folders.
    func birthDate(_ roots: [String]) -> CCLocalDay? {
        roots.compactMap { store.loadMeta($0).flatMap { CCLocalDay(key: $0.since) } }.min()
    }

    /// The oldest of the folders' last successful ingests; nil when any folder never finished
    /// one (an unstarted folder reads as stale, not fresh).
    func lastSuccessfulIngest(_ roots: [String]) -> Date? {
        var oldest: Date?
        for root in roots {
            guard let meta = store.loadMeta(root), let ts = VaultTime.parse(meta.lastIngestTs) else { return nil }
            if oldest.map({ ts < $0 }) ?? true { oldest = ts }
        }
        return oldest
    }

    /// The days in `[from, through]` every folder covered. Metas load once.
    func coveredSet(_ roots: [String], from: CCLocalDay, through: CCLocalDay) -> Set<CCLocalDay> {
        guard !roots.isEmpty, from <= through else { return [] }
        let metas = roots.map { store.loadMeta($0) }
        var out = Set<CCLocalDay>()
        var d = from
        while d <= through {
            if Self.isDayCovered(metas, d) { out.insert(d) }
            d = d.adding(days: 1)
        }
        return out
    }

    /// Covered only when every folder's ranges hold the day.
    func isDayCovered(_ roots: [String], _ day: CCLocalDay) -> Bool {
        Self.isDayCovered(roots.map { store.loadMeta($0) }, day)
    }

    private static func isDayCovered(_ metas: [VaultRootMeta?], _ day: CCLocalDay) -> Bool {
        guard !metas.isEmpty else { return false }
        for meta in metas {
            guard let meta else { return false }
            let covered = meta.covered.contains { r in
                guard let f = CCLocalDay(key: r.from), let t = CCLocalDay(key: r.to) else { return false }
                return day >= f && day <= t
            }
            if !covered { return false }
        }
        return true
    }

    /// The shard months touching `[from, toExclusive)`.
    static func months(from: CCLocalDay, toExclusive: CCLocalDay) -> [String] {
        let end = toExclusive.adding(days: -1)
        var cursor = CCLocalDay(year: from.year, month: from.month, day: 1)
        var out: [String] = []
        while cursor <= end {
            out.append(cursor.monthKey)
            cursor = cursor.nextMonthStart
        }
        return out
    }
}

// MARK: Ledger CSV

/// The sessions ledger as CSV (Windows `VaultLedgerCsv`): no IO, rows in the order given (the
/// caller sorts), CRLF line ends, RFC 4180 minimal quoting.
enum VaultLedgerCsv {
    struct Row: Equatable, Sendable {
        let uuid: String
        let root: String
        let project: String
        let firstTs: String
        let lastTs: String
        let tokensInScope: Int64
        let tokensTotal: Int64
        let models: String
    }

    static let header = "session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models\r\n"

    static func build(_ rows: [Row]) -> (text: String, rowCount: Int) {
        var s = header
        for r in rows {
            s += [escape(r.uuid), escape(r.root), escape(r.project), escape(r.firstTs), escape(r.lastTs),
                  String(r.tokensInScope), String(r.tokensTotal), escape(r.models)].joined(separator: ",")
            s += "\r\n"
        }
        return (s, rows.count)
    }

    /// A ledger row as Windows builds it: whole-second UTC times with `Z`, models as
    /// `model:tokens` joined by `;`, largest first. `root` and `project` are what the caller shows.
    static func row(_ s: VaultSessionInfo, root: String, project: String, tokensInScope: Int64) -> Row {
        Row(uuid: s.uuid, root: root, project: project,
            firstTs: utcSeconds(s.firstTs), lastTs: utcSeconds(s.lastTs),
            tokensInScope: tokensInScope, tokensTotal: s.total,
            models: VaultReader.ranked(s.byModel, top: s.byModel.count)
                .map { "\($0.name):\($0.total)" }.joined(separator: ";"))
    }

    static func utcSeconds(_ d: Date) -> String {
        let iso = VaultTime.iso(Date(timeIntervalSince1970: d.timeIntervalSince1970.rounded(.down)))
        return String(iso.prefix(19)) + "Z"
    }

    /// Quoted only when it holds a comma, a quote, CR or LF; quotes doubled.
    static func escape(_ field: String) -> String {
        guard field.contains(where: { $0 == "," || $0 == "\"" || $0 == "\n" || $0 == "\r" || $0 == "\r\n" })
        else { return field }
        return "\"" + field.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }
}

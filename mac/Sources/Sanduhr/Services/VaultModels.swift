import Foundation

/// Wire schema of the usage vault (item 46), the Windows one byte for byte in meaning:
/// `docs/superpowers/specs/2026-07-12-usage-vault-design.md` and Windows `VaultModels.cs`.
/// Session shards are the irreplaceable record: raw model strings (never tiers), unconditional
/// totals, per-local-day buckets. Meaning changes take a new field name; readers accept every
/// `schema_version` up to the current one forever, since the logs to re-derive old shards are
/// gone. Every decoder here is lenient the way .NET's is: a missing field reads as its default
/// (WS-C-era buckets have no `input`/`output`), unknown fields are ignored.
enum VaultSchema {
    static let currentSchemaVersion = 1
}

/// One local calendar day inside a session row. `total` is unconditional (every timestamped
/// event with tokens); `by_model` keys are raw Claude Code model strings. `input`/`output` are 0
/// on WS-C-era rows: 0/0 with a total means "unsplit", not zero traffic.
struct VaultDayBucket: Codable, Equatable, Sendable {
    var total: Int64 = 0
    var input: Int64 = 0
    var output: Int64 = 0
    var byModel: [String: Int64] = [:]
    /// Omitted when the day had no attributed events.
    var bySkill: [String: Int64]?

    enum CodingKeys: String, CodingKey {
        case total, input, output
        case byModel = "by_model"
        case bySkill = "by_skill"
    }

    init(total: Int64 = 0, input: Int64 = 0, output: Int64 = 0,
         byModel: [String: Int64] = [:], bySkill: [String: Int64]? = nil) {
        self.total = total
        self.input = input
        self.output = output
        self.byModel = byModel
        self.bySkill = bySkill
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        total = try c.decodeIfPresent(Int64.self, forKey: .total) ?? 0
        input = try c.decodeIfPresent(Int64.self, forKey: .input) ?? 0
        output = try c.decodeIfPresent(Int64.self, forKey: .output) ?? 0
        byModel = try c.decodeIfPresent([String: Int64].self, forKey: .byModel) ?? [:]
        bySkill = try c.decodeIfPresent([String: Int64].self, forKey: .bySkill)
    }
}

struct VaultCacheTokens: Codable, Equatable, Sendable {
    var read: Int64 = 0
    var creation: Int64 = 0

    init(read: Int64 = 0, creation: Int64 = 0) {
        self.read = read
        self.creation = creation
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        read = try c.decodeIfPresent(Int64.self, forKey: .read) ?? 0
        creation = try c.decodeIfPresent(Int64.self, forKey: .creation) ?? 0
    }
}

/// One session's aggregates, or one month slice of it when `continuation` is true. Row
/// invariant: `total == sum(by_day[*].total)`, so the rollup fold for a day reads one month's
/// shard. `event_count`, `skipped_lines` and `cache_tokens` ride the primary row only.
struct VaultSessionRow: Codable, Equatable, Sendable {
    var projectKey = ""
    var projectName = ""
    /// Only with Full paths (Windows `store_full_paths`).
    var cwd: String?
    /// The session a nested subagent or workflow transcript belongs to; nil for main
    /// transcripts. Readers fold rows by `parent_session ?? uuid`.
    var parentSession: String?
    var firstTs = ""
    var lastTs = ""
    var utcOffsetMin = 0
    var eventCount: Int64 = 0
    var skippedLines: Int64 = 0
    var continuation = false
    var total: Int64 = 0
    var byModel: [String: Int64] = [:]
    var cacheTokens: VaultCacheTokens?
    var bySkill: [String: Int64]?
    var byDay: [String: VaultDayBucket] = [:]

    enum CodingKeys: String, CodingKey {
        case projectKey = "project_key"
        case projectName = "project_name"
        case cwd
        case parentSession = "parent_session"
        case firstTs = "first_ts"
        case lastTs = "last_ts"
        case utcOffsetMin = "utc_offset_min"
        case eventCount = "event_count"
        case skippedLines = "skipped_lines"
        case continuation, total
        case byModel = "by_model"
        case cacheTokens = "cache_tokens"
        case bySkill = "by_skill"
        case byDay = "by_day"
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        projectKey = try c.decodeIfPresent(String.self, forKey: .projectKey) ?? ""
        projectName = try c.decodeIfPresent(String.self, forKey: .projectName) ?? ""
        cwd = try c.decodeIfPresent(String.self, forKey: .cwd)
        parentSession = try c.decodeIfPresent(String.self, forKey: .parentSession)
        firstTs = try c.decodeIfPresent(String.self, forKey: .firstTs) ?? ""
        lastTs = try c.decodeIfPresent(String.self, forKey: .lastTs) ?? ""
        utcOffsetMin = try c.decodeIfPresent(Int.self, forKey: .utcOffsetMin) ?? 0
        eventCount = try c.decodeIfPresent(Int64.self, forKey: .eventCount) ?? 0
        skippedLines = try c.decodeIfPresent(Int64.self, forKey: .skippedLines) ?? 0
        continuation = try c.decodeIfPresent(Bool.self, forKey: .continuation) ?? false
        total = try c.decodeIfPresent(Int64.self, forKey: .total) ?? 0
        byModel = try c.decodeIfPresent([String: Int64].self, forKey: .byModel) ?? [:]
        cacheTokens = try c.decodeIfPresent(VaultCacheTokens.self, forKey: .cacheTokens)
        bySkill = try c.decodeIfPresent([String: Int64].self, forKey: .bySkill)
        byDay = try c.decodeIfPresent([String: VaultDayBucket].self, forKey: .byDay) ?? [:]
    }
}

/// `sessions-YYYY-MM.json`.
struct VaultSessionShard: Codable, Equatable, Sendable {
    var schemaVersion = VaultSchema.currentSchemaVersion
    var writerVersion = ""
    var sessions: [String: VaultSessionRow] = [:]

    enum CodingKeys: String, CodingKey {
        case schemaVersion = "schema_version"
        case writerVersion = "writer_version"
        case sessions
    }

    init(schemaVersion: Int = VaultSchema.currentSchemaVersion, writerVersion: String = "",
         sessions: [String: VaultSessionRow] = [:]) {
        self.schemaVersion = schemaVersion
        self.writerVersion = writerVersion
        self.sessions = sessions
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? VaultSchema.currentSchemaVersion
        writerVersion = try c.decodeIfPresent(String.self, forKey: .writerVersion) ?? ""
        sessions = try c.decodeIfPresent([String: VaultSessionRow].self, forKey: .sessions) ?? [:]
    }
}

/// One day of `rollups-YYYY-MM.json`: a declared derived cache, deletable at any time and
/// rebuilt by folding the month's session shard. Never a second truth.
struct VaultRollupDay: Codable, Equatable, Sendable {
    var total: Int64 = 0
    var input: Int64 = 0
    var output: Int64 = 0
    var byModel: [String: Int64] = [:]
    var byProject: [String: Int64] = [:]
    var bySkill: [String: Int64] = [:]
    /// Distinct logical sessions touching the day (a main transcript and its agents are one).
    var sessions = 0

    enum CodingKeys: String, CodingKey {
        case total, input, output
        case byModel = "by_model"
        case byProject = "by_project"
        case bySkill = "by_skill"
        case sessions
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        total = try c.decodeIfPresent(Int64.self, forKey: .total) ?? 0
        input = try c.decodeIfPresent(Int64.self, forKey: .input) ?? 0
        output = try c.decodeIfPresent(Int64.self, forKey: .output) ?? 0
        byModel = try c.decodeIfPresent([String: Int64].self, forKey: .byModel) ?? [:]
        byProject = try c.decodeIfPresent([String: Int64].self, forKey: .byProject) ?? [:]
        bySkill = try c.decodeIfPresent([String: Int64].self, forKey: .bySkill) ?? [:]
        sessions = try c.decodeIfPresent(Int.self, forKey: .sessions) ?? 0
    }
}

struct VaultRollupShard: Codable, Equatable, Sendable {
    var schemaVersion = VaultSchema.currentSchemaVersion
    var days: [String: VaultRollupDay] = [:]

    enum CodingKeys: String, CodingKey {
        case schemaVersion = "schema_version"
        case days
    }

    init(schemaVersion: Int = VaultSchema.currentSchemaVersion, days: [String: VaultRollupDay] = [:]) {
        self.schemaVersion = schemaVersion
        self.days = days
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? VaultSchema.currentSchemaVersion
        days = try c.decodeIfPresent([String: VaultRollupDay].self, forKey: .days) ?? [:]
    }
}

/// Bookkeeping for one session log, keyed in `checkpoints.json` by the SHA-256 of its
/// case-folded absolute path (`VaultStore.pathKey`), never a readable path. `mtime_ticks` is
/// .NET ticks (100 ns since 0001-01-01 UTC), as Windows writes it.
struct VaultCheckpointEntry: Codable, Equatable, Sendable {
    var mtimeTicks: Int64 = 0
    var length: Int64 = 0
    var offset: Int64 = 0
    var tailGuard = ""
    /// Fingerprint of the stored rows this checkpoint corresponds to: a tail parse is trusted
    /// only when the stored rows still match it (after a crash between the shard write and the
    /// checkpoint write they are newer, and seeding from them would double-count).
    var rowTotal: Int64 = 0
    var rowEvents: Int64 = 0
    var rowCacheRead: Int64 = 0
    var rowCacheCreation: Int64 = 0
    var months: [String] = []
    var sealed = false
    var lastSeen = ""

    enum CodingKeys: String, CodingKey {
        case mtimeTicks = "mtime_ticks"
        case length, offset
        case tailGuard = "tail_guard"
        case rowTotal = "row_total"
        case rowEvents = "row_events"
        case rowCacheRead = "row_cache_read"
        case rowCacheCreation = "row_cache_creation"
        case months, sealed
        case lastSeen = "last_seen"
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        mtimeTicks = try c.decodeIfPresent(Int64.self, forKey: .mtimeTicks) ?? 0
        length = try c.decodeIfPresent(Int64.self, forKey: .length) ?? 0
        offset = try c.decodeIfPresent(Int64.self, forKey: .offset) ?? 0
        tailGuard = try c.decodeIfPresent(String.self, forKey: .tailGuard) ?? ""
        rowTotal = try c.decodeIfPresent(Int64.self, forKey: .rowTotal) ?? 0
        rowEvents = try c.decodeIfPresent(Int64.self, forKey: .rowEvents) ?? 0
        rowCacheRead = try c.decodeIfPresent(Int64.self, forKey: .rowCacheRead) ?? 0
        rowCacheCreation = try c.decodeIfPresent(Int64.self, forKey: .rowCacheCreation) ?? 0
        months = try c.decodeIfPresent([String].self, forKey: .months) ?? []
        sealed = try c.decodeIfPresent(Bool.self, forKey: .sealed) ?? false
        lastSeen = try c.decodeIfPresent(String.self, forKey: .lastSeen) ?? ""
    }
}

struct VaultCheckpointFile: Codable, Equatable, Sendable {
    var schemaVersion = VaultSchema.currentSchemaVersion
    var entries: [String: VaultCheckpointEntry] = [:]

    enum CodingKeys: String, CodingKey {
        case schemaVersion = "schema_version"
        case entries
    }

    init(schemaVersion: Int = VaultSchema.currentSchemaVersion, entries: [String: VaultCheckpointEntry] = [:]) {
        self.schemaVersion = schemaVersion
        self.entries = entries
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? VaultSchema.currentSchemaVersion
        entries = try c.decodeIfPresent([String: VaultCheckpointEntry].self, forKey: .entries) ?? [:]
    }
}

struct VaultDateRange: Codable, Equatable, Sendable {
    var from = ""
    var to = ""

    init(from: String, to: String) {
        self.from = from
        self.to = to
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        from = try c.decodeIfPresent(String.self, forKey: .from) ?? ""
        to = try c.decodeIfPresent(String.self, forKey: .to) ?? ""
    }
}

/// `meta.json`: the vault's birth date, the days its ingests covered (the "no record" texture),
/// the last successful ingest (degraded-mode gate) and the discovery-walk generation.
struct VaultRootMeta: Codable, Equatable, Sendable {
    var since = ""
    var covered: [VaultDateRange] = []
    var lastIngestTs = ""
    /// 2 is the recursive walk (nested subagent transcripts); below it, checkpoints are
    /// invalidated once so the re-ingest sees them. The Mac has walked recursively from the
    /// start and always writes 2.
    var walkVersion = 0

    enum CodingKeys: String, CodingKey {
        case since, covered
        case lastIngestTs = "last_ingest_ts"
        case walkVersion = "walk_version"
    }

    init(since: String = "", covered: [VaultDateRange] = [], lastIngestTs: String = "", walkVersion: Int = 0) {
        self.since = since
        self.covered = covered
        self.lastIngestTs = lastIngestTs
        self.walkVersion = walkVersion
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        since = try c.decodeIfPresent(String.self, forKey: .since) ?? ""
        covered = try c.decodeIfPresent([VaultDateRange].self, forKey: .covered) ?? []
        lastIngestTs = try c.decodeIfPresent(String.self, forKey: .lastIngestTs) ?? ""
        walkVersion = try c.decodeIfPresent(Int.self, forKey: .walkVersion) ?? 0
    }
}

// MARK: Row algebra

/// Shared by the ingester (split a parsed session into month rows) and every reader (merge a
/// session's primary and continuation slices back into one). `merge(splitByMonth(x)) == x`.
enum VaultRowMath {
    /// Per-month rows keyed `yyyy-MM`. The primary month is the month of the earliest day
    /// (the local date of `first_ts`). Each row's total, models and skills come from its own
    /// days; event count, skipped lines and cache tokens ride the primary only.
    static func splitByMonth(_ merged: VaultSessionRow) -> [String: VaultSessionRow] {
        guard let firstDay = merged.byDay.keys.min() else { return [:] }
        let primaryMonth = String(firstDay.prefix(7))
        let groups = Dictionary(grouping: merged.byDay, by: { String($0.key.prefix(7)) })
        var result: [String: VaultSessionRow] = [:]
        for (month, days) in groups {
            let primary = month == primaryMonth
            var row = merged
            row.eventCount = primary ? merged.eventCount : 0
            row.skippedLines = primary ? merged.skippedLines : 0
            row.continuation = !primary
            row.cacheTokens = primary ? merged.cacheTokens : nil
            row.byDay = Dictionary(uniqueKeysWithValues: days.map { ($0.key, $0.value) })
            recomputeRowAggregates(&row)
            result[month] = row
        }
        return result
    }

    /// One session's rows (primary and slices, any order) as a single logical row. Same-day
    /// buckets sum (a multi-file logical fold reuses this and does collide).
    static func merge(_ rows: [VaultSessionRow]) -> VaultSessionRow {
        guard let first = rows.first else { return VaultSessionRow() }
        let primary = rows.first { !$0.continuation } ?? first
        var merged = VaultSessionRow()
        merged.projectKey = primary.projectKey
        merged.projectName = primary.projectName
        merged.cwd = primary.cwd
        merged.parentSession = primary.parentSession
        merged.firstTs = primary.firstTs
        merged.lastTs = primary.lastTs
        merged.utcOffsetMin = primary.utcOffsetMin
        merged.eventCount = rows.reduce(0) { $0 + $1.eventCount }
        merged.skippedLines = rows.reduce(0) { $0 + $1.skippedLines }
        merged.continuation = false
        merged.cacheTokens = rows.lazy.compactMap(\.cacheTokens).first
        for row in rows {
            for (day, bucket) in row.byDay {
                var acc = merged.byDay[day] ?? VaultDayBucket()
                acc.total += bucket.total
                acc.input += bucket.input
                acc.output += bucket.output
                for (m, v) in bucket.byModel { acc.byModel[m, default: 0] += v }
                if let skills = bucket.bySkill {
                    var s = acc.bySkill ?? [:]
                    for (k, v) in skills { s[k, default: 0] += v }
                    acc.bySkill = s
                }
                merged.byDay[day] = acc
            }
        }
        recomputeRowAggregates(&merged)
        return merged
    }

    /// Total, models and skills := the sums of the row's own day buckets.
    static func recomputeRowAggregates(_ row: inout VaultSessionRow) {
        var total: Int64 = 0
        var byModel: [String: Int64] = [:]
        var bySkill: [String: Int64] = [:]
        for bucket in row.byDay.values {
            total += bucket.total
            for (m, v) in bucket.byModel { byModel[m, default: 0] += v }
            if let skills = bucket.bySkill {
                for (s, v) in skills { bySkill[s, default: 0] += v }
            }
        }
        row.total = total
        row.byModel = byModel
        row.bySkill = bySkill.isEmpty ? nil : bySkill
    }
}

// MARK: Days and timestamps

extension CCLocalDay {
    /// `yyyy-MM-dd`, the vault's day key.
    var key: String {
        String(format: "%04d-%02d-%02d", year, month, day)
    }

    /// `yyyy-MM`, the shard month.
    var monthKey: String {
        String(format: "%04d-%02d", year, month)
    }

    /// A `yyyy-MM-dd` key, nil when it isn't one.
    init?(key s: String) {
        let parts = s.split(separator: "-", omittingEmptySubsequences: false)
        guard parts.count == 3, parts[0].count == 4, parts[1].count == 2, parts[2].count == 2,
              let y = Int(parts[0]), let m = Int(parts[1]), let d = Int(parts[2]),
              (1...12).contains(m), (1...31).contains(d) else { return nil }
        self.init(year: y, month: m, day: d)
    }

    /// Days since 1970-01-01.
    var epochDay: Int { CCTimestamp.daysFromCivil(year, month, day) }

    /// The day `epochDay` days after 1970-01-01 (Howard Hinnant's inverse).
    init(epochDay z0: Int) {
        let z = z0 + 719_468
        let era = (z >= 0 ? z : z - 146_096) / 146_097
        let doe = z - era * 146_097
        let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let d = doy - (153 * mp + 2) / 5 + 1
        let m = mp < 10 ? mp + 3 : mp - 9
        let y = yoe + era * 400 + (m <= 2 ? 1 : 0)
        self.init(year: y, month: m, day: d)
    }

    func adding(days n: Int) -> CCLocalDay { CCLocalDay(epochDay: epochDay + n) }

    /// Monday-based weekday, Monday = 0 (1970-01-01 was a Thursday).
    var mondayIndex: Int { ((epochDay % 7) + 7 + 3) % 7 }

    /// The Monday starting this day's week.
    var weekStart: CCLocalDay { adding(days: -mondayIndex) }

    /// The first day of the next month.
    var nextMonthStart: CCLocalDay {
        month == 12 ? CCLocalDay(year: year + 1, month: 1, day: 1) : CCLocalDay(year: year, month: month + 1, day: 1)
    }
}

/// Timestamps as Windows writes them: `yyyy-MM-ddTHH:mm:ss.ffffff+00:00`, always UTC. Microsecond
/// rounding: Claude Code writes milliseconds, and a `Date` holds no finer time near today anyway.
enum VaultTime {
    static func iso(_ date: Date) -> String {
        let micros = Int64((date.timeIntervalSince1970 * 1_000_000).rounded())
        var secs = micros / 1_000_000
        var frac = micros % 1_000_000
        if frac < 0 {
            frac += 1_000_000
            secs -= 1
        }
        let days = secs >= 0 ? secs / 86_400 : (secs - 86_399) / 86_400
        let rem = secs - days * 86_400
        let day = CCLocalDay(epochDay: Int(days))
        return String(format: "%04d-%02d-%02dT%02d:%02d:%02d.%06lld+00:00",
                      day.year, day.month, day.day,
                      Int(rem / 3600), Int((rem % 3600) / 60), Int(rem % 60), frac)
    }

    /// Any form `CCTimestamp` reads (Claude Code's and .NET's).
    static func parse(_ s: String) -> Date? {
        s.isEmpty ? nil : CCTimestamp.parse(s)
    }

    /// .NET ticks between 0001-01-01 and 1970-01-01.
    static let unixEpochTicks: Int64 = 621_355_968_000_000_000

    /// A file time as .NET ticks (`mtime_ticks`).
    static func ticks(seconds: Int64, nanoseconds: Int64) -> Int64 {
        unixEpochTicks + seconds * 10_000_000 + nanoseconds / 100
    }

    static func date(ticks: Int64) -> Date {
        Date(timeIntervalSince1970: Double(ticks - unixEpochTicks) / 10_000_000)
    }
}

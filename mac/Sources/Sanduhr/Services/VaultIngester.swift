import Foundation

/// Counts for one ingest cycle. `acquired == false`: another writer held the lock and the
/// cycle skipped entirely.
struct VaultIngestResult: Equatable, Sendable {
    var acquired = true
    var filesSeen = 0
    var filesFullParsed = 0
    var filesTailParsed = 0
    var filesSkipped = 0
    var filesFailed = 0
    var rootsAborted = 0
    /// Folders whose writes were skipped because recording stopped during the cycle.
    var rootsWithdrawn = 0

    static let skipped = VaultIngestResult(acquired: false)

    mutating func add(_ r: VaultIngestResult) {
        filesSeen += r.filesSeen
        filesFullParsed += r.filesFullParsed
        filesTailParsed += r.filesTailParsed
        filesSkipped += r.filesSkipped
        filesFailed += r.filesFailed
        rootsAborted += r.rootsAborted
        rootsWithdrawn += r.rootsWithdrawn
    }
}

/// One recorded Claude Code folder: where its logs are, the vault directory name for it (its
/// `VaultFolderID`), and how its account names projects.
struct VaultRoot: Equatable, Hashable, Sendable {
    /// The Claude Code home, absolute (`~/.claude`, `~/.claude-work`, ...).
    let folder: String
    /// The vault directory name: never the path or the account label.
    let id: String
    let names: ProjectNamesChoice
}

/// The vault's only writer (item 46), a port of Windows `VaultIngester`. For each recorded
/// folder it walks `projects/<project>/**/*.jsonl`, parses session logs into month session
/// shards, folds rollups and advances checkpoints, in that order, checkpoints last: a crash at
/// any point leaves a stale checkpoint and the next cycle converges.
///
/// Single-threaded and synchronous (callers run it off the main thread); one writer across
/// processes through `VaultWriterLock`. Each file's size and modification time are taken before
/// it is opened. Live files are read from the checkpoint's offset after the 64 bytes before it
/// check out (the tail guard); a shrunk or rewritten file is read whole; a file quiet for an hour
/// gets one last whole read and is sealed. A torn last line is never consumed. A failed open
/// advances nothing.
///
/// **Project names** (the account's choice): Names stores `project_name` and
/// `project_key` = name + "~" + 8 hex of the cwd's hash, as Windows; Hidden stores
/// `VaultHiddenName.of(name)` in both and never the name or the cwd; Full paths also keeps
/// `cwd` (Windows `store_full_paths`). The name is the folded project (worktree and subfolder
/// folding, `CCProjectName`).
final class VaultIngester: @unchecked Sendable {
    static let coverageMarginDays = 25
    static let checkpointPruneDays = 7
    static let currentWalkVersion = 2
    static let tailGuardBytes = 64
    static let quiesceAfter: TimeInterval = 3600
    static let chunkSize = 1 << 20

    private let store: VaultStore
    private let writerVersion: String
    private let lockPath: String
    private let log: VaultLogSink?
    private let calendar: Calendar
    private let timeZone: TimeZone
    private let displayName: (String) -> String

    init(store: VaultStore, writerVersion: String, lockPath: String? = nil,
         timeZone: TimeZone = .current, log: VaultLogSink? = nil,
         dirHoldsGit: ((String) -> Bool)? = nil) {
        self.store = store
        self.writerVersion = writerVersion
        self.lockPath = lockPath ?? (store.vaultDir as NSString).appendingPathComponent(".writer.lock")
        self.log = log
        self.timeZone = timeZone
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = timeZone
        self.calendar = cal
        if let dirHoldsGit {
            displayName = { CCProjectName.displayName($0, dirHoldsGit: dirHoldsGit) }
        } else {
            displayName = { CCProjectName.displayName($0) }
        }
    }

    /// One cycle over `roots`.
    /// - Parameters:
    ///   - stillRecording: the erase guard (Windows `stillConsented`). The roots were chosen
    ///     before any erase flipped the account's activity, so each folder asks again right
    ///     before its writes; false means its parse work is dropped and nothing lands on disk.
    ///   - shouldStop: asked before each file; true abandons that folder's cycle at once with
    ///     nothing written (an erase cancelling an in-flight backfill).
    @discardableResult
    func ingestOnce(_ roots: [VaultRoot], now: Date,
                    stillRecording: ((String) -> Bool)? = nil,
                    shouldStop: ((String) -> Bool)? = nil) -> VaultIngestResult {
        guard let lock = VaultWriterLock.tryAcquire(lockPath) else {
            log?("ingest skipped (writer lock held)")
            return .skipped
        }
        defer { lock.release() }
        var total = VaultIngestResult()
        for root in roots {
            total.add(ingestRoot(root, now: now, stillRecording: stillRecording, shouldStop: shouldStop))
        }
        return total
    }

    // MARK: One folder

    private struct LogFile {
        let path: String
        let projectDir: String
        let length: Int64
        let mtimeTicks: Int64
    }

    private func ingestRoot(_ root: VaultRoot, now: Date, stillRecording: ((String) -> Bool)?,
                            shouldStop: ((String) -> Bool)?) -> VaultIngestResult {
        var r = VaultIngestResult()
        let id = root.id

        if (store.loadMeta(id)?.walkVersion ?? 0) < Self.currentWalkVersion {
            store.deleteCheckpoints(id)
        }

        var checkpoints = store.loadCheckpoints(id)
        var shards: [String: VaultSessionShard] = [:]
        var dirty = Set<String>()
        let nowIso = VaultTime.iso(now)

        for f in walk(root.folder) {
            if let shouldStop, shouldStop(id) {
                log?("folder cycle stopped (recording ended)")
                r.rootsWithdrawn = 1
                return r
            }
            r.filesSeen += 1
            let key = VaultStore.pathKey(f.path)
            let cp = checkpoints.entries[key]
            let uuid = ((f.path as NSString).lastPathComponent as NSString).deletingPathExtension
            let parent = Self.parentSession(of: f.path, projectDir: f.projectDir)
            let mtime = VaultTime.date(ticks: f.mtimeTicks)

            let unchanged = cp.map { $0.mtimeTicks == f.mtimeTicks && $0.length == f.length } ?? false
            let quiesced = now.timeIntervalSince(mtime) >= Self.quiesceAfter
            if unchanged, let cp, cp.sealed || !quiesced {
                checkpoints.entries[key]?.lastSeen = nowIso
                r.filesSkipped += 1
                continue
            }

            // (A) unchanged, quiet and unsealed: whole-file verify, then seal. (B) grown with a
            // good guard: tail read seeded from the stored rows. (C) anything else: whole file.
            let sealAfter = unchanged
            var seed: SessionAgg?
            if let cp, !unchanged, !cp.sealed, f.length > cp.length, cp.offset > 0, !cp.tailGuard.isEmpty {
                var corrupt = false
                seed = seedFromStored(id, uuid: uuid, months: cp.months, shards: &shards, now: now, corrupt: &corrupt)
                if corrupt {
                    r.rootsAborted = 1
                    return r
                }
                // Crash-vs-tail guard: stored rows newer than the checkpoint would double-count.
                if let s = seed,
                   s.byDay.values.reduce(0, { $0 + $1.total }) != cp.rowTotal || s.eventCount != cp.rowEvents
                    || s.cacheRead != cp.rowCacheRead || s.cacheCreation != cp.rowCacheCreation {
                    seed = nil
                }
            }

            let agg: SessionAgg
            let endOffset: Int64
            let guardHex: String
            var usedTail = false
            do {
                guard let h = FileHandle(forReadingAtPath: f.path) else { throw VaultIOError.open }
                defer { try? h.close() }
                let size = Int64(try h.seekToEnd())
                if let s = seed, let cp, size >= cp.offset, try guardAt(h, cp.offset) == cp.tailGuard {
                    agg = s
                    endOffset = try parseLines(h, from: cp.offset, into: agg)
                    usedTail = true
                } else {
                    agg = SessionAgg()
                    endOffset = try parseLines(h, from: 0, into: agg)
                }
                guardHex = try guardAt(h, endOffset)
            } catch {
                if cp != nil { checkpoints.entries[key]?.lastSeen = nowIso }
                log?(VaultLog.failure("file-open", error))
                r.filesFailed += 1
                continue
            }

            let newRows = buildRows(agg, names: root.names, parent: parent)
            var touched = Set(cp?.months ?? [])
            touched.formUnion(newRows.keys)
            for month in touched.sorted() {
                var corrupt = false
                var shard = shardFor(id, month, shards: &shards, now: now, corrupt: &corrupt)
                if corrupt {
                    r.rootsAborted = 1
                    return r
                }
                shard.sessions.removeValue(forKey: uuid)
                if let row = newRows[month] { shard.sessions[uuid] = row }
                shards[month] = shard
                dirty.insert(month)
            }

            var entry = VaultCheckpointEntry()
            entry.mtimeTicks = f.mtimeTicks
            entry.length = f.length
            entry.offset = endOffset
            entry.tailGuard = guardHex
            entry.rowTotal = newRows.values.reduce(0) { $0 + $1.total }
            entry.rowEvents = newRows.values.reduce(0) { $0 + $1.eventCount }
            entry.rowCacheRead = newRows.values.reduce(0) { $0 + ($1.cacheTokens?.read ?? 0) }
            entry.rowCacheCreation = newRows.values.reduce(0) { $0 + ($1.cacheTokens?.creation ?? 0) }
            entry.months = newRows.keys.sorted()
            entry.sealed = sealAfter
            entry.lastSeen = nowIso
            checkpoints.entries[key] = entry
            if usedTail { r.filesTailParsed += 1 } else { r.filesFullParsed += 1 }
        }

        // Bookkeeping for files gone from the walk for over 7 days goes (a returning file just
        // re-ingests). An unparseable stamp counts as stale.
        let pruneBefore = now.addingTimeInterval(-Double(Self.checkpointPruneDays) * 86_400)
        for (k, e) in checkpoints.entries {
            if let seen = VaultTime.parse(e.lastSeen), seen >= pruneBefore { continue }
            checkpoints.entries.removeValue(forKey: k)
        }

        // The erase guard, right before the first write.
        if let stillRecording, !stillRecording(id) {
            log?("folder writes skipped (recording ended)")
            r.rootsWithdrawn = 1
            return r
        }

        // Write order (binding): session shards, rollups, checkpoints, meta.
        do {
            for month in dirty.sorted() {
                if let shard = shards[month] { try store.saveSessionShard(id, month, shard) }
            }
        } catch {
            log?(VaultLog.failure("shard-write", error))
            r.rootsAborted = 1
            return r
        }

        var rollupMonths = dirty
        let currentMonth = CCLocalDay(now, calendar: calendar).monthKey
        if store.listSessionShardMonths(id).contains(currentMonth) { rollupMonths.insert(currentMonth) }
        for month in rollupMonths.sorted() { rebuildRollups(id, month, shards: shards) }

        store.saveCheckpoints(id, checkpoints)
        updateMeta(id, now: now, nowIso: nowIso)
        return r
    }

    /// Every session log under `<folder>/projects/<project>/`, nested ones included, oldest
    /// first, with the size and modification time taken now (before any open). Files lying
    /// directly in `projects` are not sessions.
    private func walk(_ folder: String) -> [LogFile] {
        let projects = (folder as NSString).appendingPathComponent("projects")
        let fm = FileManager.default
        guard let dirs = try? fm.contentsOfDirectory(atPath: projects) else { return [] }
        var files: [LogFile] = []
        for name in dirs.sorted() {
            let projectDir = (projects as NSString).appendingPathComponent(name)
            var isDir: ObjCBool = false
            guard fm.fileExists(atPath: projectDir, isDirectory: &isDir), isDir.boolValue,
                  let walker = fm.enumerator(atPath: projectDir) else { continue }
            while let rel = walker.nextObject() as? String {
                guard rel.hasSuffix(".jsonl") else { continue }
                let full = (projectDir as NSString).appendingPathComponent(rel)
                var st = stat()
                guard stat(full, &st) == 0, (st.st_mode & S_IFMT) == S_IFREG else { continue }
                files.append(LogFile(path: full, projectDir: projectDir, length: Int64(st.st_size),
                                     mtimeTicks: VaultTime.ticks(seconds: Int64(st.st_mtimespec.tv_sec),
                                                                 nanoseconds: Int64(st.st_mtimespec.tv_nsec))))
            }
        }
        files.sort { ($0.mtimeTicks, $0.path) < ($1.mtimeTicks, $1.path) }
        return files
    }

    /// The parent session of a nested transcript (`<projectDir>/<uuid>/.../x.jsonl`): the first
    /// segment when it is a uuid, else nil. Main transcripts sit directly in the project folder.
    static func parentSession(of path: String, projectDir: String) -> String? {
        let prefix = projectDir.hasSuffix("/") ? projectDir : projectDir + "/"
        guard path.hasPrefix(prefix) else { return nil }
        let parts = path.dropFirst(prefix.count).split(separator: "/")
        guard parts.count > 1 else { return nil }
        let first = String(parts[0])
        return isGuid(first) ? first : nil
    }

    /// The forms .NET's `Guid.TryParse` reads that Claude Code could produce: `D`, `N`, `B`, `P`.
    static func isGuid(_ s: String) -> Bool {
        var t = s
        if (t.hasPrefix("{") && t.hasSuffix("}")) || (t.hasPrefix("(") && t.hasSuffix(")")) {
            t = String(t.dropFirst().dropLast())
        }
        if UUID(uuidString: t) != nil { return true }
        return t.count == 32 && t.allSatisfy(\.isHexDigit)
    }

    // MARK: Shards, seeds, rollups, meta

    private func shardFor(_ id: String, _ month: String, shards: inout [String: VaultSessionShard],
                          now: Date, corrupt: inout Bool) -> VaultSessionShard {
        if let cached = shards[month] { return cached }
        var (result, shard) = store.loadSessionShard(id, month)
        if result == .corrupt {
            store.quarantineSessionShard(id, month, now: now)
            log?("session shard quarantined; checkpoints invalidated")
            corrupt = true
            return shard
        }
        shard.schemaVersion = VaultSchema.currentSchemaVersion
        shard.writerVersion = writerVersion
        shards[month] = shard
        return shard
    }

    /// The parse state rebuilt from a session's stored rows, so a tail read folds into a copy of
    /// them. Nil when no rows exist (the caller reads the whole file).
    private func seedFromStored(_ id: String, uuid: String, months: [String],
                                shards: inout [String: VaultSessionShard], now: Date,
                                corrupt: inout Bool) -> SessionAgg? {
        var rows: [VaultSessionRow] = []
        for month in months {
            let shard = shardFor(id, month, shards: &shards, now: now, corrupt: &corrupt)
            if corrupt { return nil }
            if let row = shard.sessions[uuid] { rows.append(row) }
        }
        guard !rows.isEmpty else { return nil }
        let merged = VaultRowMath.merge(rows)
        let agg = SessionAgg()
        agg.firstTs = VaultTime.parse(merged.firstTs)
        agg.lastTs = VaultTime.parse(merged.lastTs)
        agg.eventCount = merged.eventCount
        agg.skippedLines = merged.skippedLines
        agg.cwd = merged.cwd
        agg.cacheRead = merged.cacheTokens?.read ?? 0
        agg.cacheCreation = merged.cacheTokens?.creation ?? 0
        agg.seedProjectKey = merged.projectKey
        agg.seedProjectName = merged.projectName
        for (day, bucket) in merged.byDay {
            agg.byDay[day] = DayAgg(total: bucket.total, input: bucket.input, output: bucket.output,
                                    byModel: bucket.byModel, bySkill: bucket.bySkill ?? [:])
        }
        return agg
    }

    /// The month's rollups from its session shard: the only rollup writer, never a patch.
    private func rebuildRollups(_ id: String, _ month: String, shards: [String: VaultSessionShard]) {
        let shard: VaultSessionShard
        if let cached = shards[month] {
            shard = cached
        } else {
            let (result, loaded) = store.loadSessionShard(id, month)
            guard result == .ok else {
                store.deleteRollupShard(id, month)
                return
            }
            shard = loaded
        }
        store.saveRollupShard(id, month, VaultRollups.fold(shard))
    }

    private func updateMeta(_ id: String, now: Date, nowIso: String) {
        var meta = store.loadMeta(id) ?? VaultRootMeta()
        let today = CCLocalDay(now, calendar: calendar)
        if meta.since.isEmpty { meta.since = today.key }
        meta.covered = Self.mergeCoverage(meta.covered, from: today.adding(days: -Self.coverageMarginDays), to: today)
        meta.lastIngestTs = nowIso
        meta.walkVersion = Self.currentWalkVersion
        store.saveMeta(id, meta)
    }

    /// `[from, to]` merged into a sorted list of disjoint ranges (touching ranges join).
    static func mergeCoverage(_ ranges: [VaultDateRange], from: CCLocalDay, to: CCLocalDay) -> [VaultDateRange] {
        var parsed: [(from: CCLocalDay, to: CCLocalDay)] = ranges.compactMap { r in
            guard let f = CCLocalDay(key: r.from), let t = CCLocalDay(key: r.to) else { return nil }
            return (f, t)
        }
        parsed.append((from, to))
        parsed.sort { $0.from < $1.from }
        var merged: [(from: CCLocalDay, to: CCLocalDay)] = []
        for r in parsed {
            if let last = merged.last, r.from <= last.to.adding(days: 1) {
                merged[merged.count - 1] = (last.from, max(r.to, last.to))
            } else {
                merged.append(r)
            }
        }
        return merged.map { VaultDateRange(from: $0.from.key, to: $0.to.key) }
    }

    // MARK: Parsing

    private struct DayAgg {
        var total: Int64 = 0
        var input: Int64 = 0
        var output: Int64 = 0
        var byModel: [String: Int64] = [:]
        var bySkill: [String: Int64] = [:]
    }

    private final class SessionAgg {
        var firstTs: Date?
        var lastTs: Date?
        var eventCount: Int64 = 0
        var skippedLines: Int64 = 0
        var cwd: String?
        var cacheRead: Int64 = 0
        var cacheCreation: Int64 = 0
        var seedProjectKey: String?
        var seedProjectName: String?
        var byDay: [String: DayAgg] = [:]
    }

    enum VaultIOError: Error {
        case open
    }

    /// Parses whole lines from `start`; returns the offset after the last newline consumed. A
    /// torn last line (no newline yet) is not consumed: the next cycle reads it again, so a line
    /// finished after this checkpoint is counted once, never lost.
    private func parseLines(_ h: FileHandle, from start: Int64, into agg: SessionAgg) throws -> Int64 {
        try h.seek(toOffset: UInt64(start))
        var pending = Data()
        var pos = start
        var consumed = start
        while true {
            guard let chunk = try h.read(upToCount: Self.chunkSize), !chunk.isEmpty else { break }
            chunk.withUnsafeBytes { (raw: UnsafeRawBufferPointer) in
                guard let base = raw.baseAddress else { return }
                let n = raw.count
                var lineStart = 0
                while lineStart < n,
                      let hit = memchr(base + lineStart, 0x0A, n - lineStart) {
                    let i = base.distance(to: UnsafeRawPointer(hit))
                    if pending.isEmpty {
                        processLine(UnsafeRawBufferPointer(rebasing: raw[lineStart..<i]), agg)
                    } else {
                        pending.append(base.advanced(by: lineStart).assumingMemoryBound(to: UInt8.self),
                                       count: i - lineStart)
                        pending.withUnsafeBytes { processLine($0, agg) }
                        pending.removeAll(keepingCapacity: true)
                    }
                    consumed = pos + Int64(i) + 1
                    lineStart = i + 1
                }
                if lineStart < n {
                    pending.append(base.advanced(by: lineStart).assumingMemoryBound(to: UInt8.self),
                                   count: n - lineStart)
                }
            }
            pos += Int64(chunk.count)
        }
        return consumed
    }

    private static let assistantMark = Array("\"assistant\"".utf8)

    private func processLine(_ raw: UnsafeRawBufferPointer, _ agg: SessionAgg) {
        guard let base = raw.baseAddress else { return }
        var lo = 0, hi = raw.count
        while lo < hi, Self.isSpace(raw[lo]) { lo += 1 }
        while hi > lo, Self.isSpace(raw[hi - 1]) { hi -= 1 }
        guard hi > lo else { return }
        // A speed filter only, looser than "type":"assistant" on purpose so formatting drift
        // never drops events; false positives are parsed and rejected below.
        let found = Self.assistantMark.withUnsafeBytes { mark in
            memmem(base + lo, hi - lo, mark.baseAddress, mark.count) != nil
        }
        guard found else { return }
        let data = Data(bytes: base + lo, count: hi - lo)
        guard let any = try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed]) else {
            agg.skippedLines += 1
            return
        }
        guard let d = any as? [String: Any] else {
            agg.skippedLines += 1
            return
        }
        guard d["type"] as? String == "assistant",
              let msg = d["message"] as? [String: Any],
              let usage = msg["usage"] as? [String: Any],
              let ts = (d["timestamp"] as? String).flatMap(VaultTime.parse) else { return }

        agg.cacheRead += CCLogReader.number(usage["cache_read_input_tokens"])
        agg.cacheCreation += CCLogReader.number(usage["cache_creation_input_tokens"])

        let input = CCLogReader.number(usage["input_tokens"])
        let output = CCLogReader.number(usage["output_tokens"])
        let tokens = input + output
        guard tokens > 0 else { return }

        if agg.cwd == nil { agg.cwd = d["cwd"] as? String }
        var model = msg["model"] as? String ?? ""
        if model.isEmpty { model = "<none>" }   // conservation: sum(by_model) == total
        let skill = d["attributionSkill"] as? String

        if agg.firstTs.map({ ts < $0 }) ?? true { agg.firstTs = ts }
        if agg.lastTs.map({ ts > $0 }) ?? true { agg.lastTs = ts }
        agg.eventCount += 1

        let day = CCLocalDay(ts, calendar: calendar).key
        var bucket = agg.byDay[day] ?? DayAgg()
        bucket.total += tokens
        bucket.input += input
        bucket.output += output
        bucket.byModel[model, default: 0] += tokens
        if let skill, !skill.isEmpty { bucket.bySkill[skill, default: 0] += tokens }
        agg.byDay[day] = bucket
    }

    private static func isSpace(_ b: UInt8) -> Bool {
        b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0B || b == 0x0C
    }

    /// Month rows for a parsed session; none when it counted no events.
    private func buildRows(_ agg: SessionAgg, names: ProjectNamesChoice, parent: String?) -> [String: VaultSessionRow] {
        guard agg.eventCount > 0, let first = agg.firstTs, let last = agg.lastTs else { return [:] }
        var key: String
        var name: String
        if let cwd = agg.cwd {
            name = displayName(cwd)
            key = "\(name)~\(VaultStore.pathKey(cwd).prefix(8))"
            if names == .hidden {
                name = VaultHiddenName.of(name)
                key = name
            }
        } else if let seedKey = agg.seedProjectKey {
            key = seedKey
            name = agg.seedProjectName ?? "(none)"
            // A row first kept with names, now Hidden: hide the name it already had.
            if names == .hidden, name != "(none)", !VaultHiddenName.isHidden(name) {
                name = VaultHiddenName.of(name)
                key = name
            }
        } else {
            key = "(none)"
            name = "(none)"
        }

        var merged = VaultSessionRow()
        merged.projectKey = key
        merged.projectName = name
        merged.cwd = names == .full ? agg.cwd : nil
        merged.parentSession = parent
        merged.firstTs = VaultTime.iso(first)
        merged.lastTs = VaultTime.iso(last)
        merged.utcOffsetMin = timeZone.secondsFromGMT(for: first) / 60
        merged.eventCount = agg.eventCount
        merged.skippedLines = agg.skippedLines
        merged.cacheTokens = VaultCacheTokens(read: agg.cacheRead, creation: agg.cacheCreation)
        for (day, d) in agg.byDay {
            merged.byDay[day] = VaultDayBucket(total: d.total, input: d.input, output: d.output,
                                               byModel: d.byModel, bySkill: d.bySkill.isEmpty ? nil : d.bySkill)
        }
        return VaultRowMath.splitByMonth(merged)
    }

    /// Lowercase hex SHA-256 of the up to 64 bytes ending at `offset`: the tail guard.
    private func guardAt(_ h: FileHandle, _ offset: Int64) throws -> String {
        let len = Int(min(Int64(Self.tailGuardBytes), offset))
        guard len > 0 else { return "" }
        try h.seek(toOffset: UInt64(offset - Int64(len)))
        let bytes = try h.read(upToCount: len) ?? Data()
        return VaultStore.sha256Hex(bytes)
    }
}

// MARK: Rollup fold

enum VaultRollups {
    /// Per local day: total, input/output, by model, by `project_key`, by skill, and the count
    /// of distinct logical sessions (`parent_session ?? uuid`).
    static func fold(_ shard: VaultSessionShard) -> VaultRollupShard {
        var days: [String: VaultRollupDay] = [:]
        var sessionsPerDay: [String: Set<String>] = [:]
        for (uuid, row) in shard.sessions {
            let logical = row.parentSession ?? uuid
            for (day, bucket) in row.byDay {
                var d = days[day] ?? VaultRollupDay()
                d.total += bucket.total
                d.input += bucket.input
                d.output += bucket.output
                for (m, v) in bucket.byModel { d.byModel[m, default: 0] += v }
                d.byProject[row.projectKey, default: 0] += bucket.total
                if let skills = bucket.bySkill {
                    for (s, v) in skills { d.bySkill[s, default: 0] += v }
                }
                days[day] = d
                sessionsPerDay[day, default: []].insert(logical)
            }
        }
        for (day, set) in sessionsPerDay { days[day]?.sessions = set.count }
        return VaultRollupShard(schemaVersion: VaultSchema.currentSchemaVersion, days: days)
    }
}

// MARK: Hidden project names

/// Hidden project names: `p-` and the first 10 hex digits of the SHA-256 of the folded project
/// name (the name Names would store, after worktree and subfolder folding). Stable, so the
/// record still groups by project, and the same on every Mac. A hash, not encryption: someone
/// holding the vault who guesses a name can confirm the guess. The cwd is never stored.
enum VaultHiddenName {
    static func of(_ name: String) -> String {
        "p-" + VaultStore.sha256Hex(name).prefix(10)
    }

    static func isHidden(_ name: String) -> Bool {
        name.count == 12 && name.hasPrefix("p-")
            && name.dropFirst(2).allSatisfy { $0.isHexDigit && !$0.isUppercase }
    }
}

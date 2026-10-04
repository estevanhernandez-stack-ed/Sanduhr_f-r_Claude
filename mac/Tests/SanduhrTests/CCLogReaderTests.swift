import Foundation
import Testing
@testable import Sanduhr

/// Session logs written into made-up Claude Code homes under the temp directory, and the
/// synthetic fixtures in `Fixtures/cc-logs` (the Windows tests' inputs). Nothing here reads a
/// real ~/.claude* folder.
enum CCLogs {
    /// One assistant line as Claude Code writes it, model and cwd optional.
    static func event(_ ts: String, _ model: String?, _ inTok: Int64, _ outTok: Int64,
                      cwd: String? = nil, skill: String? = nil) -> String {
        var message: [String: Any] = ["usage": ["input_tokens": inTok, "output_tokens": outTok]]
        if let model { message["model"] = model }
        var ev: [String: Any] = ["type": "assistant", "timestamp": ts, "message": message]
        if let cwd { ev["cwd"] = cwd }
        if let skill { ev["attributionSkill"] = skill }
        let data = (try? JSONSerialization.data(withJSONObject: ev, options: [.sortedKeys])) ?? Data()
        return String(decoding: data, as: UTF8.self)
    }

    static func iso(_ d: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.string(from: d)
    }

    /// `<home>/<root>/projects/<project>/<session>.jsonl`, each line ending in a newline.
    @discardableResult
    static func write(_ h: TempHome, root: String = ".claude", project: String = "P",
                      session: String = "s", _ lines: [String]) -> String {
        h.dir("\(root)/projects/\(project)")
        let rel = "\(root)/projects/\(project)/\(session).jsonl"
        h.file(rel, lines.map { $0 + "\n" }.joined())
        return h.at(rel)
    }

    /// A ported fixture's text.
    static func fixture(_ name: String) -> String {
        guard let url = Bundle.module.url(forResource: name, withExtension: "jsonl",
                                          subdirectory: "Fixtures/cc-logs"),
              let text = try? String(contentsOf: url, encoding: .utf8) else { return "" }
        return text
    }

    /// A ported fixture as a session in a home's `.claude`.
    @discardableResult
    static func install(_ name: String, in h: TempHome, project: String = "P", session: String = "s") -> String {
        h.dir(".claude/projects/\(project)")
        let rel = ".claude/projects/\(project)/\(session).jsonl"
        h.file(rel, fixture(name))
        return h.at(rel)
    }

    static func reader(_ h: TempHome, root: String = ".claude",
                       fileSystem: CCLogFileSystem = LocalCCLogFileSystem()) -> CCLogReader {
        CCLogReader(root: h.at(root), fileSystem: fileSystem)
    }

    /// 2026-05-05 10:00 UTC, the Windows tests' base time.
    static let base = CCTimestamp.parse("2026-05-05T10:00:00Z")!

    static func setModified(_ path: String, _ date: Date) {
        try? FileManager.default.setAttributes([.modificationDate: date], ofItemAtPath: path)
    }

    static func append(_ path: String, _ text: String) {
        guard let h = FileHandle(forWritingAtPath: path) else { return }
        h.seekToEndOfFile()
        h.write(Data(text.utf8))
        h.closeFile()
    }
}

/// Every file-system call, so tests can say what was (and wasn't) opened.
final class SpyCCFileSystem: CCLogFileSystem, @unchecked Sendable {
    private let base = LocalCCLogFileSystem()
    private let lock = NSLock()
    private var _calls: [String] = []
    private var _reads: [(path: String, offset: UInt64)] = []

    var calls: [String] { lock.lock(); defer { lock.unlock() }; return _calls }
    var reads: [(path: String, offset: UInt64)] { lock.lock(); defer { lock.unlock() }; return _reads }

    private func note(_ s: String) { lock.lock(); _calls.append(s); lock.unlock() }

    func isDirectory(_ path: String) -> Bool { note("isDirectory"); return base.isDirectory(path) }
    func subdirectories(of path: String) -> [String] { note("subdirectories"); return base.subdirectories(of: path) }
    func jsonlFiles(under path: String) -> [String] { note("jsonlFiles"); return base.jsonlFiles(under: path) }
    func stat(_ path: String) -> CCLogFileStat? { note("stat"); return base.stat(path) }
    func read(_ path: String, from offset: UInt64) -> Data? {
        lock.lock()
        _calls.append("read")
        _reads.append((path, offset))
        lock.unlock()
        return base.read(path, from: offset)
    }
}

@Suite("Claude Code log reader, Windows parity")
struct CCLogReaderParityTests {
    // Windows Discover_log_files_handles_missing_projects_dir
    @Test func missingProjectsFolderFindsNothing() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude")
        #expect(CCLogs.reader(h).discoverLogFiles().isEmpty)
        #expect(CCLogs.reader(h).tokensSince(CCLogs.base).isEmpty)
    }

    // Windows Discover_log_files_finds_session_jsonl and DiscoverLogFiles_finds_nested_subagent_transcripts
    @Test func findsSessionsAndNestedTranscripts() {
        let h = TempHome()
        defer { h.cleanUp() }
        let main = CCLogs.write(h, project: "c--x-api", session: "main", [])
        h.dir(".claude/projects/c--x-api/11111111-2222-3333-4444-555555555555/subagents")
        h.file(".claude/projects/c--x-api/11111111-2222-3333-4444-555555555555/subagents/agent-abc.jsonl", "")
        h.file(".claude/projects/loose.jsonl", "")            // not inside a project folder
        h.file(".claude/projects/c--x-api/notes.txt", "")     // not a log
        let files = CCLogs.reader(h).discoverLogFiles()
        #expect(files.count == 2)
        #expect(files.contains(main))
        #expect(files.contains { $0.hasSuffix("subagents/agent-abc.jsonl") })
    }

    // Windows Iter_usage_events_yields_only_assistant
    @Test func yieldsOnlyAssistantEvents() {
        let h = TempHome()
        defer { h.cleanUp() }
        let f = CCLogs.install("assistant-only", in: h)
        let events = CCLogs.reader(h).usageEvents(in: f)
        #expect(events.count == 1)
        let ev = events[0]
        #expect(ev.model == "claude-opus-4-7")
        #expect(ev.inputTokens == 100)
        #expect(ev.outputTokens == 200)
        #expect(ev.cwd == "C:/proj/Sanduhr")
        #expect(ev.skill == "my-skill")
        #expect(ev.timestamp == CCTimestamp.parse("2026-05-05T10:01:00Z"))
    }

    // Windows Iter_usage_events_skips_malformed_lines
    @Test func skipsMalformedLines() {
        let h = TempHome()
        defer { h.cleanUp() }
        let f = CCLogs.install("malformed", in: h)
        #expect(CCLogs.reader(h).usageEvents(in: f).count == 1)
    }

    // Windows Tokens_since_filters_by_cutoff
    @Test func tokensSinceFiltersByCutoff() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.install("tokens-since", in: h)
        let totals = CCLogs.reader(h).tokensSince(CCLogs.base)
        #expect(totals == ["claude-opus-4-7": 100, "claude-sonnet-4-6": 100])
    }

    // Windows Tokens_since_aggregates_across_sessions (two sessions in one home here: the Mac
    // reads one linked home)
    @Test func tokensSinceAddsUpSessions() {
        let h = TempHome()
        defer { h.cleanUp() }
        let b = CCLogs.base
        CCLogs.write(h, project: "P", session: "s1",
                     [CCLogs.event(CCLogs.iso(b.addingTimeInterval(60)), "claude-opus-4-7", 100, 200)])
        CCLogs.write(h, project: "Q", session: "s2",
                     [CCLogs.event(CCLogs.iso(b.addingTimeInterval(120)), "claude-opus-4-7", 50, 150)])
        #expect(CCLogs.reader(h).tokensSince(b) == ["claude-opus-4-7": 500])
    }

    // Windows Tokens_since_skips_zero_token_events
    @Test func skipsZeroTokenEvents() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.install("zero-tokens", in: h)
        #expect(CCLogs.reader(h).tokensSince(CCLogs.base).isEmpty)
    }

    // Windows Model_to_tier_key_maps_known_prefixes and _returns_none_for_unknown
    @Test func mapsModelsToTiers() {
        #expect(CCLogReader.tierForModel("claude-opus-4-7") == "seven_day_opus")
        #expect(CCLogReader.tierForModel("claude-opus-4-6") == "seven_day_opus")
        #expect(CCLogReader.tierForModel("claude-sonnet-4-6") == "seven_day_sonnet")
        #expect(CCLogReader.tierForModel("claude-fable-5") == "seven_day_fable")
        #expect(CCLogReader.tierForModel("claude-haiku-4-5") == "seven_day")
        #expect(CCLogReader.tierForModel("gpt-4") == nil)
        #expect(CCLogReader.tierForModel(nil) == nil)
        #expect(CCLogReader.tierForModel("") == nil)
    }

    // Windows Tokens_since_by_tier_groups_by_sanduhr_tier
    @Test func tokensSinceByTier() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.install("by-tier", in: h)
        let r = CCLogs.reader(h)
        #expect(r.tokensSinceByTier(CCLogs.base) == ["seven_day_opus": 300, "seven_day_sonnet": 100])
        // The unmapped model still counts in the total the badge pass keeps.
        let burn = r.burnSince(CCLogs.base)
        #expect(burn.total == Int64(2398))   // 300 + 100, and 1,998 unmapped
        #expect(burn.events == 3)
    }

    // Windows Tokens_by_day_groups_by_local_date
    @Test func tokensByDayGroupsByLocalDate() {
        let h = TempHome()
        defer { h.cleanUp() }
        let now = Date()
        let d1 = now.addingTimeInterval(-2 * 86_400)
        let d2 = now.addingTimeInterval(-86_400)
        CCLogs.write(h, [
            CCLogs.event(CCLogs.iso(d1), "claude-opus-4-7", 50, 50),
            CCLogs.event(CCLogs.iso(d1.addingTimeInterval(5 * 3600)), "claude-opus-4-7", 100, 100),
            CCLogs.event(CCLogs.iso(d2), "claude-opus-4-7", 75, 25),
            CCLogs.event(CCLogs.iso(now), "claude-opus-4-7", 200, 300),
        ])
        let byDay = CCLogs.reader(h).tokensByDay(daysBack: 30, now: now.addingTimeInterval(1))
        #expect(byDay.values.reduce(0, +) == 900)
        #expect((1...3).contains(byDay.count))
    }

    // Windows Tokens_by_day_drops_events_outside_window
    @Test func tokensByDayDropsOldEvents() {
        let h = TempHome()
        defer { h.cleanUp() }
        let now = Date()
        CCLogs.write(h, [
            CCLogs.event(CCLogs.iso(now.addingTimeInterval(-5 * 86_400)), "claude-opus-4-7", 100, 100),
            CCLogs.event(CCLogs.iso(now.addingTimeInterval(-60 * 86_400)), "claude-opus-4-7", 999, 999),
        ])
        #expect(CCLogs.reader(h).tokensByDay(daysBack: 30, now: now).values.reduce(0, +) == 200)
    }

    // Windows Tokens_by_project_groups_by_cwd
    @Test func tokensByProjectGroupsByCwd() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.install("by-project", in: h)
        let byProject = CCLogs.reader(h).tokensByProject(since: CCLogs.base)
        #expect(byProject == ["C:/Users/dev/Projects/Sanduhr": 300, "C:/Users/dev/Projects/Other": 100])
    }

    // Windows Tokens_by_skill_groups_by_attribution
    @Test func tokensBySkillGroupsByAttribution() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.install("by-skill", in: h)
        let bySkill = CCLogs.reader(h).tokensBySkill(since: CCLogs.base)
        #expect(bySkill == ["superpowers:debugging": 300, "vibe-doc:scan": 100])
    }
}

@Suite("Claude Code project names, Windows parity")
struct CCProjectNameTests {
    static let noRepos: (String) -> Bool = { _ in false }

    static func repos(_ dirs: String...) -> (String) -> Bool {
        let norm: (String) -> String = { d in
            var s = d.replacingOccurrences(of: "\\", with: "/")
            while s.hasSuffix("/") { s.removeLast() }
            return s.lowercased()
        }
        let set = Set(dirs.map(norm))
        return { set.contains(norm($0)) }
    }

    // Windows Project_display_name_extracts_basename
    @Test func basename() {
        #expect(CCProjectName.displayName("C:\\Users\\estev\\Projects\\Sanduhr", dirHoldsGit: Self.noRepos) == "Sanduhr")
        #expect(CCProjectName.displayName("C:/Users/estev/Projects/Sanduhr", dirHoldsGit: Self.noRepos) == "Sanduhr")
        #expect(CCProjectName.displayName("/home/dev/work/foo", dirHoldsGit: Self.noRepos) == "foo")
        #expect(CCProjectName.displayName("/home/dev/work/foo/", dirHoldsGit: Self.noRepos) == "foo")
        #expect(CCProjectName.displayName("") == "")
        #expect(CCProjectName.displayName("", dirHoldsGit: Self.noRepos) == "")
    }

    // Windows ProjectDisplayName_rolls_worktrees_up_to_the_repo
    @Test(arguments: [
        ("C:\\Users\\estev\\Projects\\Project-626Labs-1\\.claude\\worktrees\\agent-ada8d4609379f018f", "Project-626Labs-1"),
        ("C:/Users/estev/Projects/Project-626Labs-1/.claude/worktrees/agent-a80669ed385edf167/functions", "Project-626Labs-1"),
        ("/home/dev/work/foo/.worktrees/feature-x", "foo"),
        ("/home/dev/work/foo/.worktrees/feature-x/src/deep", "foo"),
        ("C:/Users/estev/Projects/Sanduhr/.claude/worktrees/a/.worktrees/b", "Sanduhr"),
        ("C:/Users/estev/Projects/Sanduhr/.claude/worktrees", "worktrees"),
        ("C:/Users/estev/Projects/Sanduhr/.claude", ".claude"),
        ("C:/Users/estev/Projects/worktrees/x", "x"),
    ])
    func worktreesRollUp(_ cwd: String, _ expected: String) {
        #expect(CCProjectName.displayName(cwd, dirHoldsGit: Self.noRepos) == expected)
    }

    // Windows ProjectDisplayName_rolls_a_subfolder_up_to_its_repo
    @Test(arguments: [
        "C:/Users/estev/Projects/Project-626Labs-1/functions",
        "C:\\Users\\estev\\Projects\\Project-626Labs-1\\functions",
        "C:/Users/estev/Projects/Project-626Labs-1/functions/src/domains",
        "C:/Users/estev/Projects/Project-626Labs-1",
        "C:/Users/estev/Projects/Project-626Labs-1/",
    ])
    func subfolderRollsUpToRepo(_ cwd: String) {
        let repos = Self.repos("C:/Users/estev/Projects/Project-626Labs-1")
        #expect(CCProjectName.displayName(cwd, dirHoldsGit: repos) == "Project-626Labs-1")
    }

    // Windows ProjectDisplayName_gitRoot_rules_at_the_edges
    @Test func gitRootEdges() {
        let nested = Self.repos("/w/super", "/w/super/vendor/sub")
        #expect(CCProjectName.displayName("/w/super/vendor/sub/src", dirHoldsGit: nested) == "sub")
        #expect(CCProjectName.displayName("/w/super/docs", dirHoldsGit: nested) == "super")
        #expect(CCProjectName.displayName("/w/scratch", dirHoldsGit: nested) == "scratch")
        #expect(CCProjectName.displayName("/tmp/notes", dirHoldsGit: nested) == "notes")
        let worktreeIsRepo = Self.repos("/w/foo/.worktrees/feature-x", "/w/foo")
        #expect(CCProjectName.displayName("/w/foo/.worktrees/feature-x/src", dirHoldsGit: worktreeIsRepo) == "foo")
        #expect(CCProjectName.displayName("/w", dirHoldsGit: Self.repos("/w", "/")) == "w")
    }

    // Windows ProjectDisplayName_finds_a_real_git_directory_on_disk
    @Test func findsARealGitEntryOnDisk() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir("my-repo/functions/src")
        h.dir("my-repo/.git")
        #expect(CCProjectName.displayName(h.at("my-repo/functions/src")) == "my-repo")
        h.dir("linked-repo/pkg")
        h.file("linked-repo/.git", "gitdir: /elsewhere/.git/worktrees/x")
        #expect(CCProjectName.displayName(h.at("linked-repo/pkg")) == "linked-repo")
        h.dir("loose/inner")
        #expect(CCProjectName.displayName(h.at("loose/inner")) == "inner")
    }
}

@Suite("Claude Code burn for a local day, Windows parity")
struct CCBurnForDayTests {
    static let day = CCLocalDay(year: 2026, month: 9, day: 12)

    /// A local wall-clock hour on `day`, as a UTC timestamp, so the day filter holds in any zone.
    static func localIso(_ day: CCLocalDay, _ hour: Int, plusDays: Int = 0) -> String {
        let cal = Calendar.current
        let start = day.start(in: cal)!
        let d = cal.date(byAdding: .day, value: plusDays, to: start)!
        return CCLogs.iso(cal.date(bySettingHour: hour, minute: 0, second: 0, of: d)!)
    }

    // Windows Counts_only_the_requested_local_day_and_only_the_given_root
    @Test func countsOnlyThatDayAndThatHome() {
        let h = TempHome()
        defer { h.cleanUp() }
        let day = Self.day
        let cwd = "C:\\Users\\estev\\Projects\\Sanduhr"
        CCLogs.write(h, root: ".claude-personal", project: "c--sanduhr", [
            CCLogs.event(Self.localIso(day, 10), "claude-fable-5", 100, 200, cwd: cwd),
            CCLogs.event(Self.localIso(day, 23), "claude-sonnet-5", 10, 20, cwd: cwd),
            CCLogs.event(Self.localIso(day, 12, plusDays: -1), "claude-fable-5", 999, 0, cwd: cwd),
            CCLogs.event(Self.localIso(day, 1, plusDays: 1), "claude-fable-5", 777, 0, cwd: cwd),
        ])
        CCLogs.write(h, root: ".claude", project: "c--wbp", [
            CCLogs.event(Self.localIso(day, 12), "claude-opus-4", 500, 500, cwd: "C:\\Users\\estev\\Projects\\Other\\wbp"),
        ])
        let burn = CCLogs.reader(h, root: ".claude-personal")
            .burnForLocalDay(day, dirHoldsGit: CCProjectNameTests.noRepos)
        #expect(burn.total == 330)
        #expect(burn.byProject["Sanduhr"] == 330)
        #expect(burn.byProject["wbp"] == nil)
        #expect(burn.byTier["seven_day_fable"] == 300)
        #expect(burn.byTier["seven_day_sonnet"] == 30)
    }

    // Windows Unknown_cwd_and_unmapped_models_stay_in_the_total
    @Test func unknownCwdAndUnmappedModelsStayInTheTotal() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.write(h, root: ".claude-personal", project: "c--x", [
            CCLogs.event(Self.localIso(Self.day, 9), "mystery-model", 40, 2),
            CCLogs.event(Self.localIso(Self.day, 9), nil, 0, 0, cwd: "C:\\p\\zero"),
        ])
        let burn = CCLogs.reader(h, root: ".claude-personal").burnForLocalDay(Self.day)
        #expect(burn.total == 42)
        #expect(burn.byProject == ["(unknown)": 42])
        #expect(burn.byTier.isEmpty)
    }

    // Windows Missing_projects_dir_is_an_empty_burn_not_an_error
    @Test func missingProjectsIsEmpty() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude-personal")
        let burn = CCLogs.reader(h, root: ".claude-personal").burnForLocalDay(Self.day)
        #expect(burn == CCDayBurn())
    }

    // Windows Nested_subagent_transcripts_are_included
    @Test func nestedTranscriptsCount() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude-personal/projects/c--sanduhr/parent-uuid/sub")
        h.file(".claude-personal/projects/c--sanduhr/parent-uuid/sub/agent.jsonl",
               CCLogs.event(Self.localIso(Self.day, 15), "claude-fable-5", 5, 5,
                            cwd: "C:\\Users\\estev\\Projects\\Sanduhr") + "\n")
        #expect(CCLogs.reader(h, root: ".claude-personal").burnForLocalDay(Self.day).total == 10)
    }
}

@Suite("Claude Code log reader, incremental reads")
struct CCLogReaderIncrementalTests {
    // Windows Aggregate_for_local_cc_tab_caches_within_ttl, third step: an appended event lands.
    // Here without a TTL: the grown file is read from where the last read stopped.
    @Test func appendedLinesAreReadFromTheLastOffset() {
        let h = TempHome()
        defer { h.cleanUp() }
        let b = CCLogs.base
        let f = CCLogs.write(h, [CCLogs.event(CCLogs.iso(b.addingTimeInterval(60)), "claude-opus-4-7", 100, 100)])
        let size = LocalCCLogFileSystem().stat(f)?.size ?? 0
        let spy = SpyCCFileSystem()
        let r = CCLogs.reader(h, fileSystem: spy)
        #expect(r.burnSince(b).total == 200)
        CCLogs.append(f, CCLogs.event(CCLogs.iso(b.addingTimeInterval(600)), "claude-opus-4-7", 999, 999) + "\n")
        #expect(r.burnSince(b).total == Int64(2198))
        let reads = spy.reads.map(\.offset)
        #expect(reads.count == 2)
        #expect(reads[0] == 0)
        // The second read starts at the guard bytes just before the old end, not at 0.
        #expect(reads[1] == size - CCLogReader.guardLength)
    }

    @Test func anUnchangedFileIsNotReopened() {
        let h = TempHome()
        defer { h.cleanUp() }
        CCLogs.install("by-tier", in: h)
        let spy = SpyCCFileSystem()
        let r = CCLogs.reader(h, fileSystem: spy)
        let first = r.burnSince(CCLogs.base)
        let second = r.burnSince(CCLogs.base)
        #expect(first == second)
        #expect(spy.reads.count == 1)
    }

    @Test func filesOlderThanTheCutoffAreNotOpened() {
        let h = TempHome()
        defer { h.cleanUp() }
        let old = CCLogs.write(h, session: "old", [CCLogs.event(CCLogs.iso(CCLogs.base), "claude-opus-4-7", 5, 5)])
        CCLogs.setModified(old, CCLogs.base.addingTimeInterval(-3600))
        let fresh = CCLogs.write(h, session: "fresh", [CCLogs.event(CCLogs.iso(CCLogs.base), "claude-opus-4-7", 1, 1)])
        let spy = SpyCCFileSystem()
        #expect(CCLogs.reader(h, fileSystem: spy).burnSince(CCLogs.base).total == 2)
        #expect(spy.reads.map(\.path) == [fresh])
    }

    @Test func aLineBeingWrittenCountsOnceWhole() {
        let h = TempHome()
        defer { h.cleanUp() }
        let b = CCLogs.base
        let whole = CCLogs.event(CCLogs.iso(b.addingTimeInterval(60)), "claude-opus-4-7", 10, 10)
        let next = CCLogs.event(CCLogs.iso(b.addingTimeInterval(120)), "claude-opus-4-7", 1, 2)
        let f = CCLogs.write(h, [whole])
        let half = next.prefix(next.count / 2)
        CCLogs.append(f, String(half))
        let r = CCLogs.reader(h)
        #expect(r.burnSince(b).total == 20)                     // the half line is not JSON yet
        CCLogs.append(f, String(next.dropFirst(half.count)))    // whole, but no newline yet
        #expect(r.burnSince(b).total == 23)                     // counted, as Windows' ReadLine does
        CCLogs.append(f, "\n")
        #expect(r.burnSince(b).total == 23)                     // and never twice
        CCLogs.append(f, next + "\n")
        #expect(r.burnSince(b).total == 26)
        #expect(r.burnSince(b).events == 3)
    }

    @Test func aRewrittenOrShrunkFileIsReadWhole() {
        let h = TempHome()
        defer { h.cleanUp() }
        let b = CCLogs.base
        let ts = CCLogs.iso(b.addingTimeInterval(60))
        let f = CCLogs.write(h, [CCLogs.event(ts, "claude-opus-4-7", 100, 100),
                                 CCLogs.event(ts, "claude-opus-4-7", 100, 100)])
        let spy = SpyCCFileSystem()
        let r = CCLogs.reader(h, fileSystem: spy)
        #expect(r.burnSince(b).total == 400)
        // Shrunk: one shorter line.
        h.file(".claude/projects/P/s.jsonl", CCLogs.event(ts, "claude-opus-4-7", 1, 1) + "\n")
        #expect(r.burnSince(b).total == 2)
        let a = CCLogs.event(ts, "claude-opus-4-7", 3, 4) + "\n"
        h.file(".claude/projects/P/s.jsonl", a + a)
        #expect(r.burnSince(b).total == 14)
        // Same length, different bytes near the end, a later mtime: the guard bytes before the
        // old offset no longer match, so it is read whole.
        let other = CCLogs.event(CCLogs.iso(b.addingTimeInterval(61)), "claude-opus-4-7", 5, 6) + "\n"
        #expect(other.utf8.count == a.utf8.count)
        h.file(".claude/projects/P/s.jsonl", other + other)
        CCLogs.setModified(f, Date().addingTimeInterval(5))
        #expect(r.burnSince(b).total == 22)
        #expect(spy.reads.last?.offset == 0)
    }

    @Test func aDeletedFileLeavesTheCount() {
        let h = TempHome()
        defer { h.cleanUp() }
        let f = CCLogs.install("by-tier", in: h)
        let r = CCLogs.reader(h)
        #expect(r.burnSince(CCLogs.base).events == 3)
        try? FileManager.default.removeItem(atPath: f)
        #expect(r.burnSince(CCLogs.base) == CCBurnSince())
    }
}

@Suite("Claude Code timestamps and badge text")
struct CCTimestampAndFormatTests {
    @Test func parsesTheShapesInTheLogs() {
        let base = 1_777_975_200.0   // 2026-05-05T10:00:00Z
        #expect(CCTimestamp.parse("2026-05-05T10:00:00Z")?.timeIntervalSince1970 == base)
        #expect(CCTimestamp.parse("2026-05-05T10:00:00.500Z")?.timeIntervalSince1970 == base + 0.5)
        #expect(CCTimestamp.parse("2026-05-05T10:00:00.0000000+00:00")?.timeIntervalSince1970 == base)
        #expect(CCTimestamp.parse("2026-05-05T12:00:00+02:00")?.timeIntervalSince1970 == base)
        #expect(CCTimestamp.parse("2026-05-05T05:00:00-0500")?.timeIntervalSince1970 == base)
        #expect(CCTimestamp.parse("2026-05-05T10:00:00")?.timeIntervalSince1970 == base)   // no zone: UTC
        #expect(CCTimestamp.parse("2024-02-29T00:00:00Z")?.timeIntervalSince1970 == 1_709_164_800)
        #expect(CCTimestamp.parse("") == nil)
        #expect(CCTimestamp.parse("yesterday") == nil)
        #expect(CCTimestamp.parse("2026-13-05T10:00:00Z") == nil)
        #expect(CCTimestamp.parse("2026-05-05T10:00:00Zjunk") == nil)
    }

    // Windows TokenFormatTests.Compact_matches_python_ladder
    @Test(arguments: [
        (Int64(0), "0"), (1, "1"), (999, "999"), (1000, "1.0k"), (1499, "1.5k"), (1500, "1.5k"),
        (9999, "10.0k"), (10000, "10k"), (12345, "12k"), (999_999, "999k"), (1_000_000, "1.0M"),
        (1_234_567, "1.2M"),
    ])
    func compact(_ n: Int64, _ expected: String) {
        #expect(TokenFormat.compact(n) == expected)
    }

    @Test func spoken() {
        #expect(TokenFormat.spoken(1499) == "plus 1.5 thousand local tokens since the last refresh")
        #expect(TokenFormat.spoken(12345) == "plus 12 thousand local tokens since the last refresh")
        #expect(TokenFormat.spoken(1_234_567) == "plus 1.2 million local tokens since the last refresh")
        #expect(TokenFormat.spoken(1) == "plus 1 local token since the last refresh")
    }

    @Test func numbersReadLikeWindows() {
        #expect(CCLogReader.number(NSNumber(value: 12)) == 12)
        #expect(CCLogReader.number(NSNumber(value: 12.9)) == 12)
        #expect(CCLogReader.number("12") == 0)
        #expect(CCLogReader.number(NSNumber(value: true)) == 0)
        #expect(CCLogReader.number(nil) == 0)
    }
}

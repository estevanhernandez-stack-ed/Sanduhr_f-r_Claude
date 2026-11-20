import Foundation
import SQLite3

/// The Antigravity CLI's conversation list (every-agents spec, sections 3 and 5; Windows
/// `AntigravitySummaries`), read through the system SQLite, read-only, for the project lookup.
/// It selects `conversation_id` and `workspace_uris` and nothing else: never a title, a preview
/// or a summary. Nil when the file is missing or SQLite refuses it (locked past the busy timeout,
/// damaged, no table), so the Antigravity source lists nothing that pass.
enum AntigravitySummaries {
    /// The one statement this reader runs. A test pins it.
    static let query = "SELECT conversation_id, workspace_uris FROM conversation_summaries"

    /// The `file:` URI SQLite opens, built by hand so a space, `#`, `?` or literal `%XX` in the
    /// path is escaped once and never decoded twice.
    static func fileURI(_ path: String) -> String {
        "file://" + (path.addingPercentEncoding(withAllowedCharacters: uriSafe) ?? path)
    }

    private static let uriSafe = CharacterSet(
        charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~/")

    /// Conversation id to its `workspace_uris` JSON, rows with no workspace left out.
    static func read(dbPath: String, busyTimeoutMs: Int32 = 2000) -> [String: String]? {
        guard FileManager.default.fileExists(atPath: dbPath) else { return nil }
        var db: OpaquePointer?
        var stmt: OpaquePointer?
        defer {
            if stmt != nil { sqlite3_finalize(stmt) }
            if db != nil { sqlite3_close(db) }
        }
        guard sqlite3_open_v2(fileURI(dbPath) + "?mode=ro", &db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI, nil) == SQLITE_OK
        else { return nil }
        sqlite3_busy_timeout(db, busyTimeoutMs)
        guard sqlite3_prepare_v2(db, query, -1, &stmt, nil) == SQLITE_OK else { return nil }
        var map: [String: String] = [:]
        var rc = sqlite3_step(stmt)
        while rc == SQLITE_ROW {
            if let id = text(stmt, 0), !id.isEmpty, let uris = text(stmt, 1) { map[id] = uris }
            rc = sqlite3_step(stmt)
        }
        return rc == SQLITE_DONE ? map : nil
    }

    private static func text(_ stmt: OpaquePointer?, _ column: Int32) -> String? {
        guard let c = sqlite3_column_text(stmt, column) else { return nil }
        return String(cString: c)
    }
}

/// The Antigravity CLI's local burn for the Vault (every-agents spec, section 3; parity with
/// Windows `AntigravityBurnSource.cs`). Each `brain/<conversation_id>/.system_generated/logs/
/// transcript.jsonl` is one Vault session keyed by the conversation id. `transcript_full.jsonl`
/// repeats the same tokens and is never listed, opened or read. Reads only `created_at` and the
/// token counts from a transcript and `conversation_id` and `workspace_uris` from the database;
/// never a prompt, a reply or a title.
struct AntigravityBurnReader: AgentBurnSource {
    /// The transcript names no model.
    static let model = "antigravity"
    static let transcriptName = "transcript.jsonl"

    /// `~/.gemini/antigravity-cli`.
    var home: URL
    /// The database's busy timeout (tests shorten it).
    var busyTimeoutMs: Int32 = 2000
    /// Called with every transcript path and offset right before it is opened (a test spies it to
    /// prove no other file is ever read).
    var onOpen: (String, Int64) -> Void = { _, _ in }

    var agent: AgentSource { .antigravity }

    static func defaultHome(userHome: URL) -> URL {
        userHome.appendingPathComponent(".gemini", isDirectory: true)
            .appendingPathComponent("antigravity-cli", isDirectory: true)
    }

    /// This Mac's folder.
    static func liveHome() -> URL {
        defaultHome(userHome: FileManager.default.homeDirectoryForCurrentUser)
    }

    var summariesPath: String {
        home.appendingPathComponent("conversation_summaries.db").path
    }

    /// The first `file:` URI of a `workspace_uris` JSON array, as a path; nil for no entry, a
    /// non-array, a non-string or any other scheme.
    static func projectPath(ofWorkspaceURIs json: String?) -> String? {
        guard let json, let data = json.data(using: .utf8),
              let any = try? JSONSerialization.jsonObject(with: data) as? [Any],
              let first = any.first as? String else { return nil }
        let lower = first.lowercased()
        guard lower.hasPrefix("file:") else { return nil }
        var rest = String(first.dropFirst("file:".count))
        if rest.hasPrefix("//") {
            rest.removeFirst(2)
            // Past the host, which is empty or `localhost` for a local file.
            guard let slash = rest.firstIndex(of: "/") else { return nil }
            rest = String(rest[slash...])
        }
        guard let path = rest.removingPercentEncoding, path.hasPrefix("/"), path.count > 1 else { return nil }
        return path
    }

    func files() throws -> [AgentBurnFile] {
        let fm = FileManager.default
        let brain = home.appendingPathComponent("brain", isDirectory: true)
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: brain.path, isDirectory: &isDir), isDir.boolValue else { return [] }
        // An unreadable database (locked, missing, damaged) lists nothing, so no checkpoint moves
        // and the next pass reads the transcripts with their projects.
        guard let map = AntigravitySummaries.read(dbPath: summariesPath, busyTimeoutMs: busyTimeoutMs) else { return [] }
        var out: [AgentBurnFile] = []
        for conversation in ((try? fm.contentsOfDirectory(atPath: brain.path)) ?? []).sorted() {
            let path = brain.appendingPathComponent(conversation, isDirectory: true)
                .appendingPathComponent(".system_generated", isDirectory: true)
                .appendingPathComponent("logs", isDirectory: true)
                .appendingPathComponent(Self.transcriptName).path
            guard let a = try? fm.attributesOfItem(atPath: path), a[.type] as? FileAttributeType == .typeRegular else { continue }
            let size = (a[.size] as? NSNumber)?.int64Value ?? 0
            out.append(AgentBurnFile(path: path, sessionID: conversation,
                                     cwd: Self.projectPath(ofWorkspaceURIs: map[conversation]),
                                     compressed: false, length: size,
                                     mtimeTicks: VaultTime.ticks(a[.modificationDate] as? Date ?? Date())))
        }
        return out
    }

    func read(_ file: AgentBurnFile, from offset: Int64, state: String?) throws -> AgentBurnRead {
        onOpen(file.path, offset)
        var read = AgentBurnRead()
        read.endOffset = try AgentBurnFileReader.lines(plain: file.path, from: offset) { line in
            let (event, bad) = AntigravityLineParser.parse(line)
            if bad { read.skippedLines += 1 }
            if let event { read.events.append(event) }
        }
        return read
    }
}

/// One transcript line. A line with any of `input_tokens`, `output_tokens` or `cache_read_tokens`
/// is a token event dated by its own `created_at`; user input and tool steps carry none and are
/// skipped without counting as bad. `input_tokens` is fresh input as it stands: the 2026-11-13
/// build check found `cache_read_tokens` kept apart from it. Stateless.
enum AntigravityLineParser {
    private static let mark = Data("_tokens\"".utf8)

    static func parse(_ line: Data) -> (BurnEvent?, bad: Bool) {
        guard line.range(of: mark) != nil else { return (nil, false) }
        guard let any = try? JSONSerialization.jsonObject(with: line, options: [.fragmentsAllowed]),
              let d = any as? [String: Any] else { return (nil, true) }
        guard d["input_tokens"] != nil || d["output_tokens"] != nil || d["cache_read_tokens"] != nil,
              let ts = (d["created_at"] as? String).flatMap(VaultTime.parse) else { return (nil, false) }
        return (BurnEvent(timestamp: ts, input: CCLogReader.number(d["input_tokens"]),
                          output: CCLogReader.number(d["output_tokens"]),
                          cacheRead: CCLogReader.number(d["cache_read_tokens"]),
                          model: AntigravityBurnReader.model, cwd: nil), false)
    }
}

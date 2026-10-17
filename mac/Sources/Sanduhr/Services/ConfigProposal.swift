import Foundation

/// Config an agent proposed through the MCP server's `propose_config` (item 76, slice K): files
/// for one Claude Code folder, settings keys and MCP servers, checked again here with the server's
/// rules and scanned like an export. A proposal that passes becomes a config pack in memory, which
/// the Open sheet reviews like any other: only what the folder lacks can be brought over.
struct ConfigProposal: @unchecked Sendable {
    // The server's names (sanduhr_mcp.py); a test on each side pins them.
    static let requestFile = "config-request.json"
    static let resultFile = "config-result.json"
    static let maxServers = 16
    static let maxSettingsBytes = 64 * 1024

    var id: String
    /// The folder by the server's name (`.claude-personal`).
    var folder: String
    var summary: String
    var files: [ModProposal.File]
    var settings: [String: Any]
    var servers: [String: Any]
    var requestedAt: Date

    enum Decoded {
        case proposal(ConfigProposal)
        case refused(id: String, [String])
        case discard
    }

    /// Why a path can't be proposed, nil when it can: CLAUDE.md, an agent, command or output style,
    /// or a skill's file.
    static func pathProblem(_ path: String) -> String? {
        let pattern = #"^(CLAUDE\.md|(agents|commands|output-styles)/[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*\.md|skills/[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)+)$"#
        guard path.count <= ModProposal.maxPath, path.range(of: pattern, options: .regularExpression) != nil,
              !path.split(separator: "/").contains(where: { $0 == "." || $0 == ".." || $0.hasPrefix(".") }) else {
            return "a path is CLAUDE.md, agents/<name>.md, commands/<name>.md, output-styles/<name>.md or skills/<name>/<file>"
        }
        return nil
    }

    static func decode(_ data: Data, now: Date) -> Decoded {
        guard let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let id = root["id"] as? String, id.range(of: #"^[0-9a-f]{32}$"#, options: .regularExpression) != nil else {
            return .discard
        }
        guard let stamp = root["requested_at"] as? String, let at = MessageProposal.parseDate(stamp) else {
            return .refused(id: id, ["requested_at is missing or not a date"])
        }
        if now.timeIntervalSince(at) > ModProposal.maxAge { return .discard }
        guard (root["schema_version"] as? Int) == 1 else { return .refused(id: id, ["This Sanduhr reads requests of schema 1."]) }
        var reasons: [String] = []
        let folder = root["folder"] as? String ?? ""
        if !ModProposal.isFolderName(folder) { reasons.append("folder must be a Claude Code folder's name (.claude-personal)") }
        let summary = (root["summary"] as? String)?.trimmingCharacters(in: .whitespaces) ?? ""
        if summary.isEmpty || summary.count > ModProposal.maxSummary || summary.contains(where: \.isNewline) {
            reasons.append("summary must be one line of 1 to \(ModProposal.maxSummary) characters")
        }

        // Files: the server's rules, then the export's scan.
        var files: [ModProposal.File] = []
        let raw = root["files"] as? [Any] ?? []
        if raw.count > ModProposal.maxFiles { reasons.append("files must be a list of at most \(ModProposal.maxFiles) {path, text}") }
        var seen = Set<String>()
        for item in raw.prefix(ModProposal.maxFiles) {
            guard let f = item as? [String: Any], Set(f.keys) == ["path", "text"],
                  let path = f["path"] as? String, let text = f["text"] as? String else {
                reasons.append("each file is exactly {path, text}")
                continue
            }
            if let problem = pathProblem(path) {
                reasons.append("\(path.prefix(60)): \(problem)")
            } else if !seen.insert(path.lowercased()).inserted {
                reasons.append("\(path) is there twice")
            } else if text.contains("\u{0}") || text.utf8.count > ModProposal.maxFileBytes {
                reasons.append("\(path): text must be under \(ModProposal.maxFileBytes / 1024) KB, without NUL characters")
            } else if let line = ConfigPack.secretLine(in: text) {
                reasons.append("\(path) line \(line) looks like a secret: send a ${NAME} or leave it out")
            } else {
                files.append(ModProposal.File(path: path, text: text))
            }
        }
        if files.reduce(0, { $0 + $1.text.utf8.count }) > ModProposal.maxBytes {
            reasons.append("the files are larger than \(ModProposal.maxBytes / 1024) KB in all")
        }

        // Settings: only the keys a pack carries, and no value the mask would hide.
        let settings = root["settings"] as? [String: Any] ?? [:]
        let other = settings.keys.filter { !ConfigPack.settingsKeys.contains($0) }.sorted()
        if !other.isEmpty {
            reasons.append("settings may hold only " + ConfigPack.settingsKeys.joined(separator: ", ") + "; not " + other.prefix(5).joined(separator: ", "))
        } else if (try? JSONSerialization.data(withJSONObject: settings))?.count ?? 0 > maxSettingsBytes {
            reasons.append("settings are larger than \(maxSettingsBytes / 1024) KB")
        } else if ConfigPack.walk(settings, rewrite: { $0 }, check: { ConfigMask.maskString($0) != $0 ? "x" : nil }).1 != nil {
            reasons.append("settings hold a value that looks like a secret")
        }

        // MCP servers: every env and header value a ${NAME} reference; nothing secret-looking.
        let servers = root["mcp_servers"] as? [String: Any] ?? [:]
        if servers.count > maxServers { reasons.append("mcp_servers holds at most \(maxServers) servers") }
        for (name, value) in servers.sorted(by: { $0.key < $1.key }) {
            guard name.range(of: #"^[A-Za-z0-9_-]{1,64}$"#, options: .regularExpression) != nil, name != "sanduhr" else {
                reasons.append("\(name.prefix(40)): a server name is 1-64 letters, digits, '_' or '-', and not sanduhr")
                continue
            }
            guard let config = value as? [String: Any], config["command"] is String || config["url"] is String else {
                reasons.append("\(name): a server is an object with a command or a url")
                continue
            }
            for key in ["env", "headers"] {
                let values = config[key] as? [String: Any] ?? [:]
                if config[key] != nil && config[key] as? [String: Any] == nil
                    || values.values.contains(where: { ($0 as? String)?.range(of: #"^\$\{[A-Za-z_][A-Za-z0-9_]{0,127}\}$"#, options: .regularExpression) == nil }) {
                    reasons.append("\(name): every \(key) value must be a ${NAME} reference, never the value")
                }
            }
            let rest = config.filter { $0.key != "env" && $0.key != "headers" }
            if ConfigPack.walk(rest, rewrite: { $0 }, check: { ConfigMask.maskString($0) != $0 ? "x" : nil }).1 != nil {
                reasons.append("\(name): its command, url or arguments hold a value that looks like a secret")
            }
        }
        if reasons.isEmpty, files.isEmpty, settings.isEmpty, servers.isEmpty {
            reasons.append("propose at least one file, setting or MCP server")
        }
        guard reasons.isEmpty else { return .refused(id: id, Array(reasons.prefix(20))) }
        return .proposal(ConfigProposal(id: id, folder: folder, summary: summary, files: files, settings: settings,
                                        servers: servers, requestedAt: at))
    }

    /// The proposal as a config pack the Open sheet reads: files under `files/`, the settings and
    /// servers in the manifest as an export writes them.
    var pack: ModPackReader.Unpacked {
        var manifestFiles: [[String: Any]] = []
        var out: [String: Data] = [:]
        for f in files {
            let data = Data(f.text.utf8)
            out["files/" + f.path] = data
            manifestFiles.append(["path": "files/" + f.path, "bytes": data.count,
                                  "sha256": Digest.sha256Hex(data)])
        }
        let manifest: [String: Any] = [
            "schema": 1, "kind": "config", "label": "Claude's proposal", "created_by": "Claude, through propose_config",
            "created_at": HandoffFiles.stamp(requestedAt), "files": manifestFiles, "settings": settings, "env": [Any](),
            "plugins": [Any](), "kept_mods": [Any](),
            "mcp": servers.sorted { $0.key < $1.key }.map { ["name": $0.key, "config": $0.value] as [String: Any] },
        ]
        return ModPackReader.Unpacked(manifest: manifest, files: out)
    }

    /// The request as staged: its JSON, read again on Review.
    var json: Data {
        let doc: [String: Any] = ["schema_version": 1, "id": id, "requested_at": HandoffFiles.stamp(requestedAt), "folder": folder,
                                  "summary": summary, "files": files.map { ["path": $0.path, "text": $0.text] },
                                  "settings": settings, "mcp_servers": servers]
        return (try? JSONSerialization.data(withJSONObject: doc, options: [.sortedKeys, .withoutEscapingSlashes])) ?? Data()
    }

    /// The answer the server waits for.
    static func resultJSON(id: String, status: String, folder: String? = nil, reason: String? = nil, reasons: [String] = [],
                           remedy: String? = nil, now: Date = Date()) -> Data {
        HandoffFiles.resultJSON(id: id, status: status, fields: [("folder", folder), ("reason", reason), ("remedy", remedy)],
                                reasons: reasons, now: now)
    }
}

/// A config proposal as Sanduhr keeps it while the user decides, and after.
struct ConfigProposalRecord: Codable, Equatable, Identifiable, Sendable {
    enum Status: String, Codable, Sendable {
        case pendingReview = "pending_review"
        case approved, dismissed
    }

    var id: String
    var summary: String
    /// The folder by the server's name, and its path when Sanduhr knew it.
    var folderName: String
    var folder: String
    var files: Int
    var settings: Int
    var servers: Int
    var requestedAt: Date
    var status: Status
    var decidedAt: Date?

    var isPending: Bool { status == .pendingReview }
}

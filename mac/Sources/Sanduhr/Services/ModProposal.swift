import Foundation

/// A Claude Code mod an agent proposed through the MCP server's `propose_mod` (item 75, slice G):
/// its files inline, checked again here with the server's rules. The app is the authority: a
/// proposal that passes here is staged read-only for the user to review, and nothing else.
struct ModProposal: Equatable, Sendable {
    struct File: Equatable, Sendable {
        var path: String
        var text: String
    }

    // The server's names and limits (sanduhr_mcp.py); a test on each side pins them.
    static let switchFile = "mods-agents.json"
    static let requestFile = "mod-request.json"
    static let resultFile = "mod-result.json"
    static let statusFile = "mod-proposals.json"
    static let maxFiles = 24
    static let maxFileBytes = 64 * 1024
    static let maxBytes = 256 * 1024
    static let maxSummary = 200
    static let maxVersion = 40
    static let maxPath = 200
    static let extensions = [".ts", ".tsx", ".js", ".mjs", ".json", ".md"]
    /// The request file holds the files as JSON strings: room for their escaping.
    static let maxRequestBytes = 1024 * 1024
    static let maxAge: TimeInterval = 600

    var id: String
    var name: String
    var version: String?
    var summary: String
    var files: [File]
    var requestedAt: Date

    var bytes: Int { files.reduce(0) { $0 + $1.text.utf8.count } }

    enum Decoded: Equatable {
        case proposal(ModProposal)
        /// Readable enough to answer: the reasons go back under the id.
        case refused(id: String, [String])
        case discard
    }

    /// Why a file path can't be part of a mod, nil when it can.
    static func pathProblem(_ path: String) -> String? {
        guard !path.isEmpty, path.count <= maxPath,
              path.range(of: #"^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$"#, options: .regularExpression) != nil else {
            return "a path must be relative, of letters, digits, '.', '_', '-' and '/' (at most \(maxPath) characters)"
        }
        let parts = path.split(separator: "/").map(String.init)
        if parts.contains(where: { $0 == "." || $0 == ".." }) { return "a path must not hold '.' or '..'" }
        if parts.contains(where: { $0.hasPrefix(".") && $0 != ".claude-plugin" }) { return "only .claude-plugin may start with a dot" }
        if path.hasPrefix(".claude-plugin/types/") { return "Claude Code writes .claude-plugin/types itself; leave it out" }
        if !extensions.contains(where: { path.lowercased().hasSuffix($0) }) {
            return "files must end in " + extensions.joined(separator: " ")
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
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard (root["schema_version"] as? Int) == 1 else { return .refused(id: id, ["This Sanduhr reads requests of schema 1."]) }
        var reasons: [String] = []
        let name = root["name"] as? String ?? ""
        if !KeptMods.isName(name) || name.count > 40 {
            reasons.append("name must be 1-40 lowercase letters, digits or hyphens, not starting with a hyphen")
        } else if name == IntegrationScripts.modName {
            reasons.append("'\(name)' is Sanduhr's own mod; pick another name")
        }
        var version: String?
        if let v = root["version"], !(v is NSNull) {
            if let s = v as? String, s.count <= maxVersion, !s.contains(where: \.isNewline) {
                version = s.trimmingCharacters(in: .whitespaces).isEmpty ? nil : s.trimmingCharacters(in: .whitespaces)
            } else {
                reasons.append("version must be one line of at most \(maxVersion) characters")
            }
        }
        let summary = (root["summary"] as? String)?.trimmingCharacters(in: .whitespaces) ?? ""
        if summary.isEmpty || summary.count > maxSummary || summary.contains(where: \.isNewline) {
            reasons.append("summary must be one line of 1 to \(maxSummary) characters")
        }
        var files: [File] = []
        let raw = root["files"] as? [Any] ?? []
        if raw.isEmpty || raw.count > maxFiles { reasons.append("files must be a list of 1 to \(maxFiles) {path, text}") }
        var seen = Set<String>()
        for item in raw.prefix(maxFiles) {
            guard let f = item as? [String: Any], Set(f.keys) == ["path", "text"],
                  let path = f["path"] as? String, let text = f["text"] as? String else {
                reasons.append("each file is exactly {path, text}")
                continue
            }
            if let problem = pathProblem(path) {
                reasons.append("\(path.prefix(60)): \(problem)")
            } else if !seen.insert(path.lowercased()).inserted {
                reasons.append("\(path) is there twice")
            } else if text.contains("\u{0}") {
                reasons.append("\(path): text must be a string without NUL characters")
            } else if text.utf8.count > maxFileBytes {
                reasons.append("\(path) is larger than \(maxFileBytes / 1024) KB")
            } else {
                files.append(File(path: path, text: text))
            }
        }
        if files.reduce(0, { $0 + $1.text.utf8.count }) > maxBytes {
            reasons.append("the files are larger than \(maxBytes / 1024) KB in all")
        }
        if let manifest = files.first(where: { $0.path == ".claude-plugin/plugin.json" }) {
            let doc = (try? JSONSerialization.jsonObject(with: Data(manifest.text.utf8))) as? [String: Any]
            if doc == nil {
                reasons.append(".claude-plugin/plugin.json must be a JSON object")
            } else if doc?["name"] as? String != name {
                reasons.append("plugin.json's name must be '\(name)'")
            }
        } else if !raw.isEmpty {
            reasons.append(".claude-plugin/plugin.json is required")
        }
        guard reasons.isEmpty else { return .refused(id: id, Array(reasons.prefix(20))) }
        return .proposal(ModProposal(id: id, name: name, version: version, summary: summary, files: files, requestedAt: at))
    }

    /// The answer the server waits for: `pending_review`, or `rejected` with reasons.
    static func resultJSON(id: String, status: String, name: String? = nil, version: String? = nil,
                           reason: String? = nil, reasons: [String] = [], remedy: String? = nil, now: Date = Date()) -> Data {
        var result: [String: Any] = ["status": status]
        for (k, v) in [("name", name), ("version", version), ("reason", reason), ("remedy", remedy)] { if let v { result[k] = v } }
        if !reasons.isEmpty { result["reasons"] = reasons }
        let root: [String: Any] = ["id": id, "completed_at": HandoffFiles.stamp(now), "result": result]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])) ?? Data()
    }
}

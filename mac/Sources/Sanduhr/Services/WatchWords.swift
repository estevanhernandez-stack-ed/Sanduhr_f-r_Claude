import Foundation

/// Words that name work (item 76, slice G): the company, products, people. Kept per seat in
/// `watch-words.json` in Sanduhr's folder, owner-only, on this Mac alone: never in a pack, a
/// log or the MCP server's files. Export Config flags a row of a work seat holding one and
/// leaves it unchecked.
struct WatchWords {
    let url: URL
    static var standard: WatchWords { WatchWords(url: HistoryStore.Files.standard.dir.appendingPathComponent("watch-words.json")) }

    private struct File: Codable {
        var schema_version = 1
        var seats: [String: [String]] = [:]
    }

    func words(for seat: String) -> [String] {
        load().seats[AccountData.normalized(seat)] ?? []
    }

    /// Every seat's words, once each: a kept mod belongs to no seat, so all of them apply.
    func all() -> [String] {
        Self.parse(load().seats.keys.sorted().flatMap { load().seats[$0] ?? [] }.joined(separator: "\n"))
    }

    func set(_ words: [String], for seat: String) {
        var file = load()
        let clean = Self.parse(words.joined(separator: "\n"))
        file.seats[AccountData.normalized(seat)] = clean.isEmpty ? nil : clean
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        if let data = try? e.encode(file) { HandoffFiles.writeOwnerOnly(data, to: url) }
    }

    private func load() -> File {
        guard let data = FileManager.default.contents(atPath: url.path),
              let file = try? JSONDecoder().decode(File.self, from: data), file.schema_version == 1 else { return File() }
        return file
    }

    /// Words typed as a list: split on commas and new lines, trimmed, two characters at least,
    /// each once whatever its case, in the order given.
    static func parse(_ text: String) -> [String] {
        var seen = Set<String>()
        return text.split(whereSeparator: { $0 == "," || $0.isNewline }).compactMap { part in
            let w = part.trimmingCharacters(in: .whitespaces)
            guard w.count >= 2, seen.insert(w.lowercased()).inserted else { return nil }
            return w
        }
    }

    /// The words of `words` that `item` holds, as whole words in any case: in its name, detail,
    /// the text of its files or its manifest entry.
    static func matches(_ item: ConfigPack.Item, words: [String]) -> [String] {
        let text = searchable(item)
        return words.filter { w in
            let pattern = "(?<![A-Za-z0-9])" + NSRegularExpression.escapedPattern(for: w) + "(?![A-Za-z0-9])"
            return text.range(of: pattern, options: [.regularExpression, .caseInsensitive]) != nil
        }
    }

    /// `items` with each one holding a watch word flagged and unchecked by default.
    static func flag(_ items: [ConfigPack.Item], words: [String]) -> [ConfigPack.Item] {
        items.map { item in
            var item = item
            item.watched = words.isEmpty ? [] : matches(item, words: words)
            if !item.watched.isEmpty { item.onByDefault = false }
            return item
        }
    }

    /// Names the seat's rows use that look like names rather than words: capitalized or capitals,
    /// three letters or more, not in the system's word list or a short list of tool names, in two
    /// rows or more. Most first, at most `limit`, none already listed. Shown on this Mac only.
    static func suggest(_ items: [ConfigPack.Item], known: [String], dictionary: Set<String> = systemWords,
                        limit: Int = 24) -> [String] {
        let token = try! NSRegularExpression(pattern: #"(?<![A-Za-z0-9_])[A-Z][A-Za-z0-9]{2,}(?![A-Za-z0-9_])"#)
        let skip = Set(known.map { $0.lowercased() }).union(toolWords)
        var rows: [String: Int] = [:]
        var spelled: [String: String] = [:]
        for item in items {
            let text = searchable(item)
            let ns = text as NSString
            var here = Set<String>()
            for m in token.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
                let w = ns.substring(with: m.range)
                let key = w.lowercased()
                guard !skip.contains(key), !dictionary.contains(key) else { continue }
                if here.insert(key).inserted { rows[key, default: 0] += 1 }
                if spelled[key] == nil { spelled[key] = w }
            }
        }
        return rows.filter { $0.value >= 2 }
            .sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }
            .prefix(limit).compactMap { spelled[$0.key] }
    }

    private static func searchable(_ item: ConfigPack.Item) -> String {
        ([item.name, item.detail] + item.files.map(\.text) + [item.json.map { String(decoding: $0, as: UTF8.self) } ?? ""])
            .joined(separator: "\n")
    }

    /// `/usr/share/dict/words`, lowercased: ordinary words never suggested.
    static let systemWords: Set<String> = {
        guard let text = try? String(contentsOfFile: "/usr/share/dict/words", encoding: .utf8) else { return [] }
        return Set(text.split(separator: "\n").map { $0.lowercased() })
    }()

    /// Names every Claude Code folder uses: tools, formats, file names.
    static let toolWords: Set<String> = [
        "claude", "anthropic", "opus", "sonnet", "haiku", "fable", "mcp", "api", "cli", "json", "yaml", "toml", "html", "css",
        "markdown", "readme", "todo", "github", "gitlab", "git", "npm", "npx", "node", "python", "swift", "swiftui", "xcode",
        "macos", "ios", "linux", "windows", "bash", "zsh", "http", "https", "url", "uri", "sql", "sdk", "ide", "vscode",
        "cursor", "codex", "gemini", "openai", "google", "microsoft", "apple", "azure", "aws", "docker", "typescript",
        "javascript", "react", "pr", "prs", "ci", "cd", "ui", "ux", "id", "ids", "env", "oauth", "jwt", "uuid", "utc", "iso",
        "agents", "skill", "skills", "subagent", "subagents", "hooks", "bearer", "pdf", "csv", "png", "svg", "dmg", "zip",
    ]
}

import Foundation

/// A config pack's second folder, `any-agent/` (item 76, slice H, `docs/agnostify-spec.md`): the
/// same chosen rows in the formats other coding agents read. Skills as Agent Skills, agents and
/// commands turned into skills, an `AGENTS.md`, one MCP file per agent, and what has no
/// equivalent, named. Built only from rows already checked and scanned, so nothing reaches it
/// that isn't in the pack already.
enum AnyAgent {
    static let folder = "any-agent/"

    /// Every file of `any-agent/` for `items` (the rows that travel).
    static func files(_ items: [ConfigPack.Item]) -> [ConfigPack.File] {
        var out: [ConfigPack.File] = []
        var used = Set<String>()
        var index: [(name: String, description: String)] = []
        var renamed: [String] = []
        var leftOut: [String] = []

        func claim(_ wanted: String, suffix: String) -> String {
            var name = skillName(wanted)
            if used.contains(name) { name = skillName(String(name.prefix(64 - suffix.count - 1)) + "-" + suffix) }
            var n = 2
            let base = name
            while used.contains(name) { name = skillName(String(base.prefix(60)) + "-\(n)"); n += 1 }
            used.insert(name)
            if name != wanted { renamed.append("`\(wanted)` → `\(name)`") }
            return name
        }

        // Skills: already Agent Skills folders; only the name may need to fit the standard.
        for item in items where item.kind == .skill {
            guard let first = item.files.first?.path.split(separator: "/").dropFirst(2).first else { continue }
            let dir = String(first)
            let name = claim(dir, suffix: "skill")
            for f in item.files {
                let rest = f.path.split(separator: "/").dropFirst(3).joined(separator: "/")
                var text = f.text
                if rest == "SKILL.md" {
                    let fm = frontmatter(text)
                    let description = fm?.fields.first { $0.key == "description" }?.value ?? ""
                    text = withFields(text, name: name, description: description.isEmpty ? "The \(name) skill." : nil)
                    index.append((name, description.isEmpty ? "The \(name) skill." : description))
                }
                out.append(.init(path: folder + "skills/\(name)/\(rest)", text: text))
            }
        }

        // Agents: the prompt becomes the skill; what only Claude Code reads is named.
        for item in items where item.kind == .agent {
            guard let f = item.files.first else { continue }
            let fm = frontmatter(f.text)
            let name = claim(item.name, suffix: "agent")
            let description = clipped(fm?.fields.first { $0.key == "description" }?.value ?? "") ?? "Works as the \(item.name) agent."
            let dropped = (fm?.fields ?? []).map(\.key).filter { !["name", "description"].contains($0) }
            if !dropped.isEmpty { leftOut.append("Agent `\(item.name)`: its \(dropped.joined(separator: ", ")) (Claude Code subagent settings).") }
            out.append(.init(path: folder + "skills/\(name)/SKILL.md", text: skill(
                name: name, description: description,
                compatibility: "Written as a Claude Code subagent; runs inline here.",
                body: fm?.body ?? f.text)))
            index.append((name, description))
        }

        // Commands: a skill to use when the user asks for the command.
        for item in items where item.kind == .command {
            guard let f = item.files.first else { continue }
            let fm = frontmatter(f.text)
            let name = claim(item.name, suffix: "command")
            let said = clipped(fm?.fields.first { $0.key == "description" }?.value ?? "")
            let description = clipped((said.map { $0 + " " } ?? "") + "Use when the user asks for /\(item.name).") ?? "Use when the user asks for /\(item.name)."
            var body = fm?.body ?? f.text
            if let hint = fm?.fields.first(where: { $0.key == "argument-hint" })?.value, !hint.isEmpty {
                body = "Arguments: \(hint)\n\n" + body
            }
            if body.contains("$ARGUMENTS") || body.range(of: #"\$[1-9]"#, options: .regularExpression) != nil {
                body = "`$ARGUMENTS` (and `$1`, `$2`…) stand for what the user wrote after /\(item.name).\n\n" + body
            }
            let dropped = (fm?.fields ?? []).map(\.key).filter { !["description", "argument-hint"].contains($0) }
            if !dropped.isEmpty { leftOut.append("Command `/\(item.name)`: its \(dropped.joined(separator: ", ")).") }
            out.append(.init(path: folder + "skills/\(name)/SKILL.md", text: skill(
                name: name, description: description,
                compatibility: "Written as a Claude Code slash command.", body: body)))
            index.append((name, description))
        }

        // MCP servers: one file per agent, each spelling the pack's ${NAME} references its way.
        let servers: [(name: String, config: [String: Any])] = items.filter { $0.kind == .mcpServer }.compactMap { item in
            guard let data = item.json, let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
                  let name = o["name"] as? String, let config = o["config"] as? [String: Any] else { return nil }
            return (name, config)
        }
        if !servers.isEmpty {
            out.append(.init(path: folder + "mcp/mcp.json", text: json(["mcpServers": dict(servers) { $0 }])))
            out.append(.init(path: folder + "mcp/cursor.json", text: json(["mcpServers": dict(servers, cursor)])))
            out.append(.init(path: folder + "mcp/vscode.json", text: json(["servers": dict(servers, vscode)])))
            out.append(.init(path: folder + "mcp/gemini.json", text: json(["mcpServers": dict(servers, gemini)])))
            let (toml, skipped) = codex(servers)
            out.append(.init(path: folder + "mcp/codex.toml", text: toml))
            leftOut += skipped.map { "MCP server `\($0)` in codex.toml: Codex reaches servers over streamable HTTP, not SSE." }
        }

        // Environment variables to set: the env row's names and every reference a server makes.
        var envNames = Set(items.filter { $0.kind == .env }.flatMap { item -> [String] in
            (item.json.flatMap { (try? JSONSerialization.jsonObject(with: $0)) as? [[String: Any]] } ?? []).compactMap { $0["name"] as? String }
        })
        for s in servers { envNames.formUnion(references(in: s.config)) }
        envNames.remove("HOME")

        for item in items {
            switch item.kind {
            case .plugin: leftOut.append("Plugin `\(item.name)`: a Claude Code marketplace plugin (\(item.detail)).")
            case .outputStyle: leftOut.append("Output style `\(item.name)`: only Claude Code has output styles.")
            case .keptMod: leftOut.append("Mod `\(item.name)`: a Sanduhr mod for Claude Code's prompt.")
            case .setting: leftOut.append("Setting `\(item.name)`: a Claude Code settings key.")
            default: break
            }
        }

        let instructions = items.first { $0.kind == .instructions }?.files.first?.text
        out.append(.init(path: folder + "AGENTS.md", text: agentsMd(instructions: instructions, index: index, env: envNames.sorted())))
        out.append(.init(path: folder + "README.md", text: readme(hasMCP: !servers.isEmpty, hasSkills: !index.isEmpty,
                                                                  env: envNames.sorted(), renamed: renamed)))
        if !leftOut.isEmpty {
            out.append(.init(path: folder + "LEFT-OUT.md", text: "# Left out\n\nWhat this pack carries for Claude Code that other agents have no place for.\n\n"
                             + leftOut.map { "- " + $0 }.joined(separator: "\n") + "\n"))
        }
        return out
    }

    /// The zip entries `paths` make, counting each folder once as `ditto` writes it.
    static func entryCount(_ paths: [String]) -> Int {
        var dirs = Set<String>()
        for p in paths {
            var parts = p.split(separator: "/").dropLast()
            while !parts.isEmpty { dirs.insert(parts.joined(separator: "/")); parts = parts.dropLast() }
        }
        return paths.count + dirs.count
    }

    // MARK: Skills

    /// A name the Agent Skills standard accepts: lowercase letters, digits, single hyphens, 64 at most.
    static func skillName(_ s: String) -> String {
        var out = ""
        for c in s.lowercased() {
            if ("a"..."z").contains(c) || ("0"..."9").contains(c) { out.append(c) } else if !out.isEmpty, out.last != "-" { out.append("-") }
        }
        out = String(out.prefix(64))
        while out.hasSuffix("-") { out.removeLast() }
        return out.isEmpty ? "skill" : out
    }

    struct Frontmatter {
        var fields: [(key: String, value: String)]
        var body: String
    }

    /// The YAML header's top-level `key: value` lines (block scalars folded to one string), and
    /// the rest. Nil without a header.
    static func frontmatter(_ text: String) -> Frontmatter? {
        let lines = text.components(separatedBy: "\n")
        guard lines.first?.trimmingCharacters(in: .whitespaces) == "---",
              let end = lines.dropFirst().firstIndex(where: { $0.trimmingCharacters(in: .whitespaces) == "---" }) else { return nil }
        var fields: [(key: String, value: String)] = []
        var i = 1
        while i < end {
            let line = lines[i]
            i += 1
            guard let first = line.first, first != " ", first != "\t", first != "#", let colon = line.firstIndex(of: ":") else { continue }
            let key = String(line[..<colon]).trimmingCharacters(in: .whitespaces)
            var value = String(line[line.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
            var more: [String] = []
            while i < end, lines[i].first == " " || lines[i].first == "\t" || lines[i].isEmpty {
                more.append(lines[i].trimmingCharacters(in: .whitespaces))
                i += 1
            }
            if ["|", ">", "|-", ">-", "|+", ">+"].contains(value) {
                value = more.filter { !$0.isEmpty }.joined(separator: value.hasPrefix("|") ? "\n" : " ")
            } else if !more.isEmpty {
                value = ([value] + more.filter { !$0.isEmpty }).joined(separator: " ")
            }
            if value.count >= 2, let q = value.first, q == "\"" || q == "'", value.last == q {
                // Double-quoted escapes read as JSON's, which cover what this file writes.
                if q == "\"", let decoded = (try? JSONSerialization.jsonObject(with: Data(value.utf8), options: .fragmentsAllowed)) as? String {
                    value = decoded
                } else {
                    value = String(value.dropFirst().dropLast())
                    if q == "'" { value = value.replacingOccurrences(of: "''", with: "'") }
                }
            }
            fields.append((key, value))
        }
        return Frontmatter(fields: fields, body: String(lines[(end + 1)...].joined(separator: "\n").drop { $0 == "\n" }))
    }

    /// `text` with its header's `name` set to `name`, and a `description` added when given.
    static func withFields(_ text: String, name: String, description: String?) -> String {
        var lines = text.components(separatedBy: "\n")
        guard lines.first?.trimmingCharacters(in: .whitespaces) == "---",
              let end = lines.dropFirst().firstIndex(where: { $0.trimmingCharacters(in: .whitespaces) == "---" }) else {
            return skill(name: name, description: description ?? "The \(name) skill.", compatibility: nil, body: text)
        }
        var at = end
        if let d = description { lines.insert("description: " + yaml(d), at: at); at += 1 }
        if let n = lines[1..<at].firstIndex(where: { $0.hasPrefix("name:") }) {
            lines[n] = "name: " + name
        } else {
            lines.insert("name: " + name, at: 1)
        }
        return lines.joined(separator: "\n")
    }

    static func skill(name: String, description: String, compatibility: String?, body: String) -> String {
        var head = ["---", "name: " + name, "description: " + yaml(description)]
        if let c = compatibility { head.append("compatibility: " + yaml(c)) }
        head.append("---")
        return head.joined(separator: "\n") + "\n\n" + body.trimmingCharacters(in: .newlines) + "\n"
    }

    /// The standard's 1,024-character cap; nil when empty.
    private static func clipped(_ s: String) -> String? {
        let t = s.trimmingCharacters(in: .whitespacesAndNewlines)
        return t.isEmpty ? nil : String(t.prefix(1024))
    }

    /// A YAML double-quoted scalar (a JSON string is one).
    static func yaml(_ s: String) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: s, options: [.fragmentsAllowed, .withoutEscapingSlashes])) ?? Data("\"\"".utf8)
        return String(decoding: data, as: UTF8.self)
    }

    // MARK: MCP

    private static let reference = try! NSRegularExpression(pattern: #"\$\{([A-Za-z_][A-Za-z0-9_]*)\}"#)

    /// `s` with every `${NAME}` written by `spell`.
    static func respell(_ s: String, _ spell: (String) -> String) -> String {
        let ns = s as NSString
        var out = ""
        var last = 0
        for m in reference.matches(in: s, range: NSRange(location: 0, length: ns.length)) {
            out += ns.substring(with: NSRange(location: last, length: m.range.location - last))
            out += spell(ns.substring(with: m.range(at: 1)))
            last = m.range.location + m.range.length
        }
        return out + ns.substring(from: last)
    }

    private static func references(in config: [String: Any]) -> Set<String> {
        var out = Set<String>()
        func walk(_ v: Any) {
            switch v {
            case let s as String:
                let ns = s as NSString
                for m in reference.matches(in: s, range: NSRange(location: 0, length: ns.length)) { out.insert(ns.substring(with: m.range(at: 1))) }
            case let a as [Any]: a.forEach(walk)
            case let o as [String: Any]: o.values.forEach(walk)
            default: break
            }
        }
        walk(config)
        return out
    }

    private static func mapStrings(_ v: Any, _ f: (String) -> String) -> Any {
        switch v {
        case let s as String: f(s)
        case let a as [Any]: a.map { mapStrings($0, f) }
        case let o as [String: Any]: o.mapValues { mapStrings($0, f) }
        default: v
        }
    }

    private static func isRemote(_ c: [String: Any]) -> Bool { c["url"] != nil }

    static func cursor(_ c: [String: Any]) -> [String: Any] {
        var o = mapStrings(c, { respell($0) { $0 == "HOME" ? "${userHome}" : "${env:\($0)}" } }) as? [String: Any] ?? [:]
        if isRemote(o) { o["type"] = nil } else { o["type"] = "stdio" }
        return o
    }

    static func vscode(_ c: [String: Any]) -> [String: Any] {
        var o = mapStrings(c, { respell($0) { $0 == "HOME" ? "${userHome}" : "${env:\($0)}" } }) as? [String: Any] ?? [:]
        if !isRemote(o) { o["type"] = "stdio" }
        return o
    }

    static func gemini(_ c: [String: Any]) -> [String: Any] {
        var o = c
        o["type"] = nil
        if let url = o["url"], (c["type"] as? String) != "sse" {
            o["url"] = nil
            o["httpUrl"] = url
        }
        return o
    }

    /// `[mcp_servers.<name>]` tables. Env values are forwarded by name (`env_vars`), each header
    /// read from the variable the pack named (`env_http_headers`). SSE servers are left out.
    static func codex(_ servers: [(name: String, config: [String: Any])]) -> (String, [String]) {
        var lines = ["# Codex: merge into ~/.codex/config.toml. Values come from these environment variables."]
        var skipped: [String] = []
        for (name, c) in servers {
            if (c["type"] as? String) == "sse" { skipped.append(name); continue }
            lines.append("")
            lines.append("[mcp_servers.\(tomlKey(name))]")
            if let url = c["url"] as? String {
                lines.append("url = " + toml(url))
                if let headers = c["headers"] as? [String: String], !headers.isEmpty {
                    let pairs = headers.keys.sorted().map { key -> String in
                        let ref = references(in: ["v": headers[key] ?? ""]).first ?? ConfigPack.envName(key)
                        return tomlKey(key) + " = " + toml(ref)
                    }
                    lines.append("env_http_headers = { " + pairs.joined(separator: ", ") + " }")
                }
            } else {
                if let cmd = c["command"] as? String { lines.append("command = " + toml(cmd)) }
                if let args = c["args"] as? [String], !args.isEmpty { lines.append("args = [" + args.map(toml).joined(separator: ", ") + "]") }
                if let env = c["env"] as? [String: Any], !env.isEmpty {
                    lines.append("env_vars = [" + env.keys.sorted().map(toml).joined(separator: ", ") + "]")
                }
            }
        }
        return (lines.joined(separator: "\n") + "\n", skipped)
    }

    /// A TOML basic string.
    static func toml(_ s: String) -> String {
        var out = "\""
        for u in s.unicodeScalars {
            switch u {
            case "\\": out += "\\\\"
            case "\"": out += "\\\""
            case "\n": out += "\\n"
            case "\t": out += "\\t"
            case "\r": out += "\\r"
            default:
                if u.value < 0x20 || u.value == 0x7F { out += String(format: "\\u%04X", u.value) } else { out.unicodeScalars.append(u) }
            }
        }
        return out + "\""
    }

    static func tomlKey(_ s: String) -> String {
        s.range(of: #"^[A-Za-z0-9_-]+$"#, options: .regularExpression) != nil ? s : toml(s)
    }

    private static func dict(_ servers: [(name: String, config: [String: Any])], _ f: ([String: Any]) -> [String: Any]) -> [String: Any] {
        Dictionary(servers.map { ($0.name, f($0.config) as Any) }, uniquingKeysWith: { a, _ in a })
    }

    private static func json(_ o: Any) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: o, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])) ?? Data()
        return String(decoding: data, as: UTF8.self) + "\n"
    }

    // MARK: Documents

    static func agentsMd(instructions: String?, index: [(name: String, description: String)], env: [String]) -> String {
        var out = instructions.map { $0.trimmingCharacters(in: .newlines) + "\n\n" } ?? "# Agent setup\n\n"
        if !index.isEmpty {
            out += "## Skills in this setup\n\n" + index.map { "- **\($0.name)**: \($0.description.replacingOccurrences(of: "\n", with: " "))" }.joined(separator: "\n") + "\n\n"
        }
        if !env.isEmpty {
            out += "## Environment variables it expects\n\n" + env.map { "- `\($0)`" }.joined(separator: "\n") + "\n"
        }
        return out
    }

    static func readme(hasMCP: Bool, hasSkills: Bool, env: [String], renamed: [String]) -> String {
        var out = """
        # For any coding agent

        This folder holds the pack's setup in the formats other coding agents read. It was made from \
        a Claude Code folder by Sanduhr; `../files/` is the same setup for Claude Code. `~` is your \
        home folder (`%USERPROFILE%` on Windows).

        - **AGENTS.md**: instructions. Codex reads `~/.codex/AGENTS.md` and a repo's root \
        `AGENTS.md`; most other agents read a repo's root `AGENTS.md`. Gemini CLI reads it once \
        `"context": {"fileName": "AGENTS.md"}` is in its settings.

        """
        if hasSkills {
            out += """
            - **skills/**: Agent Skills, one folder each. Copy them into `~/.agents/skills/` for Codex \
            and Gemini CLI, `~/.claude/skills/` for Claude Code, or the skills folder your agent \
            documents. Agents and commands from Claude Code are skills here too.

            """
        }
        if hasMCP {
            out += """
            - **mcp/**: MCP servers, one file per agent. Merge the servers into:
              - `codex.toml` → `~/.codex/config.toml`
              - `gemini.json` → `~/.gemini/settings.json`
              - `cursor.json` → `~/.cursor/mcp.json`
              - `vscode.json` → your VS Code user `mcp.json` (MCP: Open User Configuration)
              - `mcp.json` → a project's `.mcp.json` (Claude Code, Copilot CLI and others)

            """
        }
        if !env.isEmpty {
            out += "\n## Set these environment variables first\n\nThe pack carries their names, never their values.\n\n"
                + env.map { "- `\($0)`" }.joined(separator: "\n") + "\n"
        }
        if !renamed.isEmpty {
            out += "\n## Renamed to fit the Agent Skills standard\n\n" + renamed.map { "- " + $0 }.joined(separator: "\n") + "\n"
        }
        out += "\nWhat had no equivalent is listed in `LEFT-OUT.md`, when there is any.\n"
        return out
    }
}

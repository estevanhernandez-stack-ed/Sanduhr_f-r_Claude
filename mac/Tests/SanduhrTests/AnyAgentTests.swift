import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice H: a config pack's any-agent/ folder, on made-up rows only.
@Suite("Packs, any-agent folder")
struct AnyAgentTests {
    private func file(_ kind: ConfigPack.Item.Kind, _ name: String, _ path: String, _ text: String) -> ConfigPack.Item {
        var item = ConfigPack.Item(kind: kind, name: name)
        item.files = [.init(path: path, text: text)]
        return item
    }

    private func server(_ name: String, _ config: [String: Any]) -> ConfigPack.Item {
        var item = ConfigPack.Item(kind: .mcpServer, name: name)
        item.json = try? JSONSerialization.data(withJSONObject: ["name": name, "config": config] as [String: Any])
        return item
    }

    private func text(_ files: [ConfigPack.File], _ path: String) -> String? {
        files.first { $0.path == "any-agent/" + path }?.text
    }

    private var rows: [ConfigPack.Item] {
        var env = ConfigPack.Item(kind: .env, name: "env")
        env.json = try? JSONSerialization.data(withJSONObject: [["name": "MY_FLAG", "needs_value": true]])
        return [
            file(.skill, "Release_Notes", "files/skills/Release_Notes/SKILL.md", "---\nname: Release_Notes\ndescription: Writes release notes.\n---\n\nSteps.\n"),
            file(.agent, "reviewer", "files/agents/reviewer.md", "---\nname: reviewer\ndescription: |\n  Reviews a diff.\n  Use after edits.\ntools: Read, Grep\nmodel: sonnet\n---\n\nYou review code.\n"),
            file(.command, "ship", "files/commands/ship.md", "---\ndescription: \"Ships it: tags and pushes.\"\nargument-hint: <version>\n---\nRelease $ARGUMENTS now.\n"),
            server("docs", ["type": "http", "url": "https://docs.example.com/mcp", "headers": ["Authorization": "${AUTHORIZATION}"]]),
            server("local", ["type": "stdio", "command": "${HOME}/bin/srv", "args": ["--fast"], "env": ["API_TOKEN": "${API_TOKEN}"]]),
            server("old", ["type": "sse", "url": "https://old.example.com/sse"]),
            ConfigPack.Item(kind: .plugin, name: "swift-lsp", detail: "from claude-plugins-official"),
            env,
        ]
    }

    @Test func skillsKeepTheirFilesAndFitTheStandard() {
        let out = AnyAgent.files(rows)
        let skill = text(out, "skills/release-notes/SKILL.md")
        #expect(skill?.contains("name: release-notes\n") == true && skill?.contains("description: Writes release notes.") == true)
        #expect(skill?.hasSuffix("Steps.\n") == true)
        #expect(text(out, "README.md")?.contains("`Release_Notes` → `release-notes`") == true)
        #expect(AnyAgent.skillName("PDF--Processing!") == "pdf-processing" && AnyAgent.skillName("---") == "skill")
        #expect(AnyAgent.skillName(String(repeating: "a", count: 80)).count == 64)
    }

    @Test func agentsAndCommandsBecomeSkills() throws {
        let out = AnyAgent.files(rows)
        let agent = try #require(text(out, "skills/reviewer/SKILL.md"))
        let fm = try #require(AnyAgent.frontmatter(agent))
        #expect(fm.fields.map(\.key) == ["name", "description", "compatibility"])
        #expect(fm.fields[1].value == "Reviews a diff.\nUse after edits.")
        #expect(fm.body == "You review code.\n")
        let shipped = try #require(text(out, "skills/ship/SKILL.md"))
        let command = try #require(AnyAgent.frontmatter(shipped))
        #expect(command.fields[1].value == "Ships it: tags and pushes. Use when the user asks for /ship.")
        #expect(command.body.contains("Arguments: <version>") && command.body.contains("`$ARGUMENTS`") && command.body.contains("Release $ARGUMENTS now."))
        let left = try #require(text(out, "LEFT-OUT.md"))
        #expect(left.contains("Agent `reviewer`: its tools, model") && left.contains("Plugin `swift-lsp`") && left.contains("`old` in codex.toml"))
    }

    @Test func eachAgentGetsItsOwnMCPFile() throws {
        let out = AnyAgent.files(rows)
        func object(_ path: String) throws -> [String: Any] {
            let t = try #require(text(out, path))
            return try #require(try JSONSerialization.jsonObject(with: Data(t.utf8)) as? [String: Any])
        }
        let cursor = try #require(try object("mcp/cursor.json")["mcpServers"] as? [String: [String: Any]])
        #expect(cursor["local"]?["command"] as? String == "${userHome}/bin/srv")
        #expect((cursor["local"]?["env"] as? [String: String])?["API_TOKEN"] == "${env:API_TOKEN}")
        #expect((cursor["docs"]?["headers"] as? [String: String])?["Authorization"] == "${env:AUTHORIZATION}" && cursor["docs"]?["type"] == nil)
        let vscode = try #require(try object("mcp/vscode.json")["servers"] as? [String: [String: Any]])
        #expect(vscode["local"]?["type"] as? String == "stdio" && vscode["docs"]?["type"] as? String == "http")
        let gemini = try #require(try object("mcp/gemini.json")["mcpServers"] as? [String: [String: Any]])
        #expect(gemini["docs"]?["httpUrl"] as? String == "https://docs.example.com/mcp" && gemini["docs"]?["url"] == nil)
        #expect(gemini["old"]?["url"] as? String == "https://old.example.com/sse" && gemini["local"]?["type"] == nil)
        let claude = try #require(try object("mcp/mcp.json")["mcpServers"] as? [String: [String: Any]])
        #expect((claude["local"]?["env"] as? [String: String])?["API_TOKEN"] == "${API_TOKEN}")

        let toml = try #require(text(out, "mcp/codex.toml"))
        #expect(toml.contains("[mcp_servers.docs]\nurl = \"https://docs.example.com/mcp\"\nenv_http_headers = { Authorization = \"AUTHORIZATION\" }"))
        #expect(toml.contains("[mcp_servers.local]\ncommand = \"${HOME}/bin/srv\"\nargs = [\"--fast\"]\nenv_vars = [\"API_TOKEN\"]"))
        #expect(!toml.contains("mcp_servers.old"))
        #expect(AnyAgent.toml("a\"b\\c\n") == #""a\"b\\c\n""#)
        #expect(AnyAgent.tomlKey("my server") == "\"my server\"")
    }

    @Test func agentsMdIsAnIndexOrTheInstructions() throws {
        let out = AnyAgent.files(rows)
        let index = try #require(text(out, "AGENTS.md"))
        #expect(index.hasPrefix("# Agent setup\n") && index.contains("- **reviewer**:") && index.contains("- `API_TOKEN`") && index.contains("- `MY_FLAG`"))
        #expect(!index.contains("`HOME`"))
        let withInstructions = AnyAgent.files(rows + [file(.instructions, "CLAUDE.md", "files/CLAUDE.md", "# House rules\n\nBe brief.\n")])
        #expect(text(withInstructions, "AGENTS.md")?.hasPrefix("# House rules\n\nBe brief.\n\n## Skills in this setup") == true)
    }

    @Test func thePackListsItAndAReaderVerifiesIt() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let url = URL(fileURLWithPath: h.at("p.sanduhrpack"))
        try ConfigPack.write(rows, label: "Personal", to: url, version: "2.21.0", anyAgent: true)
        let pack = try ModPackReader.read(url)
        #expect(pack.files.keys.contains("any-agent/skills/reviewer/SKILL.md") && pack.files.keys.contains("files/agents/reviewer.md"))
        // Import reads files/ only: nothing under any-agent/ becomes a row.
        let rowNames = ConfigImport.plan(pack, seat: h.at(".claude-new")).map(\.name)
        #expect(!rowNames.contains("release-notes") && rowNames.contains("reviewer"))

        let without = URL(fileURLWithPath: h.at("q.sanduhrpack"))
        try ConfigPack.write(rows, label: "Personal", to: without, version: "2.21.0")
        #expect(try !ModPackReader.read(without).files.keys.contains { $0.hasPrefix("any-agent/") })
    }

    @Test func itStaysOutWhenThePackWouldPassTheCaps() {
        let many = (0..<130).map { file(.agent, "a\($0)", "files/agents/a\($0).md", "---\ndescription: x\n---\nbody\n") }
        #expect(ConfigPack.anyAgentFiles(many) == nil)
        #expect(ConfigPack.anyAgentFiles(Array(many.prefix(20))) != nil)
        #expect(AnyAgent.entryCount(["a/b/c.md", "a/d.md", "e.md"]) == 5)
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice C: a personal seat described as a config pack. On made-up homes only.
@Suite("Packs, config export")
struct ConfigPackTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    /// A seat with a little of everything, and the scan Mods & Config would make of it.
    private func seat(_ h: TempHome) -> (String, SeatScan) {
        let s = ".claude-personal"
        let home = AccountData.normalized(h.at(""))
        h.dir("\(s)/agents"); h.dir("\(s)/commands"); h.dir("\(s)/skills/notes/scripts"); h.dir("\(s)/plugins")
        h.file("\(s)/CLAUDE.md", "Be brief. Notes live in \(home)/notes.\n")
        h.file("\(s)/agents/reviewer.md", "---\nname: reviewer\ndescription: Reviews.\n---\nRead the diff.\n")
        h.file("\(s)/agents/leaky.md", "---\nname: leaky\n---\nUse key sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789 here.\n")
        h.file("\(s)/commands/ship.md", "Ship it.\n")
        h.file("\(s)/skills/notes/SKILL.md", "---\nname: notes\ndescription: Notes.\n---\nWrite notes.\n")
        h.file("\(s)/skills/notes/scripts/run.sh", "echo notes\n")
        h.file("\(s)/settings.json", """
        {"permissions": {"allow": ["Read(\(home)/notes/**)"]},
         "model": "opus",
         "hooks": {"Stop": [{"hooks": [{"type": "command", "command": "open -g sanduhr://claude-code?event=stop"}]},
                            {"hooks": [{"type": "command", "command": "\(home)/bin/notify.sh"}]}],
                   "PreToolUse": [{"hooks": [{"type": "command", "command": "/Volumes/Work/guard.sh"}]}]},
         "env": {"GITHUB_TOKEN": "ghp_secretsecretsecret", "CLAUDE_CODE_PLUGIN_DIRS": "/x", "MY_FLAG": "1"},
         "enabledPlugins": {"swift-lsp@claude-plugins-official": true, "local-thing@my-dir": true}}
        """)
        h.file("\(s)/plugins/known_marketplaces.json", """
        {"claude-plugins-official": {"source": {"source": "github", "repo": "anthropics/claude-plugins-official"}},
         "my-dir": {"source": {"source": "directory", "path": "\(home)/market"}}}
        """)
        h.file("\(s)/.claude.json", """
        {"oauthAccount": {"emailAddress": "someone@example.com"},
         "mcpServers": {
           "docs": {"type": "http", "url": "https://docs.example.com/mcp", "headers": {"Authorization": "Bearer abc123"}},
           "files": {"command": "npx", "args": ["-y", "files-server", "\(home)/work"], "env": {"FILES_KEY": "s3cr3t"}},
           "leaky": {"command": "npx", "args": ["server", "--token", "abc"]},
           "sanduhr": {"command": "/usr/bin/python3", "args": ["\(home)/Library/Application Support/Sanduhr/integrations/sanduhr_mcp.py"]}}}
        """)
        let root = h.at(s)
        var scan = SeatScan(seat: root)
        scan.agents = [SeatEntry(name: "reviewer", description: "", scope: .seat, path: root + "/agents/reviewer.md"),
                       SeatEntry(name: "leaky", description: "", scope: .seat, path: root + "/agents/leaky.md"),
                       SeatEntry(name: "synced-one", description: "", scope: .synced, path: root + "/skills/synced/x.md")]
        scan.commands = [SeatEntry(name: "ship", description: "", scope: .seat, path: root + "/commands/ship.md")]
        scan.skills = [SeatEntry(name: "notes", description: "", scope: .seat, path: root + "/skills/notes/SKILL.md")]
        scan.plugins = [ModItem(folder: root, name: "swift-lsp", path: nil, origin: .installed(marketplace: "claude-plugins-official")),
                        ModItem(folder: root, name: "local-thing", path: nil, origin: .installed(marketplace: "my-dir")),
                        ModItem(folder: root, name: "sanduhr-meters", path: nil, origin: .pluginDirs)]
        return (root, scan)
    }

    private func item(_ items: [ConfigPack.Item], _ kind: ConfigPack.Item.Kind, _ name: String) throws -> ConfigPack.Item {
        try #require(items.first { $0.kind == kind && $0.name == name }, "\(kind) \(name)")
    }

    private func json(_ item: ConfigPack.Item) throws -> Any {
        try JSONSerialization.jsonObject(with: try #require(item.json))
    }

    @Test func theScanFindsSecretsAndLeavesTheRest() {
        #expect(ConfigPack.secretLine(in: "one\ntoken sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789\n") == 2)
        #expect(ConfigPack.secretLine(in: "curl -H 'Authorization: Bearer abc' x") == 1)
        #expect(ConfigPack.secretLine(in: "x\ny\nAKIA9Zq7Lm2Pw4Xr8Tn1Vb6Yc3Kd5Hs0Jg7Q\n") == 3)
        // A sha256 is hex: long, but not random enough to be a secret here.
        #expect(ConfigPack.secretLine(in: "sha256 9a7ab340200de9091165f11bd8d6a285f8ddc8fc227ab03a6d5b52a132ab2215") == nil)
        #expect(ConfigPack.secretLine(in: "Read the diff and say what changed.\nKeep it short.") == nil)
    }

    @Test func pathsUnderHomeTravelAndOthersDont() {
        let home = "/Users/someone"
        #expect(ConfigPack.homeRelative("Read(/Users/someone/notes/**)", home: home) == "Read(~/notes/**)")
        #expect(ConfigPack.machinePath(in: "~/bin/notify.sh", home: home) == nil)
        #expect(ConfigPack.machinePath(in: "/usr/bin/env python3 x", home: home) == nil)
        #expect(ConfigPack.machinePath(in: "/opt/homebrew/bin/node", home: home) == nil)
        #expect(ConfigPack.machinePath(in: "/Volumes/Work/guard.sh --fast", home: home) == "/Volumes/Work/guard.sh")
        #expect(ConfigPack.envName("X-Api-Key") == "X_API_KEY")
    }

    @Test func theSeatIsDescribedNotCopied() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (root, scan) = seat(h)
        let items = ConfigPack.plan(seat: root, scan: scan, keptOn: [], kept: KeptMods(dir: URL(fileURLWithPath: h.at("support/mods"))), home: h.at(""))

        // CLAUDE.md travels only when chosen, with the home folder as ~.
        let md = try item(items, .instructions, "CLAUDE.md")
        #expect(!md.onByDefault && md.canTravel && md.files.first?.text == "Be brief. Notes live in ~/notes.\n")
        // Files: the seat's own, a skill with its folder, a leaky agent blocked by line.
        #expect(try item(items, .agent, "reviewer").files.map(\.path) == ["files/agents/reviewer.md"])
        #expect(try item(items, .agent, "leaky").blocked == "agents/leaky.md line 4 looks like a secret.")
        #expect(!items.contains { $0.name == "synced-one" })
        #expect(try item(items, .skill, "notes").files.map(\.path) == ["files/skills/notes/SKILL.md", "files/skills/notes/scripts/run.sh"])
        #expect(try item(items, .command, "ship").canTravel)

        // Plugins by marketplace source; a folder marketplace stays; Sanduhr's mod isn't listed.
        let lsp = try json(try item(items, .plugin, "swift-lsp")) as? [String: Any]
        #expect(((lsp?["marketplace"] as? [String: Any])?["source"] as? [String: Any])?["repo"] as? String == "anthropics/claude-plugins-official")
        #expect(try item(items, .plugin, "local-thing").blocked == "my-dir is a folder on this Mac.")
        #expect(!items.contains { $0.name == "sanduhr-meters" })

        // MCP: values become references, a secret in args blocks, Sanduhr's own isn't listed.
        let docs = (try json(try item(items, .mcpServer, "docs")) as? [String: Any])?["config"] as? [String: Any]
        #expect(docs?["headers"] as? [String: String] == ["Authorization": "${AUTHORIZATION}"])
        let files = (try json(try item(items, .mcpServer, "files")) as? [String: Any])?["config"] as? [String: Any]
        #expect(files?["env"] as? [String: String] == ["FILES_KEY": "${FILES_KEY}"])
        #expect(files?["args"] as? [String] == ["-y", "files-server", "${HOME}/work"])
        #expect(try item(items, .mcpServer, "leaky").blocked != nil)
        #expect(!items.contains { $0.kind == .mcpServer && $0.name == "sanduhr" })

        // Settings: home paths as ~, Sanduhr's hooks dropped, a /Volumes path blocks hooks.
        let perms = try json(try item(items, .setting, "permissions")) as? [String: Any]
        #expect((perms?["permissions"] as? [String: Any])?["allow"] as? [String] == ["Read(~/notes/**)"])
        #expect(try item(items, .setting, "hooks").blocked == "It names /Volumes/Work/guard.sh, a path on this Mac only.")
        #expect(try item(items, .setting, "model").canTravel)
        #expect(!items.contains { $0.name == "enabledPlugins" })
        // Env by name only; this Mac's own names left out.
        #expect(try json(try item(items, .env, "env")) as? [String] == ["GITHUB_TOKEN", "MY_FLAG"])
    }

    @Test func thePackHoldsNoValueSecretOrPathOfThisMac() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (root, scan) = seat(h)
        let items = ConfigPack.plan(seat: root, scan: scan, keptOn: [], kept: KeptMods(dir: URL(fileURLWithPath: h.at("support/mods"))), home: h.at(""))
        let pack = URL(fileURLWithPath: h.at("out.sanduhrpack"))
        // Everything, blocked rows included: write leaves those out by itself.
        try ConfigPack.write(items, label: "Personal", to: pack, version: "2.21.0", now: Self.at)

        let out = URL(fileURLWithPath: h.at("unzipped"))
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        p.arguments = ["-x", "-k", pack.path, out.path]
        try p.run()
        p.waitUntilExit()
        let manifest = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: out.appendingPathComponent("sanduhr-pack.json"))) as? [String: Any])
        #expect(manifest["kind"] as? String == "config" && manifest["label"] as? String == "Personal")
        #expect((manifest["env"] as? [[String: Any]])?.map { $0["name"] as? String } == ["GITHUB_TOKEN", "MY_FLAG"])
        #expect((manifest["settings"] as? [String: Any])?["hooks"] == nil)
        #expect(!FileManager.default.fileExists(atPath: out.appendingPathComponent("files/agents/leaky.md").path))

        var everything = ""
        for rel in try FileManager.default.subpathsOfDirectory(atPath: out.path) {
            if let t = try? String(contentsOf: out.appendingPathComponent(rel), encoding: .utf8) { everything += t }
        }
        for leak in [h.at(""), "ghp_secret", "s3cr3t", "Bearer abc123", "sk-ant-api03", "someone@example.com", "/Volumes/Work", "sanduhr_mcp", ".claude-personal"] {
            #expect(!everything.contains(leak), "the pack holds \(leak)")
        }
        #expect(manifest["from"] == nil)
    }

    @Test func aPackFromAWorkFolderSaysSo() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (root, scan) = seat(h)
        let items = ConfigPack.plan(seat: root, scan: scan, keptOn: [], kept: KeptMods(dir: URL(fileURLWithPath: h.at("support/mods"))), home: h.at(""))
        let work = URL(fileURLWithPath: h.at("work.sanduhrpack")), personal = URL(fileURLWithPath: h.at("personal.sanduhrpack"))
        try ConfigPack.write(items, label: "Work", to: work, version: "2.21.0", now: Self.at, fromWork: true)
        try ConfigPack.write(items, label: "Personal", to: personal, version: "2.21.0", now: Self.at)
        #expect(try ModPackReader.read(work).fromWork)
        #expect(try !ModPackReader.read(personal).fromWork)
    }
}

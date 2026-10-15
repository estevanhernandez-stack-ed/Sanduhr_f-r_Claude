import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice E: plugins and MCP servers put in through `claude`, against a fake `claude`
/// that records its arguments and folder. On made-up homes only.
@Suite("Packs, plugins and MCP through the CLI")
struct ClaudeCLITests {
    /// A fake `claude`: logs `CLAUDE_CONFIG_DIR|args` a line per run and answers as 2.1 does.
    /// A plugin id starting `bad` fails to install.
    private func fake(_ h: TempHome) throws -> (ClaudeCLI, () -> [String]) {
        let log = h.at("calls.log")
        h.dir("bin")
        h.file("bin/claude", """
        #!/bin/bash
        echo "${CLAUDE_CONFIG_DIR:-default}|$*" >> '\(log)'
        case "$1 $2 $3" in
          "plugin marketplace list") echo '[]' ;;
          "plugin marketplace add") echo '{"command":"marketplace-add","outcome":"ok","marketplace":"demo-market"}' ;;
          "plugin marketplace remove") echo '{"command":"marketplace-remove","outcome":"ok"}' ;;
          "plugin list --json") echo '[]' ;;
          "plugin install bad"*) echo '{"command":"install","outcome":"failed","message":"Plugin \\"bad\\" not found in marketplace"}'; exit 1 ;;
          "plugin install"*) echo "{\\"command\\":\\"install\\",\\"outcome\\":\\"ok\\",\\"plugin\\":\\"$3\\"}" ;;
          "plugin uninstall"*) echo '{"command":"uninstall","outcome":"ok"}' ;;
          "mcp add-json"*) echo "Added MCP server $5 to user config" ;;
          "mcp remove"*) echo "Removed MCP server $4" ;;
          *) echo "unknown" >&2; exit 2 ;;
        esac
        """)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: h.at("bin/claude"))
        let calls = { (try? String(contentsOfFile: log, encoding: .utf8))?.split(separator: "\n").map(String.init) ?? [] }
        return (ClaudeCLI(claude: h.at("bin/claude"), home: h.at("")), calls)
    }

    @Test func theCommandsAreFixedAndNeverAcceptAPrompt() {
        let all: [ClaudeCLI.Command] = [.marketplaceList, .marketplaceAdd(source: "o/r"), .marketplaceRemove(name: "m"), .pluginList,
                                        .pluginInstall(id: "p@m"), .pluginUninstall(id: "p@m"), .mcpAddJSON(name: "s", json: "{}"), .mcpRemove(name: "s")]
        for c in all {
            #expect(!c.arguments.contains("-y") && !c.arguments.contains("--yes"))
            #expect(["plugin", "mcp"].contains(c.arguments[0]))
        }
        #expect(ClaudeCLI.Command.pluginInstall(id: "p@m").arguments == ["plugin", "install", "p@m", "--scope", "user", "--json"])
        #expect(ClaudeCLI.Command.mcpAddJSON(name: "s", json: "{}").arguments == ["mcp", "add-json", "-s", "user", "s", "{}"])
    }

    @Test func theFolderIsChosenByConfigDirAndNothingElseClaudeLeaks() {
        let cli = ClaudeCLI(claude: "/x/bin/claude", home: "/Users/someone")
        let base = ["PATH": "/usr/bin", "CLAUDE_CONFIG_DIR": "/Users/someone/.claude-work", "CLAUDE_CODE_PLUGIN_DIRS": "/p", "CLAUDECODE": "1", "LANG": "en"]
        let personal = cli.environment(seat: "/Users/someone/.claude-personal", base: base)
        #expect(personal["CLAUDE_CONFIG_DIR"] == "/Users/someone/.claude-personal")
        #expect(personal["CLAUDE_CODE_PLUGIN_DIRS"] == nil && personal["CLAUDECODE"] == nil && personal["LANG"] == "en")
        #expect(personal["PATH"]?.hasPrefix("/x/bin:") == true)
        // The default folder: no variable, so its .claude.json in the home folder is the one used.
        #expect(cli.environment(seat: "/Users/someone/.claude", base: base)["CLAUDE_CONFIG_DIR"] == nil)
    }

    @Test func aPluginAndAServerGoInAndComeBackOut() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (cli, calls) = try fake(h)
        let seat = h.at(".claude-new")
        h.dir(".claude-new")
        let jobs: [ConfigImport.Job] = [
            .plugin(id: "swift-lsp@demo-market", marketplace: "demo-market", source: "someone/demo-market"),
            .plugin(id: "bad@demo-market", marketplace: "demo-market", source: "someone/demo-market"),
            .mcp(name: "docs", json: #"{"type":"http","url":"https://docs.example.com/mcp"}"#, sha256: ConfigImport.canonicalSHA(["type": "http", "url": "https://docs.example.com/mcp"])),
        ]
        let (steps, failures) = ConfigImport.run(jobs, seat: seat, cli: cli)
        #expect(steps.map(\.kind) == [.marketplace, .plugin, .mcpServer])
        #expect(steps.map(\.name) == ["demo-market", "swift-lsp@demo-market", "docs"])
        #expect(failures.count == 1 && failures[0].hasPrefix("bad@demo-market: ") && failures[0].contains("not found"))
        // The marketplace is added once, and every run names the folder.
        #expect(calls().allSatisfy { $0.hasPrefix(seat + "|") })
        #expect(calls().filter { $0.contains("marketplace add") }.count == 1)

        // Claude Code wrote the server into the folder's .claude.json.
        h.file(".claude-new/.claude.json", #"{"mcpServers": {"docs": {"type": "http", "url": "https://docs.example.com/mcp"}}}"#)
        let record = ConfigImport.Record(id: "r", seat: seat, label: "Demo", packSHA256: "x", at: Date(), steps: steps)
        let before = calls().count
        #expect(ConfigImport.undoCLI(record, cli: cli, home: h.at("")).isEmpty)
        let undone = calls().dropFirst(before).map { $0.split(separator: "|", maxSplits: 1)[1] }
        #expect(undone == ["mcp remove -s user docs", "plugin uninstall swift-lsp@demo-market --scope user --json",
                           "plugin list --json", "plugin marketplace remove demo-market --scope user --json"])
    }

    @Test func aServerChangedSinceIsLeftAndNamed() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (cli, calls) = try fake(h)
        h.dir(".claude-new")
        h.file(".claude-new/.claude.json", #"{"mcpServers": {"docs": {"type": "http", "url": "https://elsewhere.example.com/mcp"}}}"#)
        let record = ConfigImport.Record(id: "r", seat: h.at(".claude-new"), label: "Demo", packSHA256: "x", at: Date(),
                                         steps: [.init(kind: .mcpServer, name: "docs", sha256: ConfigImport.canonicalSHA(["type": "http"]))])
        #expect(ConfigImport.undoCLI(record, cli: cli, home: h.at("")) == ["MCP server docs, changed since"])
        #expect(calls().isEmpty)
        // No claude: each is named, nothing runs.
        #expect(ConfigImport.undoCLI(record, cli: nil, home: h.at("")).first?.contains("claude wasn't found") == true)
    }

    @Test func rowsRunThroughClaudeOnlyWhenItCan() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let home = AccountData.normalized(h.at(""))
        let s = "source/.claude-personal"
        h.dir("\(s)/plugins")
        h.file("\(s)/plugins/known_marketplaces.json", #"{"claude-plugins-official": {"source": {"source": "github", "repo": "anthropics/claude-plugins-official"}}}"#)
        h.file("\(s)/.claude.json", #"{"mcpServers": {"docs": {"type": "http", "url": "https://docs.example.com/mcp"}}}"#)
        let root = h.at(s)
        var scan = SeatScan(seat: root)
        scan.plugins = [ModItem(folder: root, name: "swift-lsp", path: nil, origin: .installed(marketplace: "claude-plugins-official")),
                        ModItem(folder: root, name: "other", path: nil, origin: .installed(marketplace: "claude-plugins-official"))]
        let items = ConfigPack.plan(seat: root, scan: scan, keptOn: [], kept: KeptMods(dir: URL(fileURLWithPath: h.at("k"))), home: home)
        let url = URL(fileURLWithPath: h.at("p.sanduhrpack"))
        try ConfigPack.write(items, label: "Personal", to: url, version: "2.21.0", now: Date(timeIntervalSince1970: 1_790_000_000))
        let pack = try ModPackReader.read(url)

        let target = "target/.claude-personal"
        h.dir("\(target)/plugins")
        h.file("\(target)/plugins/installed_plugins.json", #"{"version": 2, "plugins": {"other@claude-plugins-official": []}}"#)
        func status(_ rows: [ConfigImport.Row], _ name: String) -> ConfigImport.Row.Status? { rows.first { $0.name == name }?.status }

        let ready = ConfigImport.plan(pack, seat: h.at(target), home: home, cli: .ready)
        guard case .install(let cmd)? = status(ready, "swift-lsp") else { Issue.record("swift-lsp installs"); return }
        #expect(cmd.contains("claude plugin install 'swift-lsp@claude-plugins-official'"))
        #expect(ready.first { $0.name == "swift-lsp" }?.choosable == true)
        #expect(ready.first { $0.name == "swift-lsp" }?.job == .plugin(id: "swift-lsp@claude-plugins-official", marketplace: "claude-plugins-official", source: "anthropics/claude-plugins-official"))
        #expect(status(ready, "other") == .same)
        guard case .install? = status(ready, "docs") else { Issue.record("docs adds"); return }

        h.file("\(target)/.claude.json", #"{"mcpServers": {"docs": {"type": "http", "url": "https://docs.example.com/mcp"}}}"#)
        #expect(status(ConfigImport.plan(pack, seat: h.at(target), home: home, cli: .ready), "docs") == .same)
        h.file("\(target)/.claude.json", #"{"mcpServers": {"docs": {"type": "stdio", "command": "x"}}}"#)
        #expect(status(ConfigImport.plan(pack, seat: h.at(target), home: home, cli: .ready), "docs") == .differs)

        guard case .refused? = status(ConfigImport.plan(pack, seat: h.at(target), home: home, cli: .running), "swift-lsp") else {
            Issue.record("refused while running"); return
        }
        guard case .command? = status(ConfigImport.plan(pack, seat: h.at(target), home: home, cli: .missing), "swift-lsp") else {
            Issue.record("a command without claude"); return
        }
    }
}

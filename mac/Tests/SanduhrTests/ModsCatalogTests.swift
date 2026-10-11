import Foundation
import Testing
@testable import Sanduhr

/// Item 71, slice 1a: the Catalog. Each seat read from files in a made-up home under the temp
/// directory (SeatScanner), the cards built across seats (ConfigCatalog), the filters, the wall
/// in demo mode, and the counts state.yaml reports. Nothing reads the real ~/.claude*,
/// ~/.claude.json or app settings, and nothing runs the CLI.
@Suite("Mods & Config: the Catalog")
struct ModsCatalogTests {
    static func put(_ h: TempHome, _ relative: String, _ text: String) {
        ModsPageTests.put(h, relative, text)
    }

    /// A seat at `.claude-a` (made-up names throughout): two plugins on from two marketplaces
    /// (one of them also listed by the other marketplace), one off, one declared and not
    /// installed, one only in a project, marketplace listings with a plugin installed nowhere,
    /// agents, skills (one synced), commands, an output style, MCP servers at user, local and
    /// project scope, and a project with its own agent.
    static func seatA(_ h: TempHome) {
        let s = ".claude-a"
        let project = h.at("work/proj-one")
        put(h, "\(s)/settings.json", """
        {"enabledPlugins":{"lint@alpha":true,"lint@beta":true,"tidy@alpha":false,"ghost@alpha":true},
         "env":{"SOME_TOKEN":"x"}}
        """)
        put(h, "cache/lint-a/.claude-plugin/plugin.json", #"{"name":"lint","version":"1.0.0","description":"Lints things."}"#)
        put(h, "cache/lint-b/.claude-plugin/plugin.json", #"{"name":"lint","version":"1.1.0"}"#)
        put(h, "cache/tidy/.claude-plugin/plugin.json", #"{"name":"tidy"}"#)
        put(h, "cache/scoped/.claude-plugin/plugin.json", #"{"name":"scoped"}"#)
        put(h, "\(s)/plugins/installed_plugins.json", """
        {"version":2,"plugins":{
          "lint@alpha":[{"scope":"user","installPath":"\(h.at("cache/lint-a"))","version":"1.0.0"}],
          "lint@beta":[{"scope":"user","installPath":"\(h.at("cache/lint-b"))"}],
          "tidy@alpha":[{"scope":"user","installPath":"\(h.at("cache/tidy"))"}],
          "scoped@alpha":[{"scope":"project","projectPath":"\(project)","installPath":"\(h.at("cache/scoped"))"}]}}
        """)
        put(h, "\(s)/plugins/known_marketplaces.json", """
        {"alpha":{"source":{"source":"github","repo":"example/alpha"},"installLocation":"\(h.at("markets/alpha"))"},
         "beta":{"source":{"source":"directory","path":"/nowhere"},"installLocation":"\(h.at("markets/beta"))"}}
        """)
        put(h, "markets/alpha/.claude-plugin/marketplace.json", """
        {"name":"alpha","plugins":[{"name":"lint","description":"Lints things."},{"name":"unused","description":"Nobody installed me.","version":"0.1"}]}
        """)
        put(h, "\(s)/agents/reviewer.md", "---\nname: reviewer\ndescription: \"Reviews code.\"\n---\nBody.\n")
        put(h, "\(s)/skills/notes/SKILL.md", "---\nname: notes\ndescription: |\n  Takes notes\n  in one place.\n---\n")
        put(h, "\(s)/skills/synced/shared-skill/SKILL.md", "---\nname: shared-skill\ndescription: From the account.\n---\n")
        put(h, "\(s)/commands/ship.md", "---\ndescription: Ships it.\n---\n")
        put(h, "\(s)/commands/git/sync.md", "Syncs.\n")
        put(h, "\(s)/output-styles/terse.md", "---\nname: Terse\ndescription: Short answers.\n---\n")
        put(h, "\(s)/.claude.json", """
        {"mcpServers":{"files":{"command":"npx","args":["files-server","--token","abc"],"env":{"FILES_KEY":"v"}}},
         "projects":{"\(project)":{"mcpServers":{"local-db":{"type":"http","url":"https://db.example.com/mcp","headers":{"Authorization":"Bearer zz"}}}}},
         "oauthAccount":{"emailAddress":"someone@example.com"}}
        """)
        put(h, "work/proj-one/.mcp.json", #"{"mcpServers":{"tracker":{"command":"tracker-mcp"}}}"#)
        put(h, "work/proj-one/.claude/settings.json", #"{"enabledPlugins":{"tidy@alpha":true}}"#)
        put(h, "work/proj-one/.claude/agents/proj-agent.md", "---\nname: proj-agent\ndescription: Project only.\n---\n")
    }

    /// A second seat at `.claude-b`: lint from alpha only, an agent of the same name, and the
    /// files server too.
    static func seatB(_ h: TempHome) {
        let s = ".claude-b"
        put(h, "\(s)/settings.json", #"{"enabledPlugins":{"lint@alpha":true}}"#)
        put(h, "\(s)/plugins/installed_plugins.json", """
        {"version":2,"plugins":{"lint@alpha":[{"scope":"user","installPath":"\(h.at("cache/lint-a"))"}]}}
        """)
        put(h, "\(s)/agents/reviewer.md", "---\nname: reviewer\ndescription: Reviews code too.\n---\n")
        put(h, "\(s)/.claude.json", #"{"mcpServers":{"files":{"command":"npx","args":["files-server"]}}}"#)
    }

    static func seats(_ h: TempHome, marks: [String: SeatMark] = [:]) -> [(seat: Seat, scan: SeatScan)] {
        let store = MemoryDefaults()
        for (name, mark) in marks { SeatPreferences.setMark(mark, for: h.at(name), in: store) }
        SeatPreferences.setLabel("Alpha seat", for: h.at(".claude-a"), in: store)
        return [".claude-a", ".claude-b"].map { name in
            let path = h.at(name)
            return (Seats.seat(path, origin: .found, home: h.path, store: store),
                    SeatScanner.scan(seat: path, home: h.path, managedFolder: h.at("managed")))
        }
    }

    // MARK: Reading a seat

    @Test func readsEverythingASeatHoldsFromFilesAndWritesNothing() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatA(h)
        let before = TempHomeSnapshot(h)
        let scan = SeatScanner.scan(seat: h.at(".claude-a"), home: h.path, managedFolder: h.at("managed"))
        #expect(TempHomeSnapshot(h) == before)
        #expect(scan.settingsUnreadable == nil)

        let byKey = Dictionary(scan.plugins.compactMap { i in i.key.map { ($0, i) } }, uniquingKeysWith: { a, _ in a })
        #expect(byKey["lint@alpha"]?.state == .on && byKey["lint@beta"]?.state == .on)
        #expect(byKey["tidy@alpha"]?.state == .off)
        #expect(byKey["ghost@alpha"]?.state == .on && byKey["ghost@alpha"]?.path == nil)
        #expect(scan.projectPlugins["tidy@alpha"] == [h.at("work/proj-one")])
        #expect(scan.projectPlugins["scoped@alpha"] == [h.at("work/proj-one")])

        #expect(scan.marketplaces.map(\.name) == ["alpha", "beta"])
        #expect(scan.marketplaces[0].cached && scan.marketplaces[0].source == "github example/alpha")
        #expect(scan.marketplaces[0].plugins.map(\.name) == ["lint", "unused"])
        #expect(!scan.marketplaces[1].cached && !scan.marketplaces[1].present)

        #expect(scan.agents.map(\.name).sorted() == ["proj-agent", "reviewer"])
        #expect(scan.agents.first { $0.name == "reviewer" }?.description == "Reviews code.")
        #expect(scan.agents.first { $0.name == "proj-agent" }?.scope == .project)
        #expect(scan.skills.map(\.name) == ["notes", "shared-skill"])
        #expect(scan.skills[0].description == "Takes notes in one place.")
        #expect(scan.skills[1].scope == .synced)
        #expect(scan.commands.map(\.name) == ["git:sync", "ship"])
        #expect(scan.outputStyles.map(\.name) == ["Terse"])

        let servers = Dictionary(scan.mcpServers.map { ($0.name, $0) }, uniquingKeysWith: { a, _ in a })
        #expect(servers["files"]?.scope == .user)
        #expect(servers["files"]?.target == "npx files-server --token \(ConfigMask.masked)")
        #expect(servers["files"]?.envNames == ["FILES_KEY"])
        #expect(servers["local-db"]?.scope == .local && servers["local-db"]?.target == "db.example.com")
        #expect(servers["local-db"]?.headerNames == ["Authorization"])
        #expect(servers["tracker"]?.scope == .project)
        // Values never travel: not the env value, the header, or the token.
        let text = String(describing: scan)
        #expect(!text.contains("Bearer zz") && !text.contains("--token abc") && !text.contains("someone@example.com"))
    }

    @Test func anUnparseableSettingsFileIsUnreadableNotEmpty() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-x/settings.json", "{\n  \"model\": \"x\",\n  // a comment\n}\n")
        Self.put(h, ".claude-x/.claude.json", "{}")
        let scan = SeatScanner.scan(seat: h.at(".claude-x"), home: h.path, managedFolder: h.at("managed"))
        let why = scan.settingsUnreadable
        #expect(why != nil)
        #expect(why?.hasPrefix("isn't valid JSON") == true)
        #expect(why?.contains("line") == true)
        #expect(why?.contains("comment") == false)
        Self.put(h, ".claude-y/settings.json", "[1, 2]")
        #expect(SeatScanner.scan(seat: h.at(".claude-y"), home: h.path).settingsUnreadable == "has no JSON object at its top level")
        #expect(SeatScanner.scan(seat: h.at(".claude-none"), home: h.path).settingsUnreadable == nil)
    }

    @Test func frontmatterReadsPlainQuotedAndBlockValues() {
        let f = SeatScanner.frontmatter(text: "---\nname: 'one'\ndescription: >\n  Two\n  lines\nmodel: opus\n---\nbody: no\n")
        #expect(f == ["name": "one", "description": "Two lines", "model": "opus"])
        #expect(SeatScanner.frontmatter(text: "no frontmatter\nname: x\n").isEmpty)
        #expect(SeatScanner.frontmatter(text: "---\r\nname: crlf\r\n---\r\n") == ["name": "crlf"])
    }

    @Test func managedAndPluginServersAreListed() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "managed/managed-mcp.json", #"{"mcpServers":{"corp":{"type":"sse","url":"https://user:pw@mcp.example.com/sse"}}}"#)
        Self.put(h, "mods/srv/.claude-plugin/plugin.json", #"{"name":"srv"}"#)
        Self.put(h, "mods/srv/.mcp.json", #"{"mcpServers":{"srv-tool":{"command":"node","args":["x.js"]}}}"#)
        Self.put(h, ".claude-m/settings.json", #"{"env":{"CLAUDE_CODE_PLUGIN_DIRS":"\#(h.at("mods/srv"))"}}"#)
        let scan = SeatScanner.scan(seat: h.at(".claude-m"), home: h.path, managedFolder: h.at("managed"))
        #expect(scan.mcpServers.map(\.name).sorted() == ["corp", "srv-tool"])
        #expect(scan.mcpServers.first { $0.name == "corp" }?.scope == .managed)
        #expect(scan.mcpServers.first { $0.name == "corp" }?.target == "mcp.example.com")
        #expect(scan.mcpServers.first { $0.name == "srv-tool" }?.owner == "srv")
    }

    // MARK: The cards

    @Test func oneCardPerExtensionWithAChipPerSeat() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatA(h)
        Self.seatB(h)
        let cards = ConfigCatalog.build(Self.seats(h, marks: [".claude-a": .work, ".claude-b": .personal]))
        func card(_ kind: CatalogKind, _ name: String) throws -> CatalogCard {
            try #require(cards.first { $0.kind == kind && $0.name == name })
        }
        // Sanduhr's own mod comes first, even installed nowhere.
        #expect(cards.first?.isSanduhrs == true)
        #expect(cards.first?.isAvailable == true)

        // The same plugin from two marketplaces: one card, both sources, a duplicate badge.
        let lint = try card(.plugin, "lint")
        #expect(lint.sources == ["Marketplace alpha", "Marketplace beta"])
        #expect(lint.isDuplicate && lint.needsLook)
        #expect(lint.chips.map(\.label) == ["Alpha seat", ".claude-b"])
        #expect(lint.chips.map(\.state) == [.on, .on])
        #expect(lint.chips.map(\.mark) == [.work, .personal])
        #expect(lint.problems.contains { $0.contains("On twice in Alpha seat") })
        #expect(lint.description == "Lints things." && lint.version == "1.0.0")

        // Off at user scope but on in one project: "In 1 project", never its name.
        let tidy = try card(.plugin, "tidy")
        #expect(tidy.chips.map(\.state) == [.projects(1)])
        #expect(try card(.plugin, "scoped").chips.map(\.state) == [.projects(1)])
        let ghost = try card(.plugin, "ghost")
        #expect(ghost.chips.map(\.state) == [.declared])
        #expect(ghost.problems == ["Turned on in Alpha seat but not installed on this Mac."])

        // Listed by a marketplace, installed nowhere: Available.
        let unused = try card(.plugin, "unused")
        #expect(unused.isAvailable && unused.version == "0.1" && unused.sources == ["Marketplace alpha"])

        // Agents grouped by name across seats; a project-only one says how many projects.
        #expect(try card(.agent, "reviewer").chips.count == 2)
        #expect(try card(.agent, "proj-agent").chips.map(\.state) == [.projects(1)])
        #expect(try card(.skill, "shared-skill").sources == ["claude.ai (synced)"])
        #expect(try card(.outputStyle, "Terse").description == "Short answers.")
        #expect(try card(.command, "git:sync").chips.count == 1)

        // MCP servers by name: user scope is On, local and project scopes count projects.
        #expect(try card(.mcpServer, "files").chips.map(\.state) == [.on, .on])
        #expect(try card(.mcpServer, "local-db").chips.map(\.state) == [.projects(1)])
        #expect(try card(.mcpServer, "tracker").sources == ["Project (.mcp.json)"])

        // No project path, project name or value in anything a card shows (a chip keeps its
        // seat's path to open By Seat, never to draw it).
        let all = cards.map { c in
            ([c.name, c.description, c.version ?? ""] + c.sources + c.problems + c.chips.map { $0.label + $0.state.title })
                .joined(separator: "|")
        }.joined(separator: "\n")
        #expect(!all.contains("proj-one") && !all.contains(h.path) && !all.contains("Bearer"))
    }

    @Test func filtersSearchAndDemoMode() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.seatA(h)
        Self.seatB(h)
        let cards = ConfigCatalog.build(Self.seats(h, marks: [".claude-a": .work, ".claude-b": .personal]))
        func names(_ f: CatalogFilter, demo: Bool = false, failed: Set<String> = []) -> [String] {
            ConfigCatalog.filter(cards, f, demo: demo, failedChecks: failed).map(\.name)
        }
        #expect(names(CatalogFilter(kind: .agent)) == ["proj-agent", "reviewer"])
        #expect(names(CatalogFilter(kind: .plugin, show: .available)) == [IntegrationScripts.modName, "unused"])
        #expect(names(CatalogFilter(kind: .plugin, show: .needsLook)) == ["ghost", "lint"])
        #expect(names(CatalogFilter(kind: .plugin, show: .needsLook), failed: ["plugin:tidy"]) == ["ghost", "lint", "tidy"])
        #expect(!names(CatalogFilter(show: .installed)).contains("unused"))
        #expect(names(CatalogFilter(seat: h.at(".claude-b"))) == ["lint", "reviewer", "files"])
        #expect(names(CatalogFilter(search: "nobody")) == ["unused"])
        #expect(names(CatalogFilter(search: "BETA")) == ["lint"])
        #expect(names(CatalogFilter(search: "lints things")) == ["lint"])

        // Demo mode hides every card only work seats hold, and keeps shared and available ones.
        let demo = names(CatalogFilter(), demo: true)
        #expect(!demo.contains("tidy") && !demo.contains("proj-agent") && !demo.contains("local-db"))
        #expect(demo.contains("lint") && demo.contains("reviewer") && demo.contains("unused"))
        let counts = ConfigCatalog.counts(cards)
        #expect(counts.cards == cards.count && counts.duplicates == 1 && counts.available == 2)
        #expect(counts.workOnly == cards.filter(\.isWorkOnly).count && counts.workOnly > 0)
    }

    @Test func unmarkedSeatsAreNotWorkOnlyAndStatesRank() {
        #expect(CatalogCard(kind: .plugin, name: "x", chips: [CatalogChip(seat: "/s", label: "s", mark: .unmarked, state: .on)]).isWorkOnly == false)
        let item = { (state: ModItem.State, path: String?) -> ModItem in
            ModItem(folder: "/s", name: "x", version: nil, description: "", path: path, origin: .installed(marketplace: "m"),
                    key: "x@m", isMod: false, state: state)
        }
        #expect(ConfigCatalog.state([item(.off, "/p"), item(.on, "/p")], projects: 0) == .on)
        #expect(ConfigCatalog.state([item(.off, "/p")], projects: 3) == .projects(3))
        #expect(ConfigCatalog.state([item(.off, "/p")], projects: 0) == .off)
        #expect(ConfigCatalog.state([item(.missing, "/p")], projects: 0) == .declared)
        #expect(ConfigCatalog.state([], projects: 2) == .projects(2))
        let synced = ModItem(folder: "/s", name: "x", path: nil, origin: .installed(marketplace: "synced"), key: "x@synced", state: .on)
        #expect(ConfigCatalog.state([synced], projects: 0) == .on)
        #expect(CatalogChipState.projects(1).title == "In 1 project")
        #expect(ConfigCatalog.keySource("a@inline") == "Inline folder" && ConfigCatalog.keySource("a@m") == "Marketplace m")
    }

    // MARK: Smoke hook and state.yaml

    @Test func theModsConfigActionAndItsStateAreCountsOnly() {
        #expect((try? DebugLink.action("mods-config", arg: "catalog").get()) == .modsConfig(.catalog, seat: nil))
        #expect((try? DebugLink.action("mods-config", arg: "seat").get()) == .modsConfig(.bySeat, seat: nil))
        #expect((try? DebugLink.action("mods-config", arg: "seat 2").get()) == .modsConfig(.bySeat, seat: 2))
        if case .success = DebugLink.action("mods-config", arg: "seat 0") { Issue.record("seat 0 parsed") }
        if case .success = DebugLink.action("mods-config", arg: nil) { Issue.record("no arg parsed") }
        #expect(DebugAction.names.contains("mods-config"))

        var s = DebugStateInput()
        s.modsConfig = ModsConfigDebug(tab: "seat", seats: 2,
                                       counts: ConfigCatalog.Counts(cards: 9, available: 2, needsLook: 3, duplicates: 1, workOnly: 4),
                                       shown: 5)
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("mods_config:\n  tab: seat\n  seats: 2\n  cards: 9\n  available: 2\n  needs_look: 3\n  duplicates: 1\n  work_only: 4\n  shown: 5"))
    }
}

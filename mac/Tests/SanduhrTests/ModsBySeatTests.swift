import Foundation
import Testing
@testable import Sanduhr

/// Item 71, slice 1b: By Seat's eight sections, built from made-up seats under the temp
/// directory. The precedence walker, the scope badges, the mask, the parity checks, the
/// never-list buckets, `.seat/state.json`, drift with the wall, and an mtime check proving
/// nothing in any seat changes. Nothing reads the real ~/.claude*, ~/.claude.json or app
/// settings, and nothing runs the CLI or git.
@Suite("Mods & Config: By Seat")
struct ModsBySeatTests {
    static func put(_ h: TempHome, _ relative: String, _ text: String) { ModsPageTests.put(h, relative, text) }

    static func layer(_ source: SettingsSource, _ json: String) -> SettingsLayer {
        let o = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any] ?? [:]
        return SettingsLayer(source: source, file: "/made-up/\(source.title).json", object: o)
    }

    static func row(_ rows: [SettingRow], _ key: String) -> SettingRow? { rows.first { $0.key == key } }

    // MARK: The precedence walker

    @Test func listsMergeExceptTheOverridingOnesAndPluginsResolvePerKey() throws {
        let rows = SettingsPrecedence.walk([
            Self.layer(.user, #"{"permissions":{"allow":["Bash(ls)","Read"]},"availableModels":["a","b"],"enabledPlugins":{"x@m":true,"y@m":true},"model":"user-model"}"#),
            Self.layer(.projectShared, #"{"permissions":{"allow":["Read","Edit(src)"]},"availableModels":["c"],"enabledPlugins":{"x@m":false}}"#),
            Self.layer(.managed, #"{"model":"managed-model"}"#),
        ])
        let allow = try #require(Self.row(rows, "permissions.allow"))
        #expect(allow.value == .text(#"["Read","Edit(src)","Bash(ls)"]"#))
        #expect(allow.contributors == [.projectShared, .user])
        // availableModels overrides: the highest level's list alone.
        #expect(Self.row(rows, "availableModels")?.value == .text(#"["c"]"#))
        #expect(Self.row(rows, "availableModels")?.source == .projectShared)
        // enabledPlugins per plugin id.
        #expect(Self.row(rows, "enabledPlugins.x@m")?.value == .toggle(false))
        #expect(Self.row(rows, "enabledPlugins.x@m")?.source == .projectShared)
        #expect(Self.row(rows, "enabledPlugins.y@m")?.source == .user)
        // Managed beats everything else for a scalar.
        #expect(Self.row(rows, "model")?.value == .text("managed-model"))
        #expect(Self.row(rows, "model")?.source == .managed)
    }

    @Test func theStrictestValueWinsForTheStrictKeys() {
        let rows = SettingsPrecedence.walk([
            Self.layer(.managed, #"{"disableClaudeAiConnectors":false,"maxEffortLevel":"high","syncClaudeAiSkills":true}"#),
            Self.layer(.user, #"{"disableClaudeAiConnectors":true,"maxEffortLevel":"medium","syncClaudeAiSkills":false,"permissions":{"blockReadsOutsideWorkingDirectories":true}}"#),
        ])
        #expect(Self.row(rows, "disableClaudeAiConnectors")?.value == .toggle(true))
        #expect(Self.row(rows, "disableClaudeAiConnectors")?.source == .user)
        #expect(Self.row(rows, "disableClaudeAiConnectors")?.strictWon == true)
        #expect(Self.row(rows, "maxEffortLevel")?.value == .text("medium"))
        #expect(Self.row(rows, "syncClaudeAiSkills")?.value == .toggle(false))
        #expect(Self.row(rows, "permissions.blockReadsOutsideWorkingDirectories")?.value == .toggle(true))
        #expect(Self.row(rows, "permissions.blockReadsOutsideWorkingDirectories")?.strictWon == false)
    }

    @Test func valuesWhereTheyAreIgnoredAreLeftOutWithAWarning() throws {
        let rows = SettingsPrecedence.walk([
            Self.layer(.projectShared, #"{"pluginConfigs":{"a":1},"syncClaudeAiPlugins":false,"permissions":{"defaultMode":"bypassPermissions"},"env":{"CLAUDE_CONFIG_DIR":"/x"}}"#),
            Self.layer(.projectLocal, #"{"syncClaudeAiPlugins":false}"#),
            Self.layer(.user, #"{"pluginConfigs":{"a":2},"permissions":{"defaultMode":"acceptEdits"},"mcpServers":{"s":{}}}"#),
        ])
        let configs = try #require(Self.row(rows, "pluginConfigs"))
        #expect(configs.source == .user)
        #expect(configs.warnings.contains { $0.contains("user or managed settings only") })
        #expect(Self.row(rows, "permissions.defaultMode")?.value == .text("acceptEdits"))
        #expect(Self.row(rows, "permissions.defaultMode")?.warnings.isEmpty == false)
        // A false in the local file is honored; the shared one is ignored.
        #expect(Self.row(rows, "syncClaudeAiPlugins")?.source == .projectLocal)
        #expect(Self.row(rows, "syncClaudeAiPlugins")?.warnings.contains { $0.contains("shared project file") } == true)
        // Set only where it is ignored: listed, not in effect.
        #expect(Self.row(rows, "env.CLAUDE_CONFIG_DIR")?.warnings.first == "Not in effect.")
        #expect(Self.row(rows, "mcpServers")?.warnings.first == "Not in effect.")
    }

    @Test func theScopeBadgeTable() {
        #expect(SettingsPrecedence.honored(["model"]) == "Everywhere")
        for key in ["pluginConfigs", "appendPlugins", "modelPicker", "autoMode", "desktopSessionCleanupPeriodDays"] {
            #expect(SettingsPrecedence.honored([key]) == "User or managed only")
        }
        #expect(SettingsPrecedence.honored(["syncClaudeAiSkills"]) == "User, local or managed")
        #expect(SettingsPrecedence.honored(["mcpServers"]) == "Not read from settings files")
        #expect(SettingsPrecedence.honored(["env", "CLAUDE_CONFIG_DIR"]) == "User or managed only")
        #expect(SettingsPrecedence.family("cleanupPeriodDays") == "Privacy and history")
        #expect(SettingsPrecedence.family("somethingNew") == "Other")
    }

    @Test func everyValueIsMaskedEnvAndSecretPathsAndHeaders() {
        let secret = "sk-" + "ant-" + String(repeating: "q", count: 24)
        let rows = SettingsPrecedence.walk([Self.layer(.user, """
        {"env":{"OPEN":"plain-value"},"apiKeyHelper":"/bin/echo \(secret)",
         "statusLine":{"type":"command","command":"run --token abc123"},"nested":{"auth":{"password":"pw1"}}}
        """)], machineOnly: ["statusLine"])
        let text = rows.map { "\($0.value)" }.joined(separator: "\n")
        #expect(!text.contains("plain-value") && !text.contains(secret) && !text.contains("abc123") && !text.contains("pw1"))
        #expect(Self.row(rows, "env.OPEN")?.value == .text(ConfigMask.masked))
        #expect(Self.row(rows, "statusLine")?.machineOnly == true)
        // MCP headers and env by name only.
        let s = SeatScanner.server("srv", ["url": "https://h.example.com/x", "headers": ["X-Api-Key": "v1"], "env": ["A": "${A}"]],
                                   scope: .user, owner: nil)
        #expect(s.headerNames == ["X-Api-Key"] && s.envNames == ["A"] && s.literalValues)
        #expect(!String(describing: s).contains("v1"))
    }

    @Test func lintsFindDeadAndMisplacedConfig() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-l/settings.local.json", "{}")
        let settings: [String: Any] = ["permissions": ["allow": ["Write(src/**)", "Edit(src/**)"]],
                                       "hooks": ["Stop": [["hooks": [["type": "command", "command": #"C:\tools\x.exe"#]]]]],
                                       "statusLine": ["command": h.at("gone/statusline.sh") + " --x"]]
        let lints = SettingsPrecedence.lints(settings: settings, config: ["hooks": [:]], seat: h.at(".claude-l"), isDefault: false)
        #expect(lints.contains { $0.hasPrefix("1 Write(path) allow rule never match") })
        #expect(lints.contains { $0.contains("settings.local.json in the seat folder") })
        #expect(lints.contains { $0.contains("Windows paths in hooks") })
        #expect(lints.contains { $0.contains("hooks in .claude.json is ignored") })
        #expect(lints.contains { $0.contains("written on another machine") })
    }

    // MARK: Extensions

    @Test func parityChecks() {
        func item(_ name: String, _ origin: ModItem.Origin, _ state: ModItem.State, path: String? = "/p") -> ModItem {
            ModItem(folder: "/s", name: name, path: path, origin: origin, key: nil, state: state)
        }
        var scan = SeatScan(seat: "/s")
        scan.plugins = [item("a", .installed(marketplace: "one"), .on), item("a", .installed(marketplace: "two"), .on),
                        item("gone", .installed(marketplace: "one"), .on, path: nil),
                        item("missing", .installed(marketplace: "one"), .missing),
                        item("synced-one", .installed(marketplace: "synced"), .on, path: nil),
                        item("a", .pluginDirs, .on)]
        scan.marketplaces = [SeatMarketplace(name: "one", source: "github x/y", present: true, cached: true),
                             SeatMarketplace(name: "dir", source: "directory", present: false, cached: false)]
        let p = SeatExtensionsCheck.parity(scan)
        #expect(p.contains("gone: on in settings, not installed on this Mac."))
        #expect(p.contains("missing: its folder isn't on this Mac."))
        #expect(p.contains("a: on from 2 marketplaces (one, two)."))
        #expect(p.contains("Marketplace two isn't added in this seat (no source declared)."))
        #expect(p.contains("Marketplace dir is a folder that isn't on this Mac."))
        #expect(p.contains { $0.hasPrefix("a: the inline folder hides the marketplace plugin") })
        #expect(!p.contains { $0.contains("synced-one") })
    }

    @Test func mcpRowsInPrecedenceOrderWithCollisionsAndRoutes() {
        let servers = [SeatMCPServer(name: "db", scope: .user, transport: "http", target: "db.example.com"),
                       SeatMCPServer(name: "db", scope: .local, transport: "stdio", target: "db-mcp", owner: "/p"),
                       SeatMCPServer(name: "db2", scope: .plugin, transport: "http", target: "db.example.com", owner: "plug"),
                       SeatMCPServer(name: "proj", scope: .project, transport: "stdio", target: "x", literalValues: true, owner: "/p")]
        let rows = SeatMCPView.rows(servers, approvals: SeatMCPView.MCPApprovals(enabled: ["proj"]))
        #expect(rows.map(\.server.scope) == [.local, .project, .user, .plugin])
        #expect(rows[2].shadowedBy == .local)
        #expect(rows[2].sameEndpoint == ["db2"])
        #expect(rows[1].approval == "Approved")
        #expect(rows[0].route?.contains("user scope") == true)
        #expect(rows[1].route?.contains("${VAR}") == true)
        #expect(rows[2].route?.contains("connector") == true)
        #expect(SeatMCPView.MCPApprovals(disabled: ["x"]).state("x") == "Turned off")
        #expect(SeatMCPView.MCPApprovals().state("x") == "Asks on first use")
    }

    // MARK: Seat repo

    @Test func neverListBucketsFromAGitignore() {
        let b = SeatRepo.buckets("""
        # comment
        *
        !settings.json
        !agents/
        .credentials.json
        *.log
        .env*
        projects/
        settings.local.json
        """)
        #expect(b[.synced] == ["settings.json", "agents/"])
        #expect(b[.never] == [".credentials.json", "*.log", ".env*"])
        #expect(b[.machineOnly] == ["*", "projects/", "settings.local.json"])
    }

    @Test func stateJSONIsReadOrUnknown() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "a.json", #"{"last_run":"2026-10-01T10:00:00Z","phase":"idle","paused":false,"report":["ok","pushed ~ settings.json"],"apply":["a","b"]}"#)
        Self.put(h, "b.json", #"{"lastRun":"x","lastReport":"one\ntwo"}"#)
        Self.put(h, "c.json", #"{"something":"else"}"#)
        Self.put(h, "d.json", "[1]")
        guard case .read(let a) = SeatRepo.syncState(h.at("a.json")) else { Issue.record("a not read"); return }
        #expect(a.lastRun == "2026-10-01T10:00:00Z" && a.phase == "idle" && a.paused == false && a.applySteps == 2)
        #expect(a.report == ["ok", "pushed ~ settings.json"])
        guard case .read(let b) = SeatRepo.syncState(h.at("b.json")) else { Issue.record("b not read"); return }
        #expect(b.report == ["one", "two"])
        #expect(SeatRepo.syncState(h.at("c.json")) == .unknownSchema)
        #expect(SeatRepo.syncState(h.at("d.json")) == .unknownSchema)
        #expect(SeatRepo.syncState(h.at("none.json")) == .none)
    }

    @Test func repoFactsFromFilesWithoutGit() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-g/.git/HEAD", "ref: refs/heads/main\n")
        Self.put(h, ".claude-g/.git/config", "[core]\n\tbare = false\n[remote \"origin\"]\n\turl = git@github.com:someone/seat-repo.git\n")
        Self.put(h, ".claude-g/.seat/seat.json", #"{"name":"Made-up Org","remote":"https://github.com/someone/seat-repo"}"#)
        Self.put(h, ".claude-g/.seat/disabled", "")
        let r = SeatRepo.read(h.at(".claude-g"))
        #expect(r.model == .gitClone && r.branch == "main" && r.remoteMatches == true && r.killSwitch)
        #expect(!String(describing: r).contains("Made-up Org"))
        Self.put(h, ".claude-c/.seat/seat.json", #"{"remote":"https://github.com/other/x"}"#)
        #expect(SeatRepo.read(h.at(".claude-c")).model == .copyInstalled)
        h.dir(".claude-p")
        #expect(SeatRepo.read(h.at(".claude-p")).model == .plain)
        #expect(SeatRepo.normalizedRemote("https://user@GitHub.com/a/b.git/") == "github.com/a/b")
    }

    // MARK: Instructions

    @Test func importsResolveUpToFourHopsOutsideCodeAndMarkOutsideAndMissing() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-i/CLAUDE.md", "Hi @docs/one.md and @~/shared/two.md\n```\n@not/this.md\n```\n`@nor/this.md` and @mention\n@missing/file.md\n")
        Self.put(h, ".claude-i/docs/one.md", "@a.md\n")
        Self.put(h, ".claude-i/docs/a.md", "@b.md\n")
        Self.put(h, ".claude-i/docs/b.md", "@c.md\n")
        Self.put(h, ".claude-i/docs/c.md", "@d.md\n")
        Self.put(h, ".claude-i/docs/d.md", "end\n")
        Self.put(h, "shared/two.md", "two\nlines\n")
        Self.put(h, ".claude-i/rules/style.md", "---\npaths: src/**\n---\nRule.\n")
        Self.put(h, ".claude/CLAUDE.md", "home instructions\n")
        Self.put(h, ".claude-i/projects/-p/memory/MEMORY.md", String(repeating: "- [x](gone.md)\n", count: 201))
        let part = SeatInstructions.read(seat: h.at(".claude-i"), isDefault: false, home: h.path, projects: [],
                                         managedFolder: h.at("managed"), excludes: [], managedClaudeMd: nil, .default)
        let titles = part.nodes.map(\.title)
        #expect(titles.contains { $0.hasSuffix("docs/one.md") })
        #expect(titles.contains { $0.hasSuffix("docs/c.md") })
        #expect(!titles.contains { $0.hasSuffix("docs/d.md") })   // a fifth hop isn't followed
        #expect(!titles.contains { $0.contains("not/this") || $0.contains("nor/this") || $0.contains("mention") })
        #expect(part.nodes.first { $0.title.hasSuffix("shared/two.md") }?.outsideSeat == true)
        #expect(part.nodes.first { $0.title.hasSuffix("shared/two.md") }?.lines == 2)
        #expect(part.unresolved == 1)
        #expect(part.nodes.first { $0.kind == .rule }?.gate == "src/**")
        #expect(part.leak != nil)
        #expect(part.memory.first?.overLimit == true && part.memory.first?.missingLinks == 201)
        // An exclude covering it stops the leak.
        let excluded = SeatInstructions.read(seat: h.at(".claude-i"), isDefault: false, home: h.path, projects: [],
                                             managedFolder: h.at("managed"), excludes: ["~/.claude/CLAUDE.md"], managedClaudeMd: nil, .default)
        #expect(excluded.leak == nil && excluded.excludes.first?.matches == 1)
        // Five hops deep stops at four.
        let deep = SeatInstructions.read(seat: h.at(".claude-i"), isDefault: false, home: h.path, projects: [],
                                         managedFolder: h.at("managed"), excludes: [], managedClaudeMd: nil, .default)
        #expect(deep.nodes.map(\.depth).max() == SeatReports.importDepth)
    }

    // MARK: A whole report, the wall, and nothing changed

    /// Two seats: a work one and a personal one, each with settings, a project, transcripts
    /// and leftovers.
    static func twoSeats(_ h: TempHome) {
        let project = h.at("code/made-up-project")
        Self.put(h, ".claude-w/settings.json", #"{"model":"opus","cleanupPeriodDays":90,"env":{"W_TOKEN":"w-secret-value"}}"#)
        Self.put(h, ".claude-w/.claude.json", #"{"projects":{"\#(project)":{}},"mcpServers":{"w":{"command":"w-mcp"}}}"#)
        Self.put(h, ".claude-w/agents/helper.md", "---\nname: helper\n---\n")
        Self.put(h, ".claude-w/settings.json.sanduhr-backup", "{}")
        Self.put(h, ".claude-w/projects/-code-made-up-project/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jsonl", "{}\n")
        h.dir(".claude-w/projects/-code-made-up-project/ffffffff-bbbb-cccc-dddd-eeeeeeeeeeee/subagents")
        Self.put(h, "code/made-up-project/.claude/settings.json", #"{"model":"sonnet"}"#)
        Self.put(h, "code/made-up-project/.vscode/settings.json", #"{"claudeCode.environmentVariables":[{"name":"CLAUDE_CONFIG_DIR","value":"\#(h.at(".claude-p"))"}]}"#)
        Self.put(h, ".claude-p/settings.json", #"{"model":"haiku","env":{"P_TOKEN":"p-secret-value"}}"#)
        Self.put(h, ".claude-p/.claude.json", "{}")
        Self.put(h, ".claude-p/agents/helper.md", "---\nname: helper\n---\n")
        Self.put(h, ".claude-p/agents/solo.md", "---\nname: solo\n---\n")
        Self.put(h, ".claude-p/history.jsonl", "{}\n")
    }

    static func reports(_ h: TempHome) -> [SeatReport] {
        let inputs = [".claude-w", ".claude-p"].map { name in
            SeatReports.Input(seat: h.at(name), isDefault: false,
                              scan: SeatScanner.scan(seat: h.at(name), home: h.path, managedFolder: h.at("managed")))
        }
        return SeatReports.build(inputs, home: h.path, managedFolder: h.at("managed"))
    }

    @Test func buildingEveryReportChangesNothingInAnySeat() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.twoSeats(h)
        let seats = [h.at(".claude-w"), h.at(".claude-p"), h.at("code")]
        let before = SeatMtimes.snapshot(seats)
        let files = TempHomeSnapshot(h)
        #expect(!before.isEmpty)
        _ = Self.reports(h)
        #expect(SeatMtimes.snapshot(seats) == before)
        #expect(TempHomeSnapshot(h) == files)
    }

    @Test func aWholeReportPerSeat() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.twoSeats(h)
        let reports = Self.reports(h)
        let w = reports[0], p = reports[1]
        #expect(Self.row(w.settings.rows, "model")?.setInProjects == 1)
        #expect(w.history.cleanupDays == 90 && !w.history.warns)
        #expect(p.history.warns && p.history.historyBytes == 3)
        #expect(w.history.sessions == 1 && w.history.orphanSidecars == 1)
        #expect(w.housekeeping.contains { $0.origin == "Sanduhr" && $0.name == "settings.json.sanduhr-backup" })
        #expect(w.mcp.map(\.server.name) == ["w"])
        #expect(w.routes == [SeatReport.Route(project: h.at("code/made-up-project"), target: h.at(".claude-p"))])
        #expect(w.repo.model == .plain)
        // Drift: masked values, a key only one side sets, and extensions only one side holds.
        let d = try #require(w.drift.first)
        #expect(d.other == h.at(".claude-p"))
        #expect(d.keys.map(\.key).contains("model") && d.keys.map(\.key).contains("env.W_TOKEN"))
        #expect(d.keys.first { $0.key == "env.W_TOKEN" }?.mine == ConfigMask.masked)
        #expect(d.onlyThere == ["agent:solo"] && d.onlyHere.contains("mcp:w"))
        let all = String(describing: reports)
        #expect(!all.contains("w-secret-value") && !all.contains("p-secret-value"))
        // Counts only.
        #expect(w.counts.settings == w.settings.rows.count && w.counts.leftovers == w.housekeeping.count)
    }

    @Test func theWallDecidesWhetherDriftShowsValues() {
        let store = MemoryDefaults()
        SeatPreferences.setMark(.work, for: "/s/w", in: store)
        SeatPreferences.setMark(.personal, for: "/s/p", in: store)
        SeatPreferences.setMark(.personal, for: "/s/q", in: store)
        let seat = { (p: String) in Seats.seat(p, origin: .found, home: "/s", store: store, config: []) }
        #expect(!SeatWall.showsValues(between: seat("/s/w"), and: seat("/s/p")))
        #expect(!SeatWall.showsValues(between: seat("/s/p"), and: seat("/s/u")))
        #expect(SeatWall.showsValues(between: seat("/s/p"), and: seat("/s/q")))
    }

    @Test func anUnreadableSettingsFileShowsAsUnreadable() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-x/settings.json", "{\"model\": \"x\",}")
        let scan = SeatScanner.scan(seat: h.at(".claude-x"), home: h.path)
        let r = SeatReports.build([SeatReports.Input(seat: h.at(".claude-x"), isDefault: false, scan: scan)],
                                  home: h.path, managedFolder: h.at("managed"))[0]
        #expect(r.settings.unreadable != nil && r.counts.unreadable)
        let s = SeatSectionID.settings.summary(r, others: 0)
        #expect(s.warns && s.text.hasPrefix("settings.json is unreadable: it isn't valid JSON"))
    }

    @Test func strictJSONRefusesTrailingCommasAndComments() {
        #expect(SeatScanner.strictProblem(Data(#"{"a": [1, 2], "b": "x,}"}"#.utf8)) == nil)
        #expect(SeatScanner.strictProblem(Data(#"{"a": "q\"//"}"#.utf8)) == nil)
        #expect(SeatScanner.strictProblem(Data("{\n  \"a\": 1,\n}".utf8)) == "isn't valid JSON (a trailing comma around line 2, column 9)")
        #expect(SeatScanner.strictProblem(Data(#"{"a": [1,]}"#.utf8))?.contains("trailing comma") == true)
        #expect(SeatScanner.strictProblem(Data("{\"a\": 1 /* x */}".utf8))?.contains("a comment around line 1") == true)
    }

    @Test func summariesAndStateYAML() {
        var r = SeatReport(seat: "/s")
        #expect(SeatSectionID.history.summary(r, others: 0).text.hasPrefix("Transcripts older than 30 days are deleted at the next start."))
        r.history.cleanupDays = 60
        #expect(SeatSectionID.history.summary(r, others: 0).warns == false)
        #expect(SeatSectionID.housekeeping.summary(r, others: 0).text == "Nothing left over.")
        #expect(SeatSectionID.drift.summary(r, others: 0).text == "No other seat to compare.")
        #expect(SeatSectionID.extensions.summary(r, others: 1).text == "0 agents, 0 skills, 0 commands, 0 output styles, 0 plugins, 0 hooks.")
        #expect(SeatSectionID.seat.summary(r, others: 1).text == "Plain folder (unsynced seat).")
        #expect(ModsPageModel.claudeVersion("/no/such/versions/2.1.296") == "2.1.296")
        #expect(ModsPageModel.claudeVersion("/usr/local/bin/claude") == nil)

        var s = DebugStateInput()
        s.modsConfig = ModsConfigDebug(tab: "seat", seats: 1, counts: ConfigCatalog.Counts(), shown: 0, selected: 1,
                                       bySeat: [SeatCounts(settings: 3, warnings: 1, unreadable: false, instructions: 2, extensions: 4,
                                                           parity: 0, hooks: 1, mcp: 2, historyWarning: true, leftovers: 1, drift: 0, running: true)])
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("  selected: 1\n  by_seat:\n    - settings: 3\n      warnings: 1\n      unreadable: false\n      instructions: 2\n      extensions: 4\n      parity: 0\n      hooks: 1\n      mcp: 2\n      history_warning: true\n      leftovers: 1\n      drift: 0\n      running: true\n"))
    }
}

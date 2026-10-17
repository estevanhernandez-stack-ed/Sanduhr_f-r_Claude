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
        // availableModels merges across the scopes that aren't managed (the docs: "Across non-managed
        // scopes Claude Code merges the arrays as usual").
        #expect(Self.row(rows, "availableModels")?.value == .text(#"["c","a","b"]"#))
        #expect(Self.row(rows, "availableModels")?.contributors == [.projectShared, .user])
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
        let lints = SettingsPrecedence.lints(settings: settings, configKeys: ["hooks", "projects"], seat: h.at(".claude-l"), isDefault: false)
        #expect(lints.contains { $0.hasPrefix("1 Write(path) allow rule never match") })
        #expect(lints.contains { $0.contains("settings.local.json in the folder itself") })
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
        #expect(p.contains("Marketplace two isn't added in this folder (no source declared)."))
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
        #expect(!titles.contains { $0.contains("not/this") || $0.contains("nor/this") })
        // A bare @mention is an import too (the memory docs: @README imports README); here it doesn't resolve.
        #expect(titles.contains { $0.hasSuffix("/mention") })
        #expect(part.nodes.first { $0.title.hasSuffix("shared/two.md") }?.outsideSeat == true)
        #expect(part.nodes.first { $0.title.hasSuffix("shared/two.md") }?.lines == 2)
        #expect(part.unresolved == 2)
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

    // MARK: Review fixes: managed files, the docs' rules, the wall

    static func obj(_ json: String) -> [String: Any] {
        (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any] ?? [:]
    }

    @Test func managedDropInsMergeIntoOneLayerLaterFileWins() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "managed/managed-settings.json", #"{"cleanupPeriodDays":30,"permissions":{"deny":["Read(a)"]},"env":{"A":"1"},"fallbackModel":["x","y"]}"#)
        Self.put(h, "managed/managed-settings.d/20-later.json", #"{"cleanupPeriodDays":90,"permissions":{"deny":["Read(b)"]},"env":{"B":"2"},"fallbackModel":["z"]}"#)
        Self.put(h, "managed/managed-settings.d/10-first.json", #"{"cleanupPeriodDays":60}"#)
        Self.put(h, "managed/managed-settings.d/.hidden.json", #"{"cleanupPeriodDays":1}"#)
        Self.put(h, "managed/managed-settings.d/notes.txt", "x")
        let managed = SeatReports.managedSettings(h.at("managed"), .default)
        #expect(managed.count == 3 && managed.lints.isEmpty)
        let rows = SettingsPrecedence.walk(managed.layers + [Self.layer(.user, #"{"cleanupPeriodDays":7}"#)])
        let cleanup = try #require(Self.row(rows, "cleanupPeriodDays"))
        #expect(cleanup.value == .text("90") && cleanup.source == .managed)
        #expect(cleanup.file == h.at("managed/managed-settings.d/20-later.json"))
        #expect(Self.row(rows, "permissions.deny")?.value == .text(#"["Read(b)","Read(a)"]"#))
        #expect(Self.row(rows, "env.A") != nil && Self.row(rows, "env.B") != nil)
        #expect(Self.row(rows, "fallbackModel")?.value == .text(#"["z"]"#))
        // A managed file that doesn't parse: Claude Code refuses to start with it.
        Self.put(h, "managed/managed-settings.d/30-broken.json", "{")
        let broken = SeatReports.managedSettings(h.at("managed"), .default)
        #expect(broken.lints.contains { $0.hasPrefix("30-broken.json (managed) isn't valid JSON") && $0.hasSuffix("Claude Code won't start with this file.") })
    }

    @Test func theDocsExceptionsTable() {
        let rows = SettingsPrecedence.walk([
            Self.layer(.managed, #"{"availableModels":["m1"],"autoMode":{"classifyAllShell":false},"remoteControlAtStartup":true,"crossSessionInbound":"hold","useAutoModeDuringPlan":true,"enableArtifact":true}"#),
            Self.layer(.projectLocal, #"{"remoteControlAtStartup":false,"crossSessionInbound":"refuse","useAutoModeDuringPlan":false}"#),
            Self.layer(.projectShared, #"{"crossSessionInbound":"accept","syncClaudeAiSkills":true,"disableArtifact":true}"#),
            Self.layer(.user, #"{"availableModels":["u1"],"autoMode":{"classifyAllShell":true}}"#),
        ])
        // availableModels from managed applies as-is.
        #expect(Self.row(rows, "availableModels")?.value == .text(#"["m1"]"#))
        // autoMode.classifyAllShell: true from user beats managed false.
        #expect(Self.row(rows, "autoMode.classifyAllShell")?.value == .toggle(true))
        #expect(Self.row(rows, "autoMode.classifyAllShell")?.source == .user)
        // remoteControlAtStartup: a project false beats managed true.
        #expect(Self.row(rows, "remoteControlAtStartup")?.value == .toggle(false))
        // crossSessionInbound: the stricter project value wins; one that isn't stricter is ignored.
        #expect(Self.row(rows, "crossSessionInbound")?.value == .text("refuse"))
        #expect(Self.row(rows, "crossSessionInbound")?.warnings.contains { $0.contains("project shared value isn't stricter") } == true)
        // useAutoModeDuringPlan: false from local beats managed true.
        #expect(Self.row(rows, "useAutoModeDuringPlan")?.value == .toggle(false))
        // A shared project file can't set syncClaudeAiSkills either way.
        #expect(Self.row(rows, "syncClaudeAiSkills")?.warnings.first == "Not in effect.")
        // disableArtifact: true from any scope turns enableArtifact off.
        #expect(Self.row(rows, "enableArtifact")?.value == .toggle(false))
        // remoteControlAtStartup: true in a project is ignored.
        let rc = SettingsPrecedence.walk([Self.layer(.projectShared, #"{"remoteControlAtStartup":true}"#)])
        #expect(Self.row(rc, "remoteControlAtStartup")?.warnings.first == "Not in effect.")
    }

    @Test func objectsMergeKeyByKeyAndListsCombineAtDepth() {
        let rows = SettingsPrecedence.walk([
            Self.layer(.user, #"{"sandbox":{"network":{"allowedDomains":["a.example"]},"enabled":true},"modelSettings":{"opus":{"effort":"low"},"haiku":{"effort":"high"}},"extraKnownMarketplaces":{"m":{"source":"u"},"n":{"source":"un"}},"autoMode":{"allow":["x"]}}"#),
            Self.layer(.projectLocal, #"{"sandbox":{"network":{"allowedDomains":["b.example"]}},"modelSettings":{"opus":{"thinking":true}},"extraKnownMarketplaces":{"m":{"source":"p"}}}"#),
        ])
        #expect(Self.row(rows, "sandbox")?.value == .text(#"{"enabled":true,"network":{"allowedDomains":["b.example","a.example"]}}"#))
        // modelSettings one model at a time, each whole: opus from the local file, haiku from user.
        #expect(Self.row(rows, "modelSettings")?.value == .text(#"{"haiku":{"effort":"high"},"opus":{"thinking":true}}"#))
        #expect(Self.row(rows, "extraKnownMarketplaces")?.value == .text(#"{"m":{"source":"p"},"n":{"source":"un"}}"#))
        #expect(Self.row(rows, "autoMode.allow")?.value == .text(#"["x"]"#))
    }

    @Test func managedRulesOnlyAndTheEnvListAreHonored() {
        let rows = SettingsPrecedence.walk([
            Self.layer(.managed, #"{"allowManagedPermissionRulesOnly":true,"permissions":{"allow":["Read(m)"]}}"#),
            Self.layer(.projectShared, #"{"env":{"OTEL_EXPORTER_OTLP_ENDPOINT":"https://x.example","OTEL_METRICS_EXPORTER":"none","XDG_DATA_HOME":"/x","PLAIN":"1"}}"#),
            Self.layer(.user, #"{"permissions":{"allow":["Read(u)"],"defaultMode":"acceptEdits"}}"#),
        ])
        #expect(Self.row(rows, "permissions.allow")?.value == .text(#"["Read(m)"]"#))
        #expect(Self.row(rows, "permissions.allow")?.warnings.contains { $0.hasPrefix("allowManagedPermissionRulesOnly") } == true)
        #expect(Self.row(rows, "permissions.defaultMode")?.warnings.isEmpty == true)
        #expect(Self.row(rows, "env.OTEL_EXPORTER_OTLP_ENDPOINT")?.warnings.first == "Not in effect.")
        #expect(Self.row(rows, "env.XDG_DATA_HOME")?.warnings.first == "Not in effect.")
        #expect(Self.row(rows, "env.OTEL_METRICS_EXPORTER")?.warnings.isEmpty == true)   // "none" turns it off: honored
        #expect(Self.row(rows, "env.PLAIN")?.warnings.isEmpty == true)
        #expect(SettingsPrecedence.envIgnoredInProject("OTEL_LOG_USER_PROMPTS", value: "0") == false)
        #expect(SettingsPrecedence.envIgnoredInProject("OTEL_LOG_USER_PROMPTS", value: "1"))
        #expect(SettingsPrecedence.envIgnoredInProject("ProgramFilesX86", value: nil))
    }

    @Test func aWorkSeatsValuesHideFolderPathsOutsideTheSeat() {
        let rows = SettingsPrecedence.walk([Self.layer(.user, #"""
        {"permissions":{"allow":["Read(//Users/someone/made-up-client/**)","Read(/Users/someone/.claude-w/agents/**)","Bash(ls)"],
         "additionalDirectories":["~/made-up-client"]}}
        """#)], seat: "/Users/someone/.claude-w", home: "/Users/someone")
        let allow = Self.row(rows, "permissions.allow")
        #expect(allow?.workValue == .text(#"["Read(‹path outside the folder›)","Read(/Users/someone/.claude-w/agents/**)","Bash(ls)"]"#))
        if case .text(let t)? = allow?.value { #expect(t.contains("made-up-client")) }
        #expect(allow?.shown(work: false) == allow?.value)
        if case .text(let t)? = Self.row(rows, "permissions.additionalDirectories")?.shown(work: true) {
            #expect(!t.contains("made-up-client"))
        }
        var scan = SeatScan(seat: "/Users/someone/.claude-w")
        scan.plugins = []
        let hooks = SeatExtensionsCheck.hooks(settings: Self.obj(#"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"/Users/someone/made-up-client/bin/x.sh --y"}]}]}}"#),
                                              scan: scan, seat: "/Users/someone/.claude-w", home: "/Users/someone")
        #expect(hooks.first?.command.contains("made-up-client") == true)
        #expect(hooks.first?.workCommand == "‹path outside the folder› --y")
    }

    @Test func aWorkSeatsImportsAreNumberedNotNamed() {
        let nodes = [SeatReport.InstructionNode(id: 0, kind: .seat, title: "~/.claude-w/CLAUDE.md", depth: 0),
                     SeatReport.InstructionNode(id: 1, kind: .imported, title: "~/.claude-w/docs/a.md", depth: 1),
                     SeatReport.InstructionNode(id: 2, kind: .imported, title: "~/made-up-client/notes.md", depth: 1, outsideSeat: true),
                     SeatReport.InstructionNode(id: 3, kind: .imported, title: "~/.claude-w/docs/b.md", depth: 2)]
        #expect(nodes.map { SeatInstructionTitles.title($0, in: nodes, hideNames: true) }
                == ["~/.claude-w/CLAUDE.md", "Import 1", "An import outside the folder", "Import 2"])
        #expect(SeatInstructionTitles.title(nodes[2], in: nodes, hideNames: false) == "~/made-up-client/notes.md")
    }

    @Test func instructionReadsAreBoundedAndImportsFollowTheDocs() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-i/CLAUDE.md", "See @README and @Design\\ Docs/api.md, not @\"quoted.md\". Mail me@example.com.\n@big.md\n@pipe.md\n")
        Self.put(h, ".claude-i/README", "readme\n")
        Self.put(h, ".claude-i/Design Docs/api.md", "api\n")
        Self.put(h, ".claude-i/big.md", String(repeating: "line\n", count: (SeatInstructions.readCap / 5) + 10))
        #expect(mkfifo(h.at(".claude-i/pipe.md"), 0o600) == 0)
        let part = SeatInstructions.read(seat: h.at(".claude-i"), isDefault: false, home: h.path, projects: [],
                                         managedFolder: h.at("managed"), excludes: [], managedClaudeMd: nil, .default)
        let byName = Dictionary(part.nodes.map { (($0.title as NSString).lastPathComponent, $0) }, uniquingKeysWith: { a, _ in a })
        #expect(byName["README"]?.missing == false)
        #expect(byName["api.md"]?.missing == false)
        #expect(byName["quoted.md"] == nil && byName["example.com"] == nil)
        #expect(byName["big.md"]?.truncated == true && byName["big.md"]?.bytes == SeatInstructions.readCap)
        #expect(byName["pipe.md"]?.missing == true)
    }

    @Test func neverWordsMatchWholeWordsAndVSCodeSettingsReadAsJSONC() {
        let b = SeatRepo.buckets("keybindings.json\ncatalog/\n.env.local\n*.log\n")
        #expect(b[.machineOnly] == ["keybindings.json", "catalog/"])
        #expect(b[.never] == [".env.local", "*.log"])
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "p/.vscode/settings.json", """
        {
          // VS Code allows comments
          "claudeCode.environmentVariables": [{"name": "CLAUDE_CONFIG_DIR", "value": "\(h.at(".claude-z"))"},],
        }
        """)
        #expect(SeatRepo.routes([h.at("p")], home: h.path) == [SeatReport.Route(project: h.at("p"), target: h.at(".claude-z"))])
        #expect(SeatScanner.stripJSONC(Data(#"{"a": "x // y", "b": [1,],}"#.utf8)) == Data(#"{"a": "x // y", "b": [1]}"#.utf8))
    }

    @Test func driftSkipsKeysWhenASettingsFileIsUnreadable() {
        var a = SeatScan(seat: "/a"), b = SeatScan(seat: "/b")
        a.agents = [SeatEntry(name: "x", description: "", scope: .seat, path: "/a/agents/x.md")]
        b.settingsUnreadable = "isn't valid JSON"
        let d = SeatDriftCheck.drift(mine: ["model": "x"], theirs: nil, myScan: a, theirScan: b, other: "/b")
        #expect(d.settingsUnreadable && d.keys.isEmpty && d.onlyHere == ["agent:x"])
    }

    @Test func summariesAndStateYAML() {
        var r = SeatReport(seat: "/s")
        #expect(SeatSectionID.history.summary(r, others: 0).text.hasPrefix("Transcripts older than 30 days are deleted at the next start."))
        r.history.cleanupDays = 60
        #expect(SeatSectionID.history.summary(r, others: 0).warns == false)
        #expect(SeatSectionID.housekeeping.summary(r, others: 0).text == "Nothing left over.")
        #expect(SeatSectionID.drift.summary(r, others: 0).text == "No other folder to compare.")
        #expect(SeatSectionID.extensions.summary(r, others: 1).text == "0 agents, 0 skills, 0 commands, 0 output styles, 0 plugins, 0 hooks.")
        #expect(SeatSectionID.seat.summary(r, others: 1).text == "Plain folder (unsynced).")
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

/// Modification dates of every file under some folders, to prove a read changed nothing
/// (item 71, slice 1b). Capped, so a huge seat stays quick.
enum SeatMtimes {
    static func snapshot(_ folders: [String], limit: Int = 50_000, fileManager fm: FileManager = .default) -> [String: Date] {
        var out: [String: Date] = [:]
        for folder in folders {
            out[folder] = (try? fm.attributesOfItem(atPath: folder))?[.modificationDate] as? Date
            guard let e = fm.enumerator(atPath: folder) else { continue }
            while let rel = e.nextObject() as? String, out.count < limit {
                let p = (folder as NSString).appendingPathComponent(rel)
                out[p] = (try? fm.attributesOfItem(atPath: p))?[.modificationDate] as? Date
            }
        }
        return out
    }
}

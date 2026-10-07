import Foundation
import Testing
@testable import Sanduhr

/// The Mods page (item 64, slice 1): the inventory read from temp folders, the static scan, the
/// validator's JSON (fixtures captured from `claude plugin validate --json`, paths replaced) and
/// the Check runner against a stand-in `claude` script. Nothing reads the real ~/.claude* or
/// runs the real CLI.
@Suite("Mods page")
struct ModsPageTests {
    static func put(_ home: TempHome, _ relative: String, _ text: String) {
        let url = URL(fileURLWithPath: home.at(relative))
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: url.path, contents: Data(text.utf8))
    }

    static func fixture(_ name: String) throws -> Data {
        let url = try #require(Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures/mods-validate"))
        return try Data(contentsOf: url)
    }

    /// A folder loading one of everything: an inline mod, a disabled @inline mod, a plain plugin
    /// in the plugin list, a missing path, an installed mod, an installed non-mod plugin turned
    /// off, an installed plugin whose folder is gone, a synced key, a skills-dir plugin and a
    /// session dev mod.
    static func everything(_ h: TempHome) {
        h.dir(".claude/projects")
        put(h, "mods/watch/.claude-plugin/plugin.json", #"{"name":"watch","version":"1.2.0","description":"Watches CI."}"#)
        put(h, "mods/watch/hooks/hooks.json", #"{"modules":["./register.tsx"]}"#)
        put(h, "mods/watch/hooks/register.tsx", """
        export const register = (on) => {
          on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => next(e))
          on('command.run', { command: 'ci' }, async ($) => { await $.process.run(['gh', 'run', 'list']); return { text: 'ok' } })
          on('session.start', async ($, e, next) => { $.ui.status('ci: ok'); $.ui.toast('hi'); await $.fs.read('/x'); return next(e) })
        }
        """)
        // Types and tests name APIs without the mod calling them.
        put(h, "mods/watch/types/index.d.ts", "declare const x: { http: { fetch(): void } }; // http.fetch\n")
        put(h, "mods/watch/hooks/register.test.ts", "await $.http.fetch('https://example.com')\n")
        put(h, "mods/off/.claude-plugin/plugin.json", #"{"name":"off"}"#)
        put(h, "mods/off/hooks/hooks.json", #"{"modules":["./r.js"]}"#)
        put(h, "mods/off/hooks/r.js", "on('ui.render', { component: 'Pane' }, ($, e, next) => next(e))\n")
        put(h, "mods/plain/.claude-plugin/plugin.json", #"{"name":"plain","description":"Just skills."}"#)
        put(h, "cache/beep/.claude-plugin/plugin.json", #"{"name":"beep"}"#)
        put(h, "cache/beep/hooks/hooks.json", #"{"modules":["./m.ts"]}"#)
        put(h, "cache/beep/hooks/m.ts", "$.http.fetch(`https://example.com`)\n$.ui.open({})\n")
        put(h, "cache/helper/.claude-plugin/plugin.json", #"{"name":"helper"}"#)
        put(h, "cache/helper/commands/tidy.md", "Tidy up.\n")
        put(h, "cache/helper/hooks/hooks.json", #"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo hi"}]}]}}"#)
        put(h, ".claude/plugins/installed_plugins.json", """
        {"version":2,"plugins":{
          "beep@market":[{"installPath":"\(h.at("cache/beep"))","version":"0.3.0"}],
          "helper@market":[{"installPath":"\(h.at("cache/helper"))"}],
          "gone@market":[{"installPath":"\(h.at("cache/gone"))"}]}}
        """)
        put(h, ".claude/skills/skillmod/.claude-plugin/plugin.json", #"{"name":"skillmod","version":"2"}"#)
        put(h, ".claude/skills/skillmod/hooks/hooks.json", #"{"modules":["./s.ts"]}"#)
        put(h, ".claude/skills/skillmod/hooks/s.ts", "$.ui.toast('x')\n")
        put(h, ".claude/skills/justaskill/SKILL.md", "---\nname: justaskill\n---\n")
        put(h, ".claude/dev-mods/abcdef1234567890/.claude-plugin/plugin.json", #"{"name":"draft"}"#)
        put(h, ".claude/dev-mods/abcdef1234567890/hooks/hooks.json", #"{"modules":["./d.ts"]}"#)
        put(h, ".claude/dev-mods/abcdef1234567890/hooks/d.ts", "$.ui.status('draft')\n")
        put(h, ".claude/settings.json", """
        {"env": {"CLAUDE_CODE_PLUGIN_DIRS": "~/mods/watch:\(h.at("mods/off")):~/mods/plain:~/mods/missing"},
         "enabledPlugins": {"off@inline": false, "beep@market": true, "helper@market": false,
                            "gone@market": true, "remote@synced": true, "skillmod@skills-dir": true}}
        """)
    }

    @Test func listsEveryModAndPluginAFolderLoads() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.everything(h)
        let items = ModInventory.scan(folder: h.at(".claude"), home: h.path)
        #expect(items.map(\.name) == ["watch", "off", "plain", "missing", "beep", "gone", "helper", "remote", "skillmod", "draft"])
        let by = Dictionary(uniqueKeysWithValues: items.map { ($0.name, $0) })

        let watch = by["watch"]!
        #expect(watch.origin == .pluginDirs && watch.key == "watch@inline" && watch.isMod)
        #expect(watch.version == "1.2.0" && watch.description == "Watches CI." && watch.state == .on)
        #expect(watch.touches.surfaces == [.band, .status, .toast, .commands])
        #expect(watch.touches.capabilities == [.process, .files])   // http only in the .d.ts and the test
        #expect(watch.touches.commands == ["ci"])

        #expect(by["off"]?.state == .off && by["off"]?.touches.surfaces == [.pane])
        #expect(by["plain"]?.isMod == false && by["plain"]?.state == .on && by["plain"]?.touches.isEmpty == true)
        #expect(by["missing"]?.state == .missing && by["missing"]?.path == h.at("mods/missing"))

        let beep = by["beep"]!
        #expect(beep.origin == .installed(marketplace: "market") && beep.key == "beep@market")
        #expect(beep.version == "0.3.0" && beep.state == .on)          // the record's version
        #expect(beep.touches.surfaces == [.pane] && beep.touches.capabilities == [.http])

        let helper = by["helper"]!
        #expect(!helper.isMod && helper.state == .off)
        #expect(helper.touches.surfaces == [.commands] && helper.touches.commands == ["tidy"])
        #expect(helper.touches.capabilities == [.process])            // its settings-style hook

        #expect(by["gone"]?.state == .missing)
        #expect(by["remote"]?.path == nil && by["remote"]?.state == .on)
        #expect(by["remote"]?.originTitle == "Synced from claude.ai")

        #expect(by["skillmod"]?.origin == .skillsDir && by["skillmod"]?.key == "skillmod@skills-dir")
        #expect(by["skillmod"]?.touches.surfaces == [.toast])
        #expect(by["draft"]?.origin == .devMods(session: "abcdef1234567890") && by["draft"]?.state == .session)
        #expect(by["draft"]?.originTitle == "Made in session abcdef12")
    }

    @Test func countsAcrossFolders() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.everything(h)
        h.dir(".claude-empty/projects")
        let inventory = ModInventory.scan(folders: [h.at(".claude"), h.at(".claude-empty")], home: h.path)
        #expect(inventory.map(\.items.count) == [10, 0])
        // Mods: watch, off, beep, skillmod, draft; a missing one counts as a plugin (no files to
        // read). On: watch, plain, beep, remote, skillmod and the session's draft.
        let c = ModCounts(inventory)
        #expect(c == ModCounts(folders: 2, mods: 5, plugins: 5, on: 6, missing: 2))
    }

    @Test func aFolderWithoutSettingsStillListsItsSkillsAndDevMods() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, ".claude-x/dev-mods/s1/meter/hooks/hooks.json", #"{"modules":["./m.ts"]}"#)
        Self.put(h, ".claude-x/dev-mods/s1/meter/hooks/m.ts", "$.ui.status('x')\n")
        let items = ModInventory.scan(folder: h.at(".claude-x"), home: h.path)
        #expect(items.map(\.name) == ["meter"])
        #expect(items.first?.state == .session && items.first?.isMod == true)
        #expect(ModInventory.scan(folder: h.at("nothing"), home: h.path).isEmpty)
    }

    @Test func modStatusEntriesStillSkipTypesAndTests() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        Self.put(h, "mods/t/hooks/hooks.json", #"{"modules":["./r.ts"]}"#)
        Self.put(h, "mods/t/hooks/r.ts", "on('ui.render', () => null)\n")
        Self.put(h, "mods/t/hooks/r.test.ts", "$.ui.status('stub')\n")
        Self.put(h, "mods/t/types/index.d.ts", "// ui.status\n")
        Self.put(h, ".claude/settings.json", #"{"env":{"CLAUDE_CODE_PLUGIN_DIRS":"~/mods/t"}}"#)
        #expect(ModStatusEntries.scan(folder: h.at(".claude"), home: h.path).isEmpty)
    }

    // MARK: Folders

    @Test func configBackupFoldersAreSkipped() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.dir(".claude-work/projects")
        h.dir(".claude-work.config-backup-20261001-174332/projects")
        h.dir(".claude.config-backup-20260929-000647/projects")
        #expect(ClaudeCodeFolders.discover(home: h.path).map(\.name) == [".claude", ".claude-work"])
        #expect(ClaudeCodeFolders.isBackup(".claude-work.config-backup-1"))
        #expect(!ClaudeCodeFolders.isBackup(".claude-work"))
        // The folder CLAUDE_CONFIG_DIR names is the one Claude Code uses, whatever its name.
        let env = ["CLAUDE_CONFIG_DIR": h.at(".claude-work.config-backup-20261001-174332")]
        #expect(ClaudeCodeFolders.discover(home: h.path, environment: env).map(\.name).last == ".claude-work.config-backup-20261001-174332")
    }

    @Test func pageFoldersAddLinkedAndInstalledOnes() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        let folders = ModsPageModel.folders(home: h.path, environment: [:], linked: [h.at("elsewhere"), h.at(".claude")],
                                            installed: [h.at("installed")])
        #expect(folders == [h.at(".claude"), h.at("elsewhere"), h.at("installed")])
    }

    // MARK: The validator's report

    @Test func readsTheMetersModsReport() throws {
        let r = try #require(ModCheckReport.parse(try Self.fixture("meters"), dir: "/fixture/mod"))
        #expect(r.passed && r.errors.isEmpty && r.warnings.isEmpty)
        #expect(r.hooks == ["session.start", "ui.render{component=AbovePrompt}"])
        #expect(r.calls.contains("$.env.get") && r.calls.contains("$.fs.read") && !r.calls.contains { $0.contains("via") })
        #expect(r.envReads == ["APPDATA", "HOME", "OS", "SANDUHR_SNAPSHOT"] && r.envWrites.isEmpty)
        #expect(r.risk == .medium)
        #expect(r.reasons == ["Reads files: $.fs.read", "Reads environment variables: APPDATA, HOME, OS, SANDUHR_SNAPSHOT"])
        #expect(r.notes.count == 4)                                    // types and state notes kept as written
        #expect(r.notes.contains("./register.tsx state reads: sanduhr-meters.snapshot"))
    }

    @Test func gatingAndProgramsAreHighRisk() throws {
        let r = try #require(ModCheckReport.parse(try Self.fixture("gating"), dir: "/fixture/mod"))
        #expect(r.passed && r.risk == .high)
        #expect(r.gating == ["tool.call (without .catch)"])
        #expect(r.reasons.first == "Runs programs: $.process.run")
        #expect(r.reasons.contains("Reaches the network: $.http.fetch"))
        #expect(r.reasons.contains("Writes files or settings: $.fs.write"))
        #expect(r.reasons.contains("Changes what Claude does: tool.call"))
        #expect(r.reasons.contains("Reads secret-looking environment variables: GITHUB_TOKEN"))
        #expect(r.reasons.contains("Reads environment variables: HOME"))
        #expect(r.notes.isEmpty)                                       // the gating note is in `gating`
    }

    @Test func aRefusedModShowsItsErrorsWithShortPaths() throws {
        let r = try #require(ModCheckReport.parse(try Self.fixture("refused"), dir: "/fixture/mod"))
        #expect(!r.passed)
        #expect(r.errors.count == 2 && r.warnings.count == 1)
        #expect(r.errors.first == "version: Invalid input: expected string, received number")
        #expect(r.errors.last?.contains("bad: hooks/r.ts, compiled line 6") == true)
        #expect(!(r.errors + r.warnings).contains { $0.contains("/fixture/mod") })
        #expect(r.risk == .low && r.reasons == ["Draws and keeps its own state only."])
    }

    @Test func aPlainPluginIsLowRisk() throws {
        let r = try #require(ModCheckReport.parse(try Self.fixture("plain"), dir: "/fixture/mod"))
        #expect(r.passed && r.risk == .low && r.warnings.count == 1 && r.hooks.isEmpty)
        #expect(ModCheckReport.parse(Data("not json".utf8), dir: "/x") == nil)
        #expect(ModCheckReport.parse(Data("{\"ok\": true}".utf8), dir: "/x") == nil)
    }

    @Test func noteListsSplitOutsideBraces() {
        #expect(ModCheckReport.split("a, ui.render{component=X,props=Y}, $.fs.read (via f, g)")
                == ["a", "ui.render{component=X,props=Y}", "$.fs.read"])
        #expect(ModCheckReport.split("nothing").isEmpty)
    }

    // MARK: Check

    @Test func findsClaudeOnPathThenInTheUsualFolders() {
        let home = "/Users/someone"
        let env = ["PATH": "/a/bin:/b/bin"]
        #expect(ModCheck.findClaude(environment: env, home: home, isExecutable: { $0 == "/b/bin/claude" }) == "/b/bin/claude")
        #expect(ModCheck.findClaude(environment: env, home: home,
                                    isExecutable: { $0 == "/Users/someone/.local/bin/claude" || $0 == "/opt/homebrew/bin/claude" })
                == "/Users/someone/.local/bin/claude")
        #expect(ModCheck.findClaude(environment: [:], home: home, isExecutable: { _ in false }) == nil)
    }

    @Test func checkRunsValidateOnlyNeverTest() {
        #expect(ModCheck.arguments(dir: "/m") == ["plugin", "validate", "/m", "--json"])
        #expect(ModCheck.run(claude: nil, dir: "/m") == .unavailable)
    }

    /// A stand-in `claude`: a shell script in a temp folder.
    static func fakeClaude(_ h: TempHome, _ body: String) -> String {
        let path = h.at("bin/claude")
        put(h, "bin/claude", "#!/bin/sh\n" + body + "\n")
        chmod(path, 0o755)
        return path
    }

    @Test func checkReadsTheReportEvenOnExitOne() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "refused.json", String(decoding: try Self.fixture("refused"), as: UTF8.self))
        Self.put(h, "args.txt", "")
        let claude = Self.fakeClaude(h, "echo \"$@\" > '\(h.at("args.txt"))'\ncat '\(h.at("refused.json"))'\nexit 1")
        guard case .report(let r) = ModCheck.run(claude: claude, dir: "/fixture/mod") else {
            Issue.record("expected a report")
            return
        }
        #expect(!r.passed && r.errors.count == 2)
        let args = try String(contentsOfFile: h.at("args.txt"), encoding: .utf8)
        #expect(args == "plugin validate /fixture/mod --json\n")
    }

    @Test func checkWithoutAReportSaysWhy() {
        let h = TempHome()
        defer { h.cleanUp() }
        let claude = Self.fakeClaude(h, "echo 'error: unknown command' >&2\nexit 2")
        #expect(ModCheck.run(claude: claude, dir: "/m") == .failed("error: unknown command"))
        let silent = Self.fakeClaude(h, "exit 3")
        #expect(ModCheck.run(claude: silent, dir: "/m") == .failed("It gave no report (exit 3)."))
    }

    @Test func checkStopsASlowValidator() {
        let h = TempHome()
        defer { h.cleanUp() }
        let claude = Self.fakeClaude(h, "exec sleep 20")
        let start = Date()
        #expect(ModCheck.run(claude: claude, dir: "/m", timeout: 0.5) == .timedOut)
        #expect(Date().timeIntervalSince(start) < 5)
        #expect(ModCheck.timeout == 10)
    }

    // MARK: The sketch

    @Test func theSketchDrawsEachSurfaceWithPlaceholdersOnly() throws {
        let touches = ModTouches(surfaces: [.band, .pane, .status, .toast, .commands], capabilities: [.process], commands: ["ci", "np"])
        let text = try #require(ModSketch.text(name: "watch", touches: touches))
        let plain = ANSIText.runs(text).map(\.text).joined()
        let lines = plain.split(separator: "\n").map(String.init)
        #expect(lines.count == ModSketch.lineCount(touches))
        #expect(lines.contains { $0.hasPrefix("┌ watch") })                     // the pane
        #expect(lines.contains { $0.hasPrefix("▌ watch") })                     // the band
        #expect(lines.contains { $0.hasPrefix("⚠ watch: ") })                   // the status entry
        #expect(lines.contains { $0.contains("◆ watch") })                      // a toast
        #expect(lines.last == "/ci  /np")
        // Above the prompt: the band sits right over the "> " line.
        let band = try #require(lines.firstIndex { $0.hasPrefix("▌") })
        #expect(lines[band + 1].hasPrefix("> "))
        #expect(ModSketch.text(name: "x", touches: ModTouches(capabilities: [.http])) == nil)
    }

    // MARK: state.yaml

    @Test func stateYAMLHasCountsOnly() {
        var s = DebugStateInput()
        s.modsPage = ModsPageDebug(open: true, loaded: true, counts: ModCounts(folders: 2, mods: 3, plugins: 4, on: 5, missing: 1),
                                   checked: 2, cli: true)
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("mods_page:\n  open: true\n  loaded: true\n  folders: 2\n  mods: 3\n  plugins: 4\n  enabled: 5\n  missing: 1\n  checked: 2\n  cli: true"))
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// A whole Sanduhr profile in a temp home: two Claude Code folders, Sanduhr's Application Support
/// folder and Desk's messages under `Support/`, two accounts in fake credential stores, and two
/// throwaway defaults suites standing in for the preferences domains. Nothing here reaches the
/// real Keychain, defaults, Application Support or any `~/.claude*` folder.
private struct ResetBed {
    let rig = IntegrationRig()
    let keys: RegistryFixture
    let suites = ["sanduhr.tests.reset.app.\(UUID().uuidString)", "sanduhr.tests.reset.desk.\(UUID().uuidString)"]
    let work: String
    let other: String

    static let settings = """
    {
      "model": "opus",
      "permissions": {
        "allow": ["Bash(ls:*)"]
      }
    }

    """
    static let config = "{\n  \"numStartups\": 3,\n  \"projects\": {}\n}\n"

    init() {
        keys = RegistryFixture(keychain: ["sessionKey:Personal": "sk-1", "cf_clearance:Personal": "cf-1",
                                          "sessionKey:Work": "sk-2", "sessionKey": "sk-legacy"],
                               file: ["sessionKey:Work": "sk-2"],
                               labels: ["Personal", "Work"], active: "Work")
        rig.home.dir(".claude-work/projects")
        rig.home.file(".claude-work/settings.json", Self.settings)
        rig.home.file(".claude-work/.claude.json", Self.config)
        rig.home.dir(".claude-other/projects")
        rig.home.file(".claude-other/settings.json", "{}\n")
        work = rig.home.at(".claude-work")
        other = rig.home.at(".claude-other")
        for name in suites {
            let d = UserDefaults(suiteName: name)!
            d.set(true, forKey: "deskEnabled")
            d.set("bl", forKey: "layout")
        }
    }

    var supportDir: URL { URL(fileURLWithPath: rig.home.at("Support/Sanduhr"), isDirectory: true) }
    var messages: URL { URL(fileURLWithPath: rig.home.at("Support/Desk/messages.txt")) }

    var env: ResetEnvironment {
        ResetEnvironment(installer: rig.installer, accounts: keys.registry, supportDir: supportDir,
                         messagesFile: messages,
                         domains: suites.map { ResetDomain(name: $0, defaults: UserDefaults(suiteName: $0)!) })
    }

    /// Sanduhr's own files beside the integrations: history, snapshots, seats, the vault, a theme,
    /// the widgets folder, the Desk's messages.
    func writeSanduhrFiles() {
        for rel in ["Support/Sanduhr/history.Personal.json", "Support/Sanduhr/snapshot.json",
                    "Support/Sanduhr/snapshot.0f1e2d3c.json", "Support/Sanduhr/seats.json",
                    "Support/Sanduhr/vault/2026-10.jsonl", "Support/Sanduhr/themes/mine.json",
                    "Support/Sanduhr/widgets/widgets.json"] {
            let url = URL(fileURLWithPath: rig.home.at(rel))
            try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            FileManager.default.createFile(atPath: url.path, contents: Data("{}".utf8))
        }
        try? FileManager.default.createDirectory(at: messages.deletingLastPathComponent(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: messages.path, contents: Data("keep building.\n".utf8))
    }

    /// Everything Sanduhr installs, in both folders, the meters mod switched off in one.
    func installEverything() throws {
        let i = rig.installer
        try i.install(.statusline, folder: work, python: rig.python)
        try i.install(.meters, folder: work, python: "")
        try i.install(.hooks, folder: work, python: "")
        try i.install(.mcp, folder: work, python: rig.python)
        try i.switchOwnMod(on: false, folder: work)
        try i.install(.hooks, folder: other, python: "")
        try i.install(.statusline, folder: other, python: rig.python)
    }

    func persisted(_ suite: String) -> [String: Any] {
        UserDefaults(suiteName: suite)!.persistentDomain(forName: suite) ?? [:]
    }

    /// Every file under the home, Support included, with its bytes.
    func everything() -> [String: Data] {
        var out: [String: Data] = [:]
        let fm = FileManager.default
        guard let e = fm.enumerator(atPath: rig.home.path) else { return out }
        while let rel = e.nextObject() as? String {
            let p = rig.home.at(rel)
            var isDir: ObjCBool = false
            if fm.fileExists(atPath: p, isDirectory: &isDir), !isDir.boolValue { out[rel] = fm.contents(atPath: p) }
        }
        return out
    }

    func cleanUp() {
        for s in suites { UserDefaults(suiteName: s)?.removePersistentDomain(forName: s) }
        rig.cleanUp()
    }
}

@Suite("Reset Sanduhr")
struct ResetTests {
    @Test func stepsRunClaudeCodeFirstThenKeysThenFilesThenSettings() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        b.writeSanduhrFiles()
        try b.installEverything()

        let steps = Reset.plan(Set(ResetPart.allCases), in: b.env)
        let order = ResetPart.allCases
        let parts = steps.map(\.part)
        #expect(parts.first == .claudeCode)
        #expect(parts.last == .settings)
        // Parts never interleave and follow the sheet's order.
        let indexes = parts.map { order.firstIndex(of: $0)! }
        #expect(indexes == indexes.sorted())
        #expect(Set(parts) == Set(order))
        // Claude Code: newest install first, every receipt once.
        let entries = steps.filter { $0.kind == .claudeCodeEntry }
        let receipts = b.rig.installer.loadReceipts()
        #expect(entries.count == receipts.count)
        #expect(entries.map(\.integration) == receipts.reversed().map(\.kind))
        // Accounts by Remove Account, then the keys from before accounts.
        let keys = steps.filter { $0.part == .accounts }.map(\.kind)
        #expect(keys == [.account, .account, .legacyKeys])
        // Files: Sanduhr's folder, the messages, the themes, the widgets; preferences, one step per domain, last.
        #expect(steps.filter { $0.part != .claudeCode && $0.part != .accounts }.map(\.kind)
                == [.supportFiles, .deskMessages, .themes, .widgets, .preferences, .preferences])
    }

    @Test func everythingCheckedLeavesEachFolderAsItWasAndNothingOfSanduhrs() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        let before = b.rig.snapshot()
        b.writeSanduhrFiles()
        try b.installEverything()
        #expect(b.rig.snapshot() != before)

        let results = Reset.perform(Reset.plan(Set(ResetPart.allCases), in: b.env), in: b.env)

        #expect(results.filter { !$0.ok }.isEmpty, "\(results.filter { !$0.ok }.map { "\($0.step.target): \($0.error ?? "")" })")
        // Every Claude Code file byte for byte as before Sanduhr's installs, no backup left.
        #expect(b.rig.snapshot() == before)
        #expect(!FileManager.default.fileExists(atPath: b.supportDir.path))
        // No key in either store, no account, no preferences.
        #expect(b.keys.keychain.items.isEmpty)
        #expect(b.keys.file.items.isEmpty)
        #expect(b.keys.registry.labels.isEmpty)
        for s in b.suites { #expect(b.persisted(s).isEmpty) }
        // The messages kept beside, as messages.txt.previous.
        #expect(!FileManager.default.fileExists(atPath: b.messages.path))
        #expect(FileManager.default.contents(atPath: b.messages.path + ".previous") == Data("keep building.\n".utf8))
    }

    @Test func aFolderWhoseFileChangedFailsAloneAndTheRestStillRuns() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        let otherBefore = b.rig.bytes(".claude-other/settings.json")
        b.writeSanduhrFiles()
        try b.installEverything()
        // Changed underneath: the work folder's settings.json is no longer JSON Sanduhr can edit.
        let broken = Data("{ \"model\": \"opus\", oops".utf8)
        FileManager.default.createFile(atPath: b.rig.home.at(".claude-work/settings.json"), contents: broken)

        let steps = Reset.plan(Set(ResetPart.allCases), in: b.env)
        let results = Reset.perform(steps, in: b.env)

        let failed = results.filter { !$0.ok }
        #expect(!failed.isEmpty)
        #expect(failed.allSatisfy { $0.step.part == .claudeCode && $0.step.folder == b.work })
        #expect(failed.allSatisfy { $0.error?.contains("isn't JSON") == true })
        // The broken file is left exactly as found.
        #expect(b.rig.bytes(".claude-work/settings.json") == broken)
        // Every step after the failures ran: the other folder, the work folder's other file, the
        // accounts, the files and the preferences.
        #expect(results.map(\.step) == steps)
        #expect(results.last?.step.part == .settings && results.last?.ok == true)
        #expect(b.rig.bytes(".claude-other/settings.json") == otherBefore)
        #expect(b.rig.text(".claude-work/.claude.json") == ResetBed.config)
        #expect(b.keys.registry.labels.isEmpty && b.keys.keychain.items.isEmpty)
        for s in b.suites { #expect(b.persisted(s).isEmpty) }
        #expect(!FileManager.default.fileExists(atPath: b.messages.path))
        // What removes the failed entries later stays: the scripts and their receipts, nothing else.
        let left = try FileManager.default.contentsOfDirectory(atPath: b.supportDir.path)
        #expect(left == [ResetEnvironment.integrationsName])
        #expect(!b.rig.installer.loadReceipts().isEmpty)
        #expect(b.rig.installer.loadReceipts().allSatisfy { $0.folder == b.work })
    }

    @Test func dryRunReportsKindsAndCountsAndTouchesNothing() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        b.writeSanduhrFiles()
        try b.installEverything()
        let files = b.everything()
        let keychain = b.keys.keychain.items
        let file = b.keys.file.items
        let prefs = b.suites.map { b.persisted($0).count }

        let report = Reset.dryRun(in: b.env)

        #expect(b.everything() == files)
        #expect(b.keys.keychain.items == keychain && b.keys.file.items == file)
        #expect(b.keys.registry.labels == ["Personal", "Work"])
        #expect(b.suites.map { b.persisted($0).count } == prefs)

        let receipts = b.rig.installer.loadReceipts().count
        typealias Row = Reset.Report.Row
        #expect(report.rows == [
            Row(part: "claude_code", kind: "claude_code_entry", count: receipts),
            Row(part: "accounts", kind: "account", count: 2),
            Row(part: "accounts", kind: "legacy_keys", count: 1),
            // integrations, history, seats, two snapshots, the vault (themes and widgets are their own parts).
            Row(part: "records", kind: "support_files", count: 6),
            Row(part: "desk_messages", kind: "desk_messages", count: 1),
            Row(part: "themes", kind: "themes", count: 1),
            Row(part: "widgets", kind: "widgets", count: 1),
            Row(part: "settings", kind: "preferences", count: 2),
        ])
        #expect(report.folders == 2)
        // Counts and kinds only: no path, label or domain in what state.yaml shows.
        let yaml = YAMLEmitter.emit(DebugState.resetDryRunYAML(report))
        for secret in ["/", "Personal", "Work", "sanduhr.tests", "claude-work"] {
            #expect(!yaml.contains(secret), "\(secret)")
        }
        #expect(yaml.contains("touched: false"))
    }

    @Test func uncheckedClaudeCodeLeavesFoldersAndWhatRemovesThemLater() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        b.writeSanduhrFiles()
        try b.installEverything()
        let installed = b.rig.snapshot()
        let receipts = b.rig.installer.loadReceipts()

        var parts = Set(ResetPart.allCases)
        parts.remove(.claudeCode)
        let steps = Reset.plan(parts, in: b.env)
        #expect(!steps.contains { $0.part == .claudeCode })
        let results = Reset.perform(steps, in: b.env)

        #expect(results.filter { !$0.ok }.isEmpty)
        // Both folders untouched, and integrations/ kept: their entries still run and can still
        // be removed from Settings, Claude Code.
        #expect(b.rig.snapshot() == installed)
        #expect(try FileManager.default.contentsOfDirectory(atPath: b.supportDir.path) == [ResetEnvironment.integrationsName])
        #expect(b.rig.installer.loadReceipts() == receipts)
    }

    @Test func anUncheckedFolderStaysAndTheCheckedOneIsUndone() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        let before = b.rig.snapshot()
        try b.installEverything()
        let workInstalled = b.rig.bytes(".claude-work/settings.json")

        let steps = Reset.plan([.claudeCode], folders: [b.other], in: b.env)
        #expect(steps.allSatisfy { $0.folder == b.other })
        #expect(Reset.perform(steps, in: b.env).filter { !$0.ok }.isEmpty)
        #expect(b.rig.bytes(".claude-other/settings.json") == before[".claude-other/settings.json"])
        #expect(b.rig.bytes(".claude-work/settings.json") == workInstalled)
    }

    @Test func nothingProfileHasLittleToDo() {
        let b = ResetBed()
        defer { b.cleanUp() }
        let steps = Reset.plan(Set(ResetPart.allCases), in: b.env)
        // No receipts, no Sanduhr folder, no messages: the accounts and the preferences only.
        #expect(steps.map(\.part) == [.accounts, .accounts, .accounts, .settings, .settings])
    }

    /// Review M1: someone else's edit between install and reset stays; Sanduhr's entry and the
    /// backup Install kept both go.
    @Test func anEditSinceInstallStaysAndTheBackupGoes() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        try b.rig.installer.install(.statusline, folder: b.work, python: b.rig.python)
        let backup = b.rig.home.at(".claude-work/settings.json") + IntegrationInstaller.backupSuffix
        #expect(FileManager.default.fileExists(atPath: backup))
        let edited = try #require(b.rig.text(".claude-work/settings.json")).replacingOccurrences(of: "\"opus\"", with: "\"sonnet\"")
        FileManager.default.createFile(atPath: b.rig.home.at(".claude-work/settings.json"), contents: Data(edited.utf8))

        let results = Reset.perform(Reset.plan([.claudeCode], in: b.env), in: b.env)

        #expect(results.filter { !$0.ok }.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: backup))
        let root = b.rig.json(".claude-work/settings.json")
        #expect(root["model"] as? String == "sonnet")
        #expect(root["statusLine"] == nil)
    }

    /// Review M3: installs.json that doesn't read keeps integrations/, whatever else is checked.
    @Test func unreadableReceiptsKeepTheIntegrationsFolder() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        b.writeSanduhrFiles()
        try b.installEverything()
        let receipts = b.rig.installer.receiptsURL
        try Data("[{\"folder\": 1, \"newer\": true}]".utf8).write(to: receipts)
        #expect(Reset.receiptsUnreadable(b.env))

        let steps = Reset.plan(Set(ResetPart.allCases), in: b.env)
        #expect(!steps.contains { $0.part == .claudeCode })
        #expect(Reset.perform(steps, in: b.env).filter { !$0.ok }.isEmpty)
        #expect(try FileManager.default.contentsOfDirectory(atPath: b.supportDir.path) == [ResetEnvironment.integrationsName])
        #expect(FileManager.default.fileExists(atPath: receipts.path))
        // The sweep at quit keeps it too.
        Reset.sweep(Set(ResetPart.allCases), in: b.env)
        #expect(FileManager.default.fileExists(atPath: receipts.path))
    }

    /// Review low: an entry of Sanduhr's still there after Remove fails its step and keeps its
    /// receipt, so it can still be removed later.
    @Test func anEntryStillThereKeepsItsReceipt() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        try b.rig.installer.install(.statusline, folder: b.other, python: b.rig.python)
        // A receipt whose "previous" value is itself a Sanduhr statusline: Remove puts it back.
        let ours = try #require(b.rig.json(".claude-other/settings.json")["statusLine"])
        let text = String(decoding: try JSONSerialization.data(withJSONObject: ours, options: [.sortedKeys]), as: UTF8.self)
        var receipts = b.rig.installer.loadReceipts()
        receipts[0].previous = text
        b.rig.installer.saveReceipts(receipts)

        let results = Reset.perform(Reset.plan([.claudeCode], in: b.env), in: b.env)

        #expect(results.count == 1)
        #expect(results.first?.error?.contains("still there") == true)
        #expect(b.rig.installer.loadReceipts().count == 1)
    }

    /// Review M2: a dev build (file store only) says its Keychain stays; a Keychain key that
    /// can't be deleted says where it is.
    @Test func keyStoresAreSaidInTheResults() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        #expect(b.env.clearsKeychain)
        let file = FakeBackend(["sessionKey:Work": "sk"])
        let defaults = MemoryDefaults()
        defaults.set(["Work"], forKey: AccountRegistry.listKey)
        var dev = b.env
        dev.accounts = AccountRegistry(backend: file, stores: [(kind: .file, backend: file)], defaults: defaults)
        #expect(!dev.clearsKeychain)
        let devResults = Reset.perform(Reset.plan([.accounts], in: dev), in: dev)
        #expect(!devResults.isEmpty)
        #expect(devResults.allSatisfy { $0.note == ResetEnvironment.devBuildKeysNote })

        b.keys.keychain.failDelete = ["sessionKey:Work"]
        let results = Reset.perform(Reset.plan([.accounts], in: b.env), in: b.env)
        let work = try #require(results.first { $0.step.target == "Work" })
        #expect(work.error?.contains("Keychain Access") == true)
        #expect(work.error?.contains("Work") == false)
        #expect(results.allSatisfy { $0.note == nil })
    }

    /// Review H1: the sweep at quit takes out what a stray write put back, only for the checked
    /// parts, and running it again changes nothing.
    @Test func theQuitSweepIsIdempotentAndFollowsTheParts() throws {
        let b = ResetBed()
        defer { b.cleanUp() }
        b.writeSanduhrFiles()
        var parts = Set(ResetPart.allCases)
        parts.remove(.themes)
        _ = Reset.perform(Reset.plan(parts, in: b.env), in: b.env)
        // Stray writes after the run.
        FileManager.default.createFile(atPath: b.supportDir.appendingPathComponent("snapshot.json").path, contents: Data("{}".utf8))
        UserDefaults(suiteName: b.suites[0])!.set(true, forKey: "SUEnableAutomaticChecks")

        Reset.sweep(parts, in: b.env)
        Reset.sweep(parts, in: b.env)

        #expect(try FileManager.default.contentsOfDirectory(atPath: b.supportDir.path) == [ResetEnvironment.themesName])
        for s in b.suites { #expect(b.persisted(s).isEmpty) }
    }

    @Test func widgetsAreTheirOwnPartNextToThemes() throws {
        #expect(ResetPart.allCases.firstIndex(of: .widgets) == ResetPart.allCases.firstIndex(of: .themes)! + 1)
        let b = ResetBed()
        defer { b.cleanUp() }
        b.writeSanduhrFiles()
        let widgets = b.supportDir.appendingPathComponent(ResetEnvironment.widgetsName)

        // Records alone leaves the widgets folder (and the themes) where they are.
        _ = Reset.perform(Reset.plan([.records], in: b.env), in: b.env)
        #expect(FileManager.default.fileExists(atPath: widgets.path))
        #expect(try Set(FileManager.default.contentsOfDirectory(atPath: b.supportDir.path))
                == [ResetEnvironment.themesName, ResetEnvironment.widgetsName])

        // Unchecked, it stays whatever else is checked; checked, it goes and nothing is made again.
        var parts = Set(ResetPart.allCases)
        parts.remove(.widgets)
        _ = Reset.perform(Reset.plan(parts, in: b.env), in: b.env)
        #expect(try FileManager.default.contentsOfDirectory(atPath: b.supportDir.path) == [ResetEnvironment.widgetsName])
        let results = Reset.perform(Reset.plan([.widgets], in: b.env), in: b.env)
        #expect(results.map(\.step.kind) == [.widgets] && results.allSatisfy(\.ok))
        #expect(!FileManager.default.fileExists(atPath: widgets.path))
        #expect(Reset.plan([.widgets], in: b.env).isEmpty)

        // The quit sweep takes a stray widgets folder only when the part was checked.
        try FileManager.default.createDirectory(at: widgets, withIntermediateDirectories: true)
        Reset.sweep([.themes], in: b.env)
        #expect(FileManager.default.fileExists(atPath: widgets.path))
        Reset.sweep([.widgets], in: b.env)
        #expect(!FileManager.default.fileExists(atPath: widgets.path))
    }

    @Test func theHookTakesOnlyTheDryRun() {
        func parse(_ arg: String?) -> DebugRequest {
            var c = URLComponents(string: "sanduhr://debug/action")!
            c.queryItems = [URLQueryItem(name: "name", value: "reset")] + (arg.map { [URLQueryItem(name: "arg", value: $0)] } ?? [])
            return DebugLink.parse(c.url!)
        }
        #expect(parse("--dry-run").command == .action(.resetDryRun, dir: nil))
        #expect(parse("dry-run").command == .action(.resetDryRun, dir: nil))
        for refused in [nil, "", "now", "all", "--force", "dry"] {
            #expect(parse(refused).command == nil, "\(refused ?? "nil")")
            #expect(parse(refused).error?.contains("--dry-run") == true)
        }
    }

    /// The real reset starts only from the sheet's confirmed button: no other file in the app,
    /// the debug hooks included, calls `Reset.perform`.
    @Test func onlyTheSheetRunsAReset() throws {
        let sources = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Sources/Sanduhr")
        let fm = FileManager.default
        var callers: [String] = []
        let e = try #require(fm.enumerator(atPath: sources.path))
        while let rel = e.nextObject() as? String {
            guard rel.hasSuffix(".swift"),
                  let text = try? String(contentsOf: sources.appendingPathComponent(rel), encoding: .utf8) else { continue }
            if text.contains("Reset.perform(") { callers.append(rel) }
        }
        #expect(callers == ["Views/ResetSheet.swift"])
    }
}

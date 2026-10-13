import Darwin
import Foundation
import Testing
@testable import Sanduhr

/// Item 71, slice 0 ("the ground"): the `.claude.json` placement rule, Remove by receipt, seat
/// discovery and preferences, the wall's direction rule, the secret mask, receipts read record
/// by record, and which seat a running `claude` belongs to. Everything runs in a made-up home
/// under the temp directory; nothing reads the real ~/.claude*, ~/.claude.json or app settings.
@Suite("Mods & Config: the ground")
struct ModsConfigGroundTests {
    /// A stub as Claude Code writes it when it starts with `CLAUDE_CONFIG_DIR` naming a folder
    /// that has no `.claude.json` yet: no sign-in.
    static let stub = #"{"firstStartTime":"2026-10-01T00:00:00.000Z","machineID":"m","userID":"u","migrationVersion":3}"#
    static let live = #"{"numStartups":40,"projects":{"/Users/someone/p":{}},"oauthAccount":{"organizationUuid":"org-1","emailAddress":"someone@example.com"}}"#

    static func age(_ h: TempHome, _ relative: String, secondsAgo: TimeInterval) {
        try? FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSinceNow: -secondsAgo)],
                                               ofItemAtPath: h.at(relative))
    }

    // MARK: Placement rule

    @Test func liveFileBeatsANewerStubInsideTheDefaultHome() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.file(".claude.json", Self.live)
        h.file(".claude/.claude.json", Self.stub)
        Self.age(h, ".claude.json", secondsAgo: 3600)      // the stub is newer, and still loses
        let folder = h.at(".claude")
        #expect(ClaudeCodeFolders.configFile(for: folder, home: h.path) == h.at(".claude.json"))
        let c = ClaudeCodeFolders.configCandidates(for: folder, home: h.path)
        #expect(c.map(\.path) == [h.at(".claude.json"), h.at(".claude/.claude.json")])
        #expect(c.map(\.inUse) == [true, false])
        #expect(c.map(\.signedIn) == [true, false])
        #expect(c.map(\.exists) == [true, true])
    }

    @Test func aSignedInInnerFileBeatsAStubBesideIt() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.file(".claude.json", Self.stub)
        h.file(".claude/.claude.json", Self.live)
        Self.age(h, ".claude/.claude.json", secondsAgo: 3600)
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude/.claude.json"))
    }

    @Test func liveOnlyAndInnerOnly() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        // Neither: beside the home, where Claude Code would create it.
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude.json"))
        h.file(".claude.json", Self.live)
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude.json"))
        try? FileManager.default.removeItem(atPath: h.at(".claude.json"))
        h.file(".claude/.claude.json", Self.stub)
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude/.claude.json"))
        #expect(ClaudeCodeFolders.configCandidates(for: h.at(".claude"), home: h.path).map(\.exists) == [false, true])
    }

    @Test func bothSignedInOrNeitherTheNewerWinsAndATieGoesBesideTheHome() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.file(".claude.json", Self.live)
        h.file(".claude/.claude.json", Self.live)
        Self.age(h, ".claude.json", secondsAgo: 60)
        Self.age(h, ".claude/.claude.json", secondsAgo: 30)
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude/.claude.json"))
        Self.age(h, ".claude/.claude.json", secondsAgo: 90)
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude.json"))
        // Neither signed in: still the newer.
        h.file(".claude.json", Self.stub)
        h.file(".claude/.claude.json", Self.stub)
        Self.age(h, ".claude.json", secondsAgo: 500)
        Self.age(h, ".claude/.claude.json", secondsAgo: 100)
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude/.claude.json"))
        // A tie goes to Claude Code's own default.
        let same = Date(timeIntervalSinceNow: -200)
        for f in [".claude.json", ".claude/.claude.json"] {
            try? FileManager.default.setAttributes([.modificationDate: same], ofItemAtPath: h.at(f))
        }
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude.json"))
    }

    @Test func signedInMeansAnOauthAccountObject() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.file(".claude.json", #"{"oauthAccount":"not an object"}"#)
        h.file(".claude/.claude.json", #"{"oauthAccount":{}}"#)
        Self.age(h, ".claude/.claude.json", secondsAgo: 3600)
        let c = ClaudeCodeFolders.configCandidates(for: h.at(".claude"), home: h.path)
        #expect(c.map(\.signedIn) == [false, true])
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude/.claude.json"))
    }

    @Test func anotherSeatAlwaysKeepsItsOwnFile() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude-a/projects")
        h.file(".claude.json", Self.live)
        let c = ClaudeCodeFolders.configCandidates(for: h.at(".claude-a"), home: h.path)
        #expect(c.map(\.path) == [h.at(".claude-a/.claude.json")])
        #expect(c[0].inUse && !c[0].exists)
    }

    // MARK: Remove by receipt

    /// An entry installed into the stub under the old rule comes out of the stub, even though
    /// the rule now picks the live file, and the live file isn't touched.
    @Test func removeEditsTheFileTheReceiptNames() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/.claude.json", Self.stub)
        let folder = r.home.at(".claude")
        let stubBefore = r.bytes(".claude/.claude.json")
        #expect(try r.installer.install(.mcp, folder: folder, python: r.python) == .installed)
        #expect(r.installer.loadReceipts().first?.file == r.home.at(".claude/.claude.json"))

        // The live file appears (the user signs in without CLAUDE_CONFIG_DIR).
        r.home.file(".claude.json", Self.live)
        let liveBefore = r.bytes(".claude.json")
        #expect(r.installer.configFile(.mcp, folder: folder) == r.home.at(".claude.json"))
        #expect(r.installer.status(.mcp, folder: folder) == .notInstalled)

        try r.installer.remove(.mcp, folder: folder)
        #expect(r.bytes(".claude/.claude.json") == stubBefore)
        #expect(r.bytes(".claude.json") == liveBefore)
        #expect(r.installer.loadReceipts().isEmpty)
    }

    /// Installed in both files over time: Remove undoes both, each from its own receipt.
    @Test func removeUndoesEveryFileItWroteFor() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/.claude.json", Self.stub)
        let stubBefore = r.bytes(".claude/.claude.json")
        let folder = r.home.at(".claude")
        try r.installer.install(.mcp, folder: folder, python: r.python)
        r.home.file(".claude.json", Self.live)
        let liveBefore = r.bytes(".claude.json")
        try r.installer.install(.mcp, folder: folder, python: r.python)
        #expect(r.installer.status(.mcp, folder: folder) == .installed)
        #expect(Set(r.installer.loadReceipts().map(\.file)) == [r.home.at(".claude.json"), r.home.at(".claude/.claude.json")])
        try r.installer.remove(.mcp, folder: folder)
        #expect(r.bytes(".claude.json") == liveBefore)
        #expect(r.bytes(".claude/.claude.json") == stubBefore)
        #expect(r.installer.loadReceipts().isEmpty)
    }

    // MARK: Receipts, record by record

    @Test func aMalformedRecordIsSkippedAndTheOthersStillUndo() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", Self.live)
        let folder = r.home.at(".claude")
        let before = r.bytes(".claude.json")
        try r.installer.install(.mcp, folder: folder, python: r.python)
        // A record from a newer Sanduhr (an unknown kind) and one cut short, beside the good one.
        let url = r.installer.receiptsURL
        var array = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [Any])
        array.append(["folder": "/f", "file": "/f/settings.json", "kind": "configEdit", "keyPath": "cleanupPeriodDays"])
        array.append(["folder": "/g"])
        try JSONSerialization.data(withJSONObject: array).write(to: url)

        let read = r.installer.readReceipts()
        #expect(read.records.count == 1 && read.unreadable.count == 2 && read.skipped == 2 && !read.fileUnreadable)
        #expect(r.installer.status(.mcp, folder: folder) == .installed)

        try r.installer.remove(.mcp, folder: folder)
        #expect(r.bytes(".claude.json") == before)
        // The two it couldn't read are kept as written, and the scripts stay while they are.
        let after = r.installer.readReceipts()
        #expect(after.records.isEmpty && after.unreadable.count == 2)
        let kept = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [[String: Any]])
        #expect(kept.contains { $0["kind"] as? String == "configEdit" })
        #expect(FileManager.default.fileExists(atPath: r.support.path))
    }

    @Test func aFileThatIsNotAnArrayIsMovedAsideNeverOverwritten() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", Self.live)
        try FileManager.default.createDirectory(at: r.support, withIntermediateDirectories: true)
        let url = r.installer.receiptsURL
        try Data("{not json".utf8).write(to: url)
        #expect(r.installer.readReceipts().fileUnreadable)
        #expect(r.installer.readReceipts().skipped == 1)
        try r.installer.install(.mcp, folder: r.home.at(".claude"), python: r.python)
        #expect(r.installer.loadReceipts().count == 1)
        let aside = ReceiptLog.asideURL(url)
        #expect(String(decoding: try Data(contentsOf: aside), as: UTF8.self) == "{not json")
    }

    @Test func receiptLogRoundTripsWithNothingSetAside() throws {
        let one = IntegrationReceipt(folder: "/f", file: "/f/settings.json", kind: .statusline)
        let data = try #require(ReceiptLog.encode([one]))
        let back = ReceiptLog.decode(IntegrationReceipt.self, from: data)
        #expect(back.records == [one] && back.skipped == 0)
        #expect(ReceiptLog.decode(IntegrationReceipt.self, from: Data("[]".utf8)).records.isEmpty)
    }

    // MARK: Discovery and seats

    @Test func discoverySkipsSeatBackupFolders() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.dir(".claude-work/projects")
        h.dir(".claude-backup-20261001/projects")
        h.dir(".claude-work.config-backup-20261001-174332/projects")
        #expect(ClaudeCodeFolders.isBackup(".claude-backup-20261001"))
        #expect(!ClaudeCodeFolders.isBackup(".claude-backups-are-mine"))
        #expect(ClaudeCodeFolders.discover(home: h.path).map(\.name) == [".claude", ".claude-work"])
    }

    @Test func seatsCarryLabelsAndMarksFromSanduhrsPreferencesOnly() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.dir(".claude-alpha/projects")
        h.dir(".claude-beta/projects")
        h.dir("elsewhere/seat/projects")
        h.dir("not-a-seat")
        // A seat's own identity file never names it.
        h.file(".claude-alpha/seat.json", #"{"name":"Some Employer"}"#)
        let d = MemoryDefaults()
        AccountData.link(h.at(".claude-alpha"), to: "Work", in: d)
        AccountData.setWork(true, for: "Work", in: d)
        AccountData.link(h.at(".claude"), to: "Personal", in: d)
        SeatPreferences.setMark(.work, for: h.at(".claude-beta"), in: d)
        SeatPreferences.setLabel("  Lab  ", for: h.at(".claude-beta"), in: d)
        SeatPreferences.add(h.at("elsewhere/seat"), in: d)
        SeatPreferences.add(h.at("elsewhere/seat"), in: d)
        SeatPreferences.add(h.at("not-a-seat"), in: d)
        let before = TempHomeSnapshot(h)

        let seats = Seats.discover(home: h.path, store: d, installed: [h.at(".claude-beta")])
        #expect(seats.map(\.name) == [".claude", ".claude-alpha", ".claude-beta", "seat"])
        #expect(seats.map(\.origin) == [.found, .found, .found, .added])
        // A linked folder takes its account's name; the others keep their own label or name.
        #expect(seats.map(\.label) == ["Personal", "Work", "Lab", "seat"])
        #expect(seats.map(\.account) == ["Personal", "Work", nil, nil])
        #expect(seats.map(\.labelChosen) == [false, false, true, false])
        // A link to an account not marked work says nothing: unmarked, not personal.
        #expect(seats.map(\.mark) == [.unmarked, .work, .work, .unmarked])
        #expect(seats.map(\.markSource) == [nil, .account, .toggle, nil])
        #expect(seats.map(\.isDefault) == [true, false, false, false])
        #expect(seats[0].configFile == h.at(".claude.json"))
        #expect(!seats.contains { $0.label.contains("Employer") })
        // Reading seats writes nothing anywhere in the home.
        #expect(TempHomeSnapshot(h) == before)

        // A blank label goes back to the folder's name; unmarked clears the toggle.
        SeatPreferences.setLabel(" ", for: h.at(".claude-beta"), in: d)
        SeatPreferences.setMark(.unmarked, for: h.at(".claude-beta"), in: d)
        let beta = Seats.seat(h.at(".claude-beta"), origin: .found, home: h.path, store: d)
        #expect(beta.label == ".claude-beta" && beta.mark == .unmarked && beta.markSource == nil)
        SeatPreferences.forget(h.at("elsewhere/seat"), in: d)
        SeatPreferences.forget(h.at("not-a-seat"), in: d)
        #expect(d.values[SeatPreferences.key] == nil)
    }

    @Test func theEnvironmentsSeatIsMarkedAsSuch() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.dir("cc/projects")
        let seats = Seats.discover(home: h.path, environment: ["CLAUDE_CONFIG_DIR": h.at("cc")], store: MemoryDefaults())
        #expect(seats.map(\.origin) == [.found, .environment])
    }

    @Test func theDefaultSeatShowsBothFilesWhenBothExist() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.file(".claude.json", Self.live)
        h.file(".claude/.claude.json", Self.stub)
        Self.age(h, ".claude.json", secondsAgo: 60)
        let seat = Seats.seat(h.at(".claude"), origin: .found, home: h.path, store: MemoryDefaults())
        #expect(seat.hasTwoConfigFiles)
        let display = { (p: String) in ClaudeCodeFolders.Folder(path: p).display(home: h.path) }
        let note = ModsPageModel.configNote(seat.config, display: display)
        #expect(note?.hasPrefix("Two .claude.json files: ~/.claude.json (signed in) is the one Sanduhr reads") == true)
        #expect(note?.contains("~/.claude/.claude.json (not signed in) is what Claude Code reads only when CLAUDE_CONFIG_DIR names this folder") == true)
        #expect(note?.contains("someone") == false)
        try? FileManager.default.removeItem(atPath: h.at(".claude/.claude.json"))
        let one = Seats.seat(h.at(".claude"), origin: .found, home: h.path, store: MemoryDefaults())
        #expect(!one.hasTwoConfigFiles)
        #expect(ModsPageModel.configNote(one.config, display: display) == nil)
    }

    @Test func pageFoldersIncludeTheOnesAddedByHand() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.dir("added/projects")
        let folders = ModsPageModel.folders(home: h.path, environment: [:], added: [h.at("added"), h.at(".claude")],
                                            linked: [h.at("linked"), h.at("added")], installed: [])
        #expect(folders == [h.at(".claude"), h.at("added"), h.at("linked")])
        #expect(ModsPageModel.receiptsSkippedText(1).hasPrefix("One of Sanduhr's install records couldn't be read and was set aside"))
        #expect(ModsPageModel.receiptsSkippedText(2).hasPrefix("2 of Sanduhr's install records couldn't be read and were set aside"))
    }

    // MARK: The wall

    @Test func nothingCrossesOutOfAWorkSeat() {
        func seat(_ path: String, _ mark: SeatMark) -> Seat {
            Seat(path: path, origin: .found, isDefault: false, account: nil, label: path, labelChosen: false, mark: mark,
                 markSource: mark == .unmarked ? nil : .toggle, config: [])
        }
        let work = seat("/w", .work), work2 = seat("/w2", .work)
        let personal = seat("/p", .personal), personal2 = seat("/p2", .personal), unmarked = seat("/u", .unmarked)
        #expect(!SeatWall.allowsCopy(from: work, to: personal))
        #expect(!SeatWall.allowsCopy(from: work, to: work2))
        #expect(!SeatWall.allowsCopy(from: work, to: unmarked))
        #expect(!SeatWall.allowsCopy(from: unmarked, to: personal))
        #expect(SeatWall.allowsCopy(from: personal, to: personal2))
        #expect(SeatWall.allowsCopy(from: personal, to: work))
        #expect(!SeatWall.allowsCopy(from: personal, to: personal))
        #expect(SeatWall.showsValues(between: personal, and: personal2))
        #expect(!SeatWall.showsValues(between: personal, and: work))
        #expect(!SeatWall.showsValues(between: work, and: work2))
        #expect(!SeatWall.showsValues(between: personal, and: unmarked))
    }

    // MARK: The secret mask

    @Test func secretPathsAreMaskedAndNeverEditable() {
        #expect(ConfigMask.isSecret(["env", "PATH"]))
        #expect(ConfigMask.isSecret(["env"]))
        #expect(ConfigMask.isSecret(["apiKeyHelper"]))
        #expect(ConfigMask.isSecret(["mcpServers", "x", "headers", "Authorization"]))
        #expect(ConfigMask.isSecret(["mcpServers", "x", "env", "GITHUB_TOKEN"]))
        #expect(ConfigMask.isSecret(["otelHeadersHelper", "refresh_token"]))
        #expect(ConfigMask.isSecret(["oauthAccount", "emailAddress"]))
        #expect(ConfigMask.isSecret(["someService", "Password"]))
        #expect(ConfigMask.isSecret(["awsCredentialExport"]))
        #expect(ConfigMask.isSecret(["a", "client_secret"]))
        #expect(!ConfigMask.isSecret(["cleanupPeriodDays"]))
        #expect(!ConfigMask.isSecret(["permissions", "allow", "0"]))
        #expect(!ConfigMask.isSecret(["syncClaudeAiSkills"]))
        #expect(!ConfigMask.isEditable(["env", "FOO"]))
        #expect(ConfigMask.isEditable(["cleanupPeriodDays"]))
    }

    @Test func maskKeepsNamesAndDropsValues() throws {
        let settings: [String: Any] = [
            "cleanupPeriodDays": 90,
            "env": ["ANTHROPIC_API_KEY": "sk-ant-secret", "NODE_OPTIONS": "--max-old-space-size=8192"],
            "mcpServers": ["gh": ["command": "npx", "args": ["-y", "server"],
                                  "env": ["GITHUB_TOKEN": "ghp_secret"],
                                  "headers": ["Authorization": "Bearer secret"]]],
            "apiKeyHelper": "/bin/print-secret",
            "nested": ["deeper": ["password": ["a": "secret-a"], "list": ["fine"]]],
        ]
        let shown = ConfigMask.display(settings)
        for secret in ["sk-ant-secret", "8192", "ghp_secret", "Bearer", "print-secret", "secret-a"] {
            #expect(!shown.contains(secret), "\(secret) leaked")
        }
        for name in ["ANTHROPIC_API_KEY", "NODE_OPTIONS", "GITHUB_TOKEN", "Authorization", "apiKeyHelper", "90", "npx", "fine"] {
            #expect(shown.contains(name), "\(name) missing")
        }
        let masked = try #require(ConfigMask.mask(settings) as? [String: Any])
        #expect((masked["env"] as? [String: String])?["NODE_OPTIONS"] == ConfigMask.masked)
        #expect(ConfigMask.mask(["a", "b"], at: ["env", "LIST"]) as? [String] == [ConfigMask.masked, ConfigMask.masked])
        #expect(ConfigMask.display("plain", at: ["model"]) == "plain")
        #expect(ConfigMask.display("hunter2", at: ["env", "X"]) == ConfigMask.masked)
    }

    // MARK: Review fixes

    /// A receipt Sanduhr can't read may pin a stamp: the launch prune deletes nothing while one
    /// is there, and prunes as before once every receipt reads.
    @Test func noStampIsPrunedWhileAReceiptIsUnreadable() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        let scripts = r.scripts
        try scripts.refresh()
        let pinned = "aaaaaaaaaaaa", stray = "bbbbbbbbbbbb"
        for stamp in [pinned, stray] {
            try FileManager.default.createDirectory(at: r.support.appendingPathComponent(stamp), withIntermediateDirectories: true)
        }
        var good = IntegrationReceipt(folder: r.home.at(".claude"), file: r.home.at(".claude/settings.json"), kind: .meters)
        good.pinned = pinned
        let goodJSON = try #require(String(data: try JSONEncoder().encode(good), encoding: .utf8))
        try Data("[\(goodJSON), {\"folder\": \"/g\", \"kind\": \"futureKind\"}]".utf8).write(to: r.installer.receiptsURL)
        #expect(scripts.pinnedStamps == [pinned])
        #expect(!scripts.receiptsAllReadable)

        scripts.refreshIfInstalled(settingsFiles: [])
        let fm = FileManager.default
        #expect(fm.fileExists(atPath: r.support.appendingPathComponent(pinned).path))
        #expect(fm.fileExists(atPath: r.support.appendingPathComponent(stray).path))

        try Data("[\(goodJSON)]".utf8).write(to: r.installer.receiptsURL)
        scripts.refreshIfInstalled(settingsFiles: [])
        #expect(fm.fileExists(atPath: r.support.appendingPathComponent(pinned).path))
        #expect(!fm.fileExists(atPath: r.support.appendingPathComponent(stray).path))
    }

    /// An unreadable receipts file is moved aside, and Remove of the last readable receipt then
    /// keeps the scripts: the set-aside file may still name them.
    @Test func scriptsStayWhileAReceiptsFileIsSetAside() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", Self.live)
        try FileManager.default.createDirectory(at: r.support, withIntermediateDirectories: true)
        try Data("[{\"folder\":".utf8).write(to: r.installer.receiptsURL)        // cut short
        try r.installer.install(.mcp, folder: r.home.at(".claude"), python: r.python)
        try r.installer.remove(.mcp, folder: r.home.at(".claude"))
        #expect(r.installer.scripts.isInstalled)
        #expect(FileManager.default.fileExists(atPath: ReceiptLog.asideURL(r.installer.receiptsURL).path))
    }

    /// Remove with no readable receipt for the folder, but a set-aside one naming it, changes
    /// nothing and says why.
    @Test func removeRefusesWhenOnlyASetAsideReceiptNamesTheFolder() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", Self.live)
        let folder = r.home.at(".claude")
        try r.installer.install(.mcp, folder: folder, python: r.python)
        let installed = r.bytes(".claude.json")
        // The receipt gains a field of a different type than this version reads: set aside.
        let url = r.installer.receiptsURL
        var array = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [[String: Any]])
        array[0]["createdFile"] = "not a bool"
        try JSONSerialization.data(withJSONObject: array).write(to: url)
        #expect(r.installer.loadReceipts().isEmpty)
        #expect(throws: IntegrationInstaller.Failure.receiptSetAside(file: r.home.at(".claude.json"))) {
            try r.installer.remove(.mcp, folder: folder)
        }
        #expect(r.bytes(".claude.json") == installed)
        #expect(IntegrationsModel.setAsideText("~/.claude.json").contains("Nothing was changed"))
    }

    /// Unchanged records are written back from their own bytes (a field this version doesn't
    /// know survives), and a record that doesn't decode is kept byte for byte.
    @Test func savesKeepUnknownFieldsAndSetAsideBytes() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        try FileManager.default.createDirectory(at: r.support, withIntermediateDirectories: true)
        let known = #"{"createdFile":false,"createdParent":false,"file":"/f/settings.json","folder":"/f","fromTheFuture":{"x":1},"kind":"statusline"}"#
        let odd = #"{ "kind" : "configEdit",   "folder" : "/h" }"#
        try Data("[\n\(known),\n\(odd)\n]\n".utf8).write(to: r.installer.receiptsURL)
        var receipts = r.installer.loadReceipts()
        #expect(receipts.count == 1)
        receipts.append(IntegrationReceipt(folder: "/g", file: "/g/settings.json", kind: .hooks))
        r.installer.saveReceipts(receipts)
        let text = try String(contentsOf: r.installer.receiptsURL, encoding: .utf8)
        #expect(text.contains(known))
        #expect(text.contains(odd))
        let back = r.installer.readReceipts()
        #expect(back.records.map(\.kind) == [.statusline, .hooks] && back.unreadable == [Data(odd.utf8)])
        // A changed record is written fresh.
        receipts[0].createdFile = true
        r.installer.saveReceipts(receipts)
        #expect(!(try String(contentsOf: r.installer.receiptsURL, encoding: .utf8)).contains("fromTheFuture"))
        #expect(r.installer.readReceipts().records.first?.createdFile == true)
    }

    @Test func aLinkedAccountsSideDecidesOnceChosen() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude-a/projects")
        let d = MemoryDefaults()
        AccountData.link(h.at(".claude-a"), to: "Unflagged", in: d)
        SeatPreferences.setMark(.work, for: h.at(".claude-a"), in: d)
        // No side chosen on Accounts: the folder's own toggle still decides.
        let seat = Seats.seat(h.at(".claude-a"), origin: .found, home: h.path, store: d)
        #expect(seat.mark == .work && seat.markSource == .toggle && seat.label == "Unflagged")
        AccountData.setSide(.personal, for: "Unflagged", in: d)
        let personal = Seats.seat(h.at(".claude-a"), origin: .found, home: h.path, store: d)
        #expect(personal.mark == .personal && personal.markSource == .account)
        AccountData.setWork(true, for: "Unflagged", in: d)
        let flagged = Seats.seat(h.at(".claude-a"), origin: .found, home: h.path, store: d)
        #expect(flagged.mark == .work && flagged.markSource == .account)
        // Back to not set: the toggle again.
        AccountData.setSide(.unmarked, for: "Unflagged", in: d)
        #expect(Seats.seat(h.at(".claude-a"), origin: .found, home: h.path, store: d).markSource == .toggle)
    }

    @Test func anAccountsSideKeepsTheOldWorkField() {
        let d = MemoryDefaults()
        AccountData.setSide(.work, for: "A", in: d)
        AccountData.setSide(.personal, for: "B", in: d)
        let raw = d.object(forKey: AccountData.key) as? [String: [String: String]]
        // An older build reads `work`; `side` carries only personal.
        #expect(raw?["A"]?["work"] == "true" && raw?["A"]?["side"] == nil)
        #expect(raw?["B"]?["side"] == "personal" && raw?["B"]?["work"] == nil)
        #expect(AccountData.choices(for: "A", in: d).work && !AccountData.choices(for: "B", in: d).work)
        #expect(AccountData.choices(for: "B", in: d).side == .personal)
        AccountData.setWork(false, for: "B", in: d)          // turning work off leaves personal alone
        #expect(AccountData.choices(for: "B", in: d).side == .personal)
        AccountData.setSide(.unmarked, for: "B", in: d)
        #expect(d.object(forKey: AccountData.key) as? [String: [String: String]] == ["A": raw?["A"] ?? [:]])
    }

    @Test func addedFoldersListOnlyWhileTheyLookLikeHomes() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir("kept/projects")
        h.dir("emptied")
        let folders = ModsPageModel.folders(home: h.path, environment: [:], added: [h.at("kept"), h.at("emptied"), h.at("gone")],
                                            linked: [], installed: [])
        #expect(folders == [h.at("kept")])
    }

    @Test func valuesInsideCommandsAreMasked() {
        func shows(_ s: String) -> String { ConfigMask.maskString(s) }
        // URL userinfo and secret-named query parameters.
        #expect(shows("https://user:hunter2@example.com/x") == "https://\(ConfigMask.masked)@example.com/x")
        #expect(shows("https://example.com/x?token=abc123&page=2") == "https://example.com/x?token=\(ConfigMask.masked)&page=2")
        #expect(shows("https://example.com/?api_key=zzz") == "https://example.com/?api_key=\(ConfigMask.masked)")
        // Flags: --flag=value and --flag value.
        #expect(shows("server --api-" + "key=" + "abc123 --port=8080") == "server --api-key=\(ConfigMask.masked) --port=8080")
        #expect(shows("server --to" + "ken " + "abc123 --verbose") == "server --token \(ConfigMask.masked) --verbose")
        #expect(shows("run --verbose --name me") == "run --verbose --name me")
        // NAME=value in a command, with a secret-named NAME.
        #expect(shows("GITHUB_TOKEN=ghx123 PORT=1 python3 x.py") == "GITHUB_TOKEN=\(ConfigMask.masked) PORT=1 python3 x.py")
        #expect(shows("Bash(export DB_PASSWORD='p w')") == "Bash(export DB_PASSWORD=\(ConfigMask.masked))")
        // Headers and bearer tokens, in a permission rule or a hook command.
        #expect(!shows("Bash(curl -H 'Authorization: Basic dXNlcjpwYXNz' x)").contains("dXNlcjpwYXNz"))
        #expect(!shows("curl -H \"Cookie: session=s3cr3t\" x").contains("s3cr3t"))
        #expect(shows("Bearer " + "eyJ" + "hbGciOi.payload") == "Bearer \(ConfigMask.masked)")
        // Key-shaped strings anywhere.
        #expect(!shows("echo " + "sk-" + "ant-api03-ABCDEFGHIJKLMNOP | x").contains("ABCDEFGHIJ"))
        #expect(!shows("key " + "sk-" + "proj1234567890abc").contains("1234567890"))
        #expect(!shows("gh" + "p_" + "abcdefghijklmnopqrstuvwxyz0123").contains("abcdefghij"))
        // Plain commands stay readable.
        #expect(shows("python3 /Users/x/statusline.py --compact") == "python3 /Users/x/statusline.py --compact")
    }

    @Test func argsListsAndNewSecretWords() {
        let server: [String: Any] = ["command": "npx",
                                     "args": ["-y", "server", "--api-key", "abc123", "--port", "8080", "--auth-token=zzz"],
                                     "url": "https://u:p@example.com/mcp"]
        let shown = ConfigMask.display(server, at: ["mcpServers", "s"])
        for leaked in ["abc123", "zzz", "u:p@"] { #expect(!shown.contains(leaked), "\(leaked) leaked") }
        for kept in ["--api-key", "8080", "--port", "server"] { #expect(shown.contains(kept), "\(kept) missing") }
        #expect(ConfigMask.isSecret(["auth"]) && ConfigMask.isSecret(["oauthClient"]) && ConfigMask.isSecret(["Authorization"]))
        #expect(ConfigMask.isSecret(["bearerToken"]) && ConfigMask.isSecret(["cookies"]))
        #expect(!ConfigMask.isSecret(["author"]) && !ConfigMask.isSecret(["authors", "0"]))
        // statusLine and hook commands are masked by value even though their paths aren't secret.
        let settings: [String: Any] = ["statusLine": ["type": "command", "command": "API_KEY=xyz789 bash s.sh"],
                                       "hooks": ["Stop": [["hooks": [["type": "command", "command": "curl -H 'Authorization: Bearer qqq' x"]]]]],
                                       "permissions": ["allow": ["Bash(export SECRET_X=val1)"]]]
        let all = ConfigMask.display(settings)
        for leaked in ["xyz789", "qqq", "val1"] { #expect(!all.contains(leaked), "\(leaked) leaked") }
    }

    // MARK: Running sessions

    /// A buffer as the kernel lays it out: the executable path NUL-padded to 8 bytes.
    static func procargs(executable: String, args: [String], env: [String]) -> [UInt8] {
        var argc = Int32(args.count)
        var b = withUnsafeBytes(of: &argc) { Array($0) }
        let path = Array(executable.utf8)
        b += path + [UInt8](repeating: 0, count: (path.count + 1 + 7) / 8 * 8 - path.count)
        for a in args { b += Array(a.utf8) + [0] }
        for e in env { b += Array(e.utf8) + [0] }
        return b + [0, 0]
    }

    @Test func aProcessSeatComesFromItsLaunchEnvironment() throws {
        let buffer = Self.procargs(executable: "/opt/homebrew/bin/claude", args: ["claude", "--resume"],
                                   env: ["HOME=/Users/x", "ANTHROPIC_API_KEY=sk-ant-secret", "CLAUDE_CONFIG_DIR=/Users/x/.claude-work/"])
        let p = try #require(ClaudeProcesses.parse(buffer))
        #expect(p.executable == "/opt/homebrew/bin/claude")
        #expect(p.configDir == "/Users/x/.claude-work/")
        #expect(p.isClaudeCode)
        #expect(ClaudeProcesses.seat(configDir: p.configDir, home: "/Users/x") == "/Users/x/.claude-work")
        // Nothing else of the environment is kept.
        #expect(!String(describing: p).contains("sk-ant"))

        let plain = try #require(ClaudeProcesses.parse(Self.procargs(executable: "/x/versions/2.1.296", args: ["claude"], env: ["CLAUDE_CONFIG_DIR="])))
        #expect(plain.configDir == nil && plain.isClaudeCode)
        #expect(ClaudeProcesses.seat(configDir: nil, home: "/Users/x") == "/Users/x/.claude")
        #expect(ClaudeProcesses.seat(configDir: "~/.claude-b", home: "/Users/x") == "/Users/x/.claude-b")

        let node = try #require(ClaudeProcesses.parse(Self.procargs(executable: "/usr/local/bin/node",
                                                                   args: ["node", "/usr/local/lib/node_modules/@anthropic-ai/claude-code/cli.js"], env: [])))
        #expect(node.isClaudeCode)
        let desktop = try #require(ClaudeProcesses.parse(Self.procargs(executable: "/Applications/Claude.app/Contents/MacOS/Claude",
                                                                      args: ["/Applications/Claude.app/Contents/MacOS/Claude"], env: [])))
        #expect(!desktop.isClaudeCode)
        // A launcher running a `claude` script counts; an editor opening one doesn't.
        #expect(ClaudeProcesses.isClaudeCode(executable: "/usr/local/bin/node", arguments: ["node", "/usr/local/bin/claude", "-c"]))
        #expect(!ClaudeProcesses.isClaudeCode(executable: "/usr/bin/vim", arguments: ["vim", "/Users/x/bin/claude"]))
    }

    @Test func malformedBuffersParseToNothing() {
        #expect(ClaudeProcesses.parse([1, 0]) == nil)                                   // shorter than argc
        // argc larger than the arguments there.
        #expect(ClaudeProcesses.parse([5, 0, 0, 0] + Array("/x".utf8) + [0, 0, 0, 0, 0, 0] + Array("a".utf8) + [0]) == nil)
        // A huge or negative argc.
        #expect(ClaudeProcesses.parse([0xff, 0xff, 0xff, 0x7f] + Array("/x".utf8) + [0, 0, 0, 0, 0, 0]) == nil)
        #expect(ClaudeProcesses.parse([0xff, 0xff, 0xff, 0xff] + Array("/x".utf8) + [0, 0, 0, 0, 0, 0]) == nil)
        // No NUL after the executable path, or padding cut short.
        #expect(ClaudeProcesses.parse([1, 0, 0, 0] + Array("/opt/bin/claude".utf8)) == nil)
        #expect(ClaudeProcesses.parse([1, 0, 0, 0] + Array("/x".utf8) + [0]) == nil)
        // Truncated inside an argument: no NUL before the end.
        let cut = Self.procargs(executable: "/opt/bin/claude", args: ["claude-code"], env: [])
        #expect(ClaudeProcesses.parse(Array(cut.prefix(cut.count - 5))) == nil)
        // An empty argv[0] isn't swallowed by the padding: the variable after it is still read.
        let empty = Self.procargs(executable: "/opt/bin/claude", args: ["", "--resume"], env: ["CLAUDE_CONFIG_DIR=/s"])
        #expect(ClaudeProcesses.parse(empty)?.configDir == "/s")
        #expect(ClaudeProcesses.parse(empty)?.isClaudeCode == true)
        // An environment cut off without its final NUL still parses what came before.
        let open = Self.procargs(executable: "/opt/bin/claude", args: ["claude"], env: ["CLAUDE_CONFIG_DIR=/s", "X=1"]).dropLast(3)
        #expect(ClaudeProcesses.parse(Array(open))?.configDir == "/s")
    }

    /// The kernel hands this test process its own arguments the way it hands Sanduhr a `claude`
    /// of the same user. Only this process is read.
    @Test func readsTheArgumentsOfAProcessOfThisUser() throws {
        let buffer = try #require(ClaudeProcesses.procargs(getpid()))
        let p = try #require(ClaudeProcesses.parse(buffer))
        #expect(!p.executable.isEmpty)
        #expect(!p.isClaudeCode)
    }
}

/// Every file under a temp home with its bytes and modification date, to show a read wrote
/// nothing.
struct TempHomeSnapshot: Equatable {
    let files: [String: Data]
    let dates: [String: Date]

    init(_ h: TempHome) {
        var files: [String: Data] = [:], dates: [String: Date] = [:]
        let fm = FileManager.default
        if let e = fm.enumerator(atPath: h.path) {
            while let rel = e.nextObject() as? String {
                let p = h.at(rel)
                dates[rel] = (try? fm.attributesOfItem(atPath: p))?[.modificationDate] as? Date
                var isDir: ObjCBool = false
                if fm.fileExists(atPath: p, isDirectory: &isDir), !isDir.boolValue { files[rel] = fm.contents(atPath: p) }
            }
        }
        self.files = files
        self.dates = dates
    }
}

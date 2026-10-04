import Foundation
import Testing
@testable import Sanduhr

/// Item 47: `mcp-access.json`, what the MCP server may read per account. Made-up labels and
/// paths, temp folders only.
@Suite("MCP access file")
struct MCPAccessTests {
    private let folder = "/tmp/sanduhr-test-home/.claude-test"

    @Test func sharingOffLeavesTheAccountOut() {
        let e = MCPAccess.entries(labels: ["Home", "Work"], active: "Home",
                                  choices: ["Work": AccountDataChoices(activity: .record, share: .off, folder: folder)])
        #expect(e.isEmpty)
        let json = String(decoding: MCPAccess.encode(e), as: UTF8.self)
        #expect(json == #"{"accounts":[],"schema_version":1}"#)
    }

    @Test func metersShareTheHistoryFileOnly() {
        let c = AccountDataChoices(activity: .record, names: .names, share: .meters, folder: folder)
        let e = MCPAccess.entries(labels: ["Home"], active: "Home", choices: ["Home": c])
        #expect(e == [MCPAccessEntry(accountRef: AccountRef.of("Home")!, active: true, share: .meters,
                                     historyFile: "history.Home.json", names: nil, vaultID: nil, liveFolder: nil)])
        let json = String(decoding: MCPAccess.encode(e), as: UTF8.self)
        #expect(!json.contains(folder))
        #expect(!json.contains("vault_id"))
        #expect(!json.contains("names"))
    }

    @Test func activityWithARecordSharesTheVaultAndTheFolder() {
        let c = AccountDataChoices(activity: .record, names: .hidden, share: .activity, folder: folder + "/")
        let e = MCPAccess.entries(labels: ["Home", "Work"], active: "Home", choices: ["Work": c])
        #expect(e.count == 1)
        #expect(e[0].accountRef == AccountRef.of("Work"))
        #expect(e[0].active == false)
        #expect(e[0].names == .hidden)
        #expect(e[0].vaultID == VaultFolderID.of(folder))
        #expect(e[0].vaultID?.count == 16)
        #expect(e[0].liveFolder == folder)
    }

    @Test func liveOnlySharesTheFolderWithoutAVault() {
        let c = AccountDataChoices(activity: .live, share: .activity, folder: folder)
        let e = MCPAccess.entries(labels: ["Home"], active: "Home", choices: ["Home": c])
        #expect(e[0].liveFolder == folder)
        #expect(e[0].vaultID == nil)
        #expect(e[0].names == .names)
    }

    @Test func activityWithNothingReadSharesNoPath() {
        let notTracked = AccountDataChoices(activity: .off, share: .activity, folder: folder)
        let noFolder = AccountDataChoices(activity: .record, share: .activity, folder: nil)
        let e = MCPAccess.entries(labels: ["A", "B"], active: "A", choices: ["A": notTracked, "B": noFolder])
        #expect(e.count == 2)
        #expect(e.allSatisfy { $0.liveFolder == nil && $0.vaultID == nil && $0.share == .activity })
    }

    @Test func entriesFollowTheListOrderAndTheActiveAccount() {
        let m = AccountDataChoices(share: .meters)
        let e = MCPAccess.entries(labels: ["B", "A", "C"], active: "C", choices: ["A": m, "B": m, "C": m])
        #expect(e.map(\.accountRef) == ["B", "A", "C"].map { AccountRef.of($0)! })
        #expect(e.map(\.active) == [false, false, true])
    }

    @Test func theFileShapeIsTheSchema() throws {
        let c = AccountDataChoices(activity: .record, names: .full, share: .activity, folder: folder)
        let data = MCPAccess.encode(MCPAccess.entries(labels: ["Work"], active: "Work", choices: ["Work": c]))
        let root = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(root["schema_version"] as? Int == 1)
        let accounts = try #require(root["accounts"] as? [[String: Any]])
        let a = try #require(accounts.first)
        #expect(Set(a.keys) == ["account_ref", "active", "share", "history_file", "names", "vault_id", "live_folder"])
        #expect(a["share"] as? String == "activity")
        #expect(a["names"] as? String == "full")
        #expect(a["live_folder"] as? String == folder)
        // No escaped slashes, and the label appears only inside the history file name.
        let text = String(decoding: data, as: UTF8.self)
        #expect(!text.contains("\\/"))
        #expect(text.components(separatedBy: "Work").count == 2)
    }

    @Test func writeIsAtomicOwnerOnlyAndSkipsAnUnchangedFile() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-mcp-access-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent(MCPAccess.fileName)
        let m = ["Home": AccountDataChoices(share: .meters)]
        #expect(MCPAccess.sync(labels: ["Home"], active: "Home", choices: m, to: url) == .written)
        #expect(MCPAccess.sync(labels: ["Home"], active: "Home", choices: m, to: url) == .unchanged)
        let attrs = try FileManager.default.attributesOfItem(atPath: url.path)
        #expect((attrs[.posixPermissions] as? NSNumber)?.intValue == 0o600)
        #expect(MCPAccess.sync(labels: ["Home"], active: "Home", choices: [:], to: url) == .written)
        let text = try String(contentsOf: url, encoding: .utf8)
        #expect(text == #"{"accounts":[],"schema_version":1}"#)
        // No temporary file is left beside it.
        let names = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        #expect(names == [MCPAccess.fileName])
    }
}

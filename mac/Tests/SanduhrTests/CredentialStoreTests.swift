import Foundation
import Testing
@testable import Sanduhr

/// An in-memory backend. `failSet`/`failDelete` make those calls throw; `corrupt` makes reads of
/// those accounts return something other than what was written.
final class FakeBackend: CredentialBackend, @unchecked Sendable {
    struct Boom: Error {}
    var items: [String: String]
    var failSet: Set<String> = []
    var failDelete: Set<String> = []
    var corrupt: Set<String> = []

    init(_ items: [String: String] = [:]) { self.items = items }

    func get(account: String) -> String? {
        guard let v = items[account], !v.isEmpty else { return nil }
        return corrupt.contains(account) ? v + "x" : v
    }

    func set(_ value: String, account: String) throws {
        if failSet.contains(account) { throw Boom() }
        items[account] = value
    }

    func delete(account: String) throws {
        if failDelete.contains(account) { throw Boom() }
        items.removeValue(forKey: account)
    }
}

@Suite("Credential store plan")
struct CredentialStorePlanTests {
    @Test func unsignedUsesTheFileWhateverIsStored() {
        for fileHas in [false, true] {
            for keychainHas in [false, true] {
                let plan = CredentialStorePlan.decide(isSigned: false, fileHas: fileHas, keychainHas: keychainHas)
                #expect(plan == CredentialStorePlan(store: .file, migrate: false))
            }
        }
    }

    @Test func signedWithFileOnlyMigrates() {
        let plan = CredentialStorePlan.decide(isSigned: true, fileHas: true, keychainHas: false)
        #expect(plan == CredentialStorePlan(store: .keychain, migrate: true))
    }

    @Test func signedWithKeychainOnlyUsesIt() {
        let plan = CredentialStorePlan.decide(isSigned: true, fileHas: false, keychainHas: true)
        #expect(plan == CredentialStorePlan(store: .keychain, migrate: false))
    }

    @Test func signedWithBothMovesTheNewerFile() {
        let plan = CredentialStorePlan.decide(isSigned: true, fileHas: true, keychainHas: true)
        #expect(plan == CredentialStorePlan(store: .keychain, migrate: true))
    }

    @Test func signedWithNothingUsesTheKeychain() {
        let plan = CredentialStorePlan.decide(isSigned: true, fileHas: false, keychainHas: false)
        #expect(plan == CredentialStorePlan(store: .keychain, migrate: false))
    }
}

@Suite("Credential migration")
struct CredentialMigrationTests {
    let accounts = KeychainAccount.all

    @Test func movesBothValuesThenDeletesTheFile() {
        let file = FakeBackend(["sessionKey": "sk-1", "cf_clearance": "cf-1"])
        let keychain = FakeBackend()
        #expect(CredentialMigration.run(from: file, to: keychain, accounts: accounts) == .migrated)
        #expect(keychain.items == ["sessionKey": "sk-1", "cf_clearance": "cf-1"])
        #expect(file.items.isEmpty)
    }

    @Test func emptyFileHasNothingToMove() {
        let file = FakeBackend()
        let keychain = FakeBackend(["sessionKey": "kept"])
        #expect(CredentialMigration.run(from: file, to: keychain, accounts: accounts) == .nothingToMove)
        #expect(keychain.items == ["sessionKey": "kept"])
    }

    @Test func fileWinsOverAStaleKeychain() {
        let file = FakeBackend(["sessionKey": "new"])
        let keychain = FakeBackend(["sessionKey": "old", "cf_clearance": "old-cf"])
        #expect(CredentialMigration.run(from: file, to: keychain, accounts: accounts) == .migrated)
        #expect(keychain.items == ["sessionKey": "new"])
        #expect(file.items.isEmpty)
    }

    @Test func writeFailureKeepsTheFile() {
        let file = FakeBackend(["sessionKey": "sk-1", "cf_clearance": "cf-1"])
        let keychain = FakeBackend()
        keychain.failSet = ["cf_clearance"]
        let outcome = CredentialMigration.run(from: file, to: keychain, accounts: accounts)
        #expect(outcome == .keptFileBecause(.writeFailed(account: "cf_clearance")))
        #expect(outcome.store == .file)
        #expect(file.items == ["sessionKey": "sk-1", "cf_clearance": "cf-1"])
    }

    @Test func readBackMismatchKeepsTheFile() {
        let file = FakeBackend(["sessionKey": "sk-1"])
        let keychain = FakeBackend()
        keychain.corrupt = ["sessionKey"]
        let outcome = CredentialMigration.run(from: file, to: keychain, accounts: accounts)
        #expect(outcome == .keptFileBecause(.readBackMismatch(account: "sessionKey")))
        #expect(file.items == ["sessionKey": "sk-1"])
    }

    @Test func failingToClearAStaleKeychainItemKeepsTheFile() {
        let file = FakeBackend(["sessionKey": "sk-1"])
        let keychain = FakeBackend(["cf_clearance": "stale"])
        keychain.failDelete = ["cf_clearance"]
        let outcome = CredentialMigration.run(from: file, to: keychain, accounts: accounts)
        #expect(outcome == .keptFileBecause(.clearFailed(account: "cf_clearance")))
        #expect(file.items == ["sessionKey": "sk-1"])
    }

    @Test func failingToDeleteTheFileKeepsUsingIt() {
        let file = FakeBackend(["sessionKey": "sk-1"])
        file.failDelete = ["sessionKey"]
        let keychain = FakeBackend()
        let outcome = CredentialMigration.run(from: file, to: keychain, accounts: accounts)
        #expect(outcome == .keptFileBecause(.sourceDeleteFailed(account: "sessionKey")))
        #expect(outcome.store == .file)
        #expect(file.items == ["sessionKey": "sk-1"])
    }

    @Test func outcomesPickTheirStore() {
        #expect(CredentialMigrationOutcome.migrated.store == .keychain)
        #expect(CredentialMigrationOutcome.nothingToMove.store == .keychain)
    }
}

@Suite("Credential store resolve")
struct CredentialResolveTests {
    let accounts = KeychainAccount.all

    @Test func unsignedNeverTouchesTheKeychain() {
        let file = FakeBackend(["sessionKey": "sk-1"])
        let keychain = FakeBackend(["sessionKey": "other"])
        let r = CredentialMigration.resolve(isSigned: false, file: file, keychain: keychain, accounts: accounts)
        #expect(r.store == .file)
        #expect(r.outcome == nil)
        #expect(file.items == ["sessionKey": "sk-1"])
        #expect(keychain.items == ["sessionKey": "other"])
    }

    @Test func signedFileOnlyMigrates() {
        let file = FakeBackend(["sessionKey": "sk-1"])
        let keychain = FakeBackend()
        let r = CredentialMigration.resolve(isSigned: true, file: file, keychain: keychain, accounts: accounts)
        #expect(r.store == .keychain)
        #expect(r.outcome == .migrated)
        #expect(keychain.items == ["sessionKey": "sk-1"])
        #expect(file.items.isEmpty)
    }

    @Test func signedKeychainOnlyRunsNoMigration() {
        let keychain = FakeBackend(["sessionKey": "sk-1"])
        let r = CredentialMigration.resolve(isSigned: true, file: FakeBackend(), keychain: keychain, accounts: accounts)
        #expect(r.store == .keychain)
        #expect(r.outcome == nil)
    }

    @Test func signedWriteFailureFallsBackToTheFile() {
        let file = FakeBackend(["sessionKey": "sk-1"])
        let keychain = FakeBackend()
        keychain.failSet = ["sessionKey"]
        let r = CredentialMigration.resolve(isSigned: true, file: file, keychain: keychain, accounts: accounts)
        #expect(r.store == .file)
        #expect(r.outcome == .keptFileBecause(.writeFailed(account: "sessionKey")))
        #expect(file.items == ["sessionKey": "sk-1"])
    }
}

/// The file backend against a temporary directory, never the real credentials file.
@Suite("File backend")
struct FileBackendTests {
    func temp() -> FileBackend {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-creds-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return FileBackend(url: dir.appendingPathComponent("credentials.json"))
    }

    @Test func writesOwnerOnlyAndRemovesTheFileWhenEmpty() throws {
        let file = temp()
        defer { try? FileManager.default.removeItem(at: file.url.deletingLastPathComponent()) }
        try file.set("sk-1", account: "sessionKey")
        try file.set("cf-1", account: "cf_clearance")
        #expect(file.get(account: "sessionKey") == "sk-1")
        let attrs = try FileManager.default.attributesOfItem(atPath: file.url.path)
        #expect((attrs[.posixPermissions] as? NSNumber)?.intValue == 0o600)
        try file.delete(account: "sessionKey")
        #expect(file.exists(account: "sessionKey") == false)
        #expect(FileManager.default.fileExists(atPath: file.url.path))
        try file.delete(account: "cf_clearance")
        #expect(FileManager.default.fileExists(atPath: file.url.path) == false)
        try file.delete(account: "cf_clearance")    // missing is not an error
    }

    @Test func migratesIntoAFakeKeychainAndLeavesNoFile() throws {
        let file = temp()
        defer { try? FileManager.default.removeItem(at: file.url.deletingLastPathComponent()) }
        try file.set("sk-1", account: "sessionKey")
        let keychain = FakeBackend()
        #expect(CredentialMigration.run(from: file, to: keychain, accounts: KeychainAccount.all) == .migrated)
        #expect(FileManager.default.fileExists(atPath: file.url.path) == false)
        #expect(keychain.items == ["sessionKey": "sk-1"])
    }
}

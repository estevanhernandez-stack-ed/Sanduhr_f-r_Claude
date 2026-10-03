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

@Suite("Sign out")
struct SignOutTests {
    let accounts = KeychainAccount.all

    func run(_ keychain: FakeBackend, _ file: any CredentialBackend) -> SignOutResult {
        SignOut.run(backends: [(kind: .keychain, backend: keychain), (kind: .file, backend: file)],
                    accounts: accounts)
    }

    @Test func clearsBothStores() {
        let keychain = FakeBackend(["sessionKey": "sk-k", "cf_clearance": "cf-k"])
        let file = FakeBackend(["sessionKey": "sk-f", "cf_clearance": "cf-f"])
        let result = run(keychain, file)
        #expect(result.succeeded)
        #expect(keychain.items.isEmpty)
        #expect(file.items.isEmpty)
        #expect(result.outcomes.map(\.store) == [.keychain, .keychain, .file, .file])
        #expect(result.outcomes.map(\.account) == accounts + accounts)
    }

    @Test func eitherStoreEmptyIsStillASuccess() {
        let keychainOnly = FakeBackend(["sessionKey": "sk-k"])
        let emptyFile = FakeBackend()
        #expect(run(keychainOnly, emptyFile).succeeded)
        #expect(keychainOnly.items.isEmpty)

        let emptyKeychain = FakeBackend()
        let fileOnly = FakeBackend(["sessionKey": "sk-f", "cf_clearance": "cf-f"])
        #expect(run(emptyKeychain, fileOnly).succeeded)
        #expect(fileOnly.items.isEmpty)
    }

    @Test func aFailedDeleteIsReportedWithoutValuesAndTheRestStillRun() {
        let keychain = FakeBackend(["sessionKey": "sk-secret", "cf_clearance": "cf-secret"])
        keychain.failDelete = ["sessionKey"]
        let file = FakeBackend(["sessionKey": "sk-file"])
        let result = run(keychain, file)
        #expect(!result.succeeded)
        #expect(result.failures.map(\.account) == ["sessionKey"])
        #expect(result.failures.map(\.store) == [.keychain])
        #expect(keychain.items == ["sessionKey": "sk-secret"])   // only the failed one is left
        #expect(file.items.isEmpty)
        for f in result.failures {
            #expect(!(f.error ?? "").contains("secret"))
        }
    }

    @Test func aDeleteThatLeavesTheValueIsAFailure() {
        let keychain = FakeBackend()
        let sticky = StickyBackend(["sessionKey": "sk-1"])
        let result = run(keychain, sticky)
        #expect(result.failures.map(\.account) == ["sessionKey"])
        #expect(result.failures.map(\.store) == [.file])
    }

    @Test func anythingStoredLooksInEveryStoreAndAccount() {
        func stored(_ keychain: FakeBackend, _ file: FakeBackend) -> Bool {
            SignOut.anythingStored(backends: [(kind: .keychain, backend: keychain),
                                              (kind: .file, backend: file)],
                                   accounts: accounts)
        }
        #expect(!stored(FakeBackend(), FakeBackend()))
        #expect(stored(FakeBackend(["sessionKey": "sk-k"]), FakeBackend()))
        #expect(stored(FakeBackend(), FakeBackend(["cf_clearance": "cf-f"])))

        let keychain = FakeBackend(["sessionKey": "sk-k"])
        let file = FakeBackend(["cf_clearance": "cf-f"])
        #expect(stored(keychain, file))
        _ = run(keychain, file)
        #expect(!stored(keychain, file))   // after Sign Out the button reads Signed Out
    }

    @Test func clearsARealFileInATemporaryFolder() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-signout-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let file = FileBackend(url: dir.appendingPathComponent("credentials.json"))
        try file.set("sk-1", account: "sessionKey")
        try file.set("cf-1", account: "cf_clearance")
        #expect(run(FakeBackend(), file).succeeded)
        #expect(FileManager.default.fileExists(atPath: file.url.path) == false)
    }

    @Test func snapshotIsWrittenSignedOutWithNoTiers() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-snapshot-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("snapshot.json")
        SnapshotWriter.writeSignedOut(now: Date(timeIntervalSince1970: 0), to: url)
        let data = try Data(contentsOf: url)
        let obj = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(obj["status"] as? String == "error")
        #expect(obj["error_kind"] as? String == "session_expired")
        #expect((obj["tiers"] as? [Any])?.isEmpty == true)
        #expect(obj["schema_version"] as? Int == SnapshotWriter.schemaVersion)
    }

    @Test func signedOutNeedsSignIn() {
        #expect(UsageViewModel.StatusMessage.signedOut.needsSignIn)
        #expect(UsageViewModel.StatusMessage.error("x", isAuth: true).needsSignIn)
        #expect(!UsageViewModel.StatusMessage.error("x", isAuth: false).needsSignIn)
        #expect(!UsageViewModel.StatusMessage.idle.needsSignIn)
        #expect(!UsageViewModel.StatusMessage.signedOut.isError)
    }
}

/// A backend whose deletes return without removing anything.
final class StickyBackend: CredentialBackend, @unchecked Sendable {
    var items: [String: String]
    init(_ items: [String: String]) { self.items = items }
    func get(account: String) -> String? { items[account] }
    func set(_ value: String, account: String) throws { items[account] = value }
    func delete(account: String) throws {}
}

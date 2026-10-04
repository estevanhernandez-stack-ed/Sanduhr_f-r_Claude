import Foundation
import Testing
@testable import Sanduhr

/// A registry over two fake stores (the Keychain one active) and in-memory defaults: nothing
/// here reaches the real Keychain, credentials file or defaults.
struct RegistryFixture {
    let keychain: FakeBackend
    let file: FakeBackend
    let defaults = MemoryDefaults()
    let registry: AccountRegistry

    init(keychain: [String: String] = [:], file: [String: String] = [:], labels: [String]? = nil, active: String? = nil) {
        self.keychain = FakeBackend(keychain)
        self.file = FakeBackend(file)
        if let labels { defaults.set(labels, forKey: AccountRegistry.listKey) }
        if let active { defaults.set(active, forKey: AccountRegistry.activeKey) }
        registry = AccountRegistry(backend: self.keychain,
                                   stores: [(kind: .keychain, backend: self.keychain), (kind: .file, backend: self.file)],
                                   defaults: defaults)
    }
}

@Suite("Account labels")
struct AccountLabelTests {
    @Test func followsTheWindowsRule() {
        for good in ["Personal", "Work", "a", "Team 2_b-c", String(repeating: "x", count: 32)] {
            #expect(AccountRegistry.isValid(good), "\(good)")
        }
        for bad in ["", String(repeating: "x", count: 33), "Work\n", "Wörk", "a:b", "a/b", "a.b", "Ωmega"] {
            #expect(!AccountRegistry.isValid(bad), "\(bad)")
        }
    }

    /// Item 38: the Add Account and Rename fields flag a duplicate as you type, by the rule
    /// `add` and `rename` throw on.
    @Test func labelProblemAsYouType() {
        let existing = ["Personal", "Work"]
        #expect(AccountRegistry.labelProblem("", existing: existing) == nil)
        #expect(AccountRegistry.labelProblem("Team", existing: existing) == nil)
        #expect(AccountRegistry.labelProblem("Work!", existing: existing) == .invalidLabel)
        #expect(AccountRegistry.labelProblem("personal", existing: existing) == .duplicateLabel)
        #expect(AccountRegistry.labelProblem("WORK", existing: existing) == .duplicateLabel)
        // Renaming Work: its own name in another case is fine, another account's is not.
        #expect(AccountRegistry.labelProblem("work", existing: existing, except: "Work") == nil)
        #expect(AccountRegistry.labelProblem("Work", existing: existing, except: "Work") == nil)
        #expect(AccountRegistry.labelProblem("PERSONAL", existing: existing, except: "Work") == .duplicateLabel)
        #expect(AccountError.duplicateLabel.description == "an account with that label already exists")
    }

    @Test func slotsUseTheWindowsNames() {
        #expect(AccountRegistry.slot(KeychainAccount.sessionKey, for: "Work") == "sessionKey:Work")
        #expect(AccountRegistry.slot(KeychainAccount.cfClearance, for: "Work") == "cf_clearance:Work")
        #expect(AccountRegistry.slots(for: nil) == ["sessionKey", "cf_clearance"])
        #expect(AccountRegistry.slotKind("cf_clearance:Team 2") == "cf_clearance")
        #expect(AccountRegistry.slotKind("sessionKey") == "sessionKey")
    }
}

@Suite("Account registry")
struct AccountRegistryTests {
    @Test func addListsInOrderAndTheFirstIsActive() throws {
        let f = RegistryFixture()
        #expect(f.registry.labels.isEmpty)
        #expect(f.registry.active == nil)
        try f.registry.add("Personal", sessionKey: "sk-p", cfClearance: "cf-p")
        try f.registry.add("Work", sessionKey: "sk-w")
        #expect(f.registry.labels == ["Personal", "Work"])
        #expect(f.registry.active == "Personal")
        #expect(f.keychain.items == ["sessionKey:Personal": "sk-p", "cf_clearance:Personal": "cf-p",
                                     "sessionKey:Work": "sk-w"])
        #expect(f.registry.credentials(for: "Work") == AccountCredentials(sessionKey: "sk-w", cfClearance: nil))
    }

    @Test func addRefusesBadOrDuplicateLabels() throws {
        let f = RegistryFixture()
        try f.registry.add("Work", sessionKey: "sk-w")
        #expect(throws: AccountError.invalidLabel) { try f.registry.add("a:b", sessionKey: "sk") }
        #expect(throws: AccountError.duplicateLabel) { try f.registry.add("work", sessionKey: "sk") }
        #expect(throws: AccountError.missingSessionKey) { try f.registry.add("Home", sessionKey: "") }
        #expect(f.registry.labels == ["Work"])
    }

    @Test func aFailedAddLeavesNothingBehind() {
        let f = RegistryFixture()
        f.keychain.failSet = ["cf_clearance:Work"]
        #expect(throws: AccountError.writeFailed(slot: "cf_clearance")) {
            try f.registry.add("Work", sessionKey: "sk-w", cfClearance: "cf-w")
        }
        #expect(f.keychain.items.isEmpty)
        #expect(f.registry.labels.isEmpty)
    }

    @Test func activeFallsBackToTheFirstListedAccount() throws {
        let f = RegistryFixture(labels: ["Personal", "Work"], active: "Gone")
        #expect(f.registry.active == "Personal")
        try f.registry.setActive("Work")
        #expect(f.registry.active == "Work")
        #expect(throws: AccountError.unknownLabel) { try f.registry.setActive("Gone") }
    }

    @Test func savedLabelsDropAnythingInvalid() {
        let f = RegistryFixture(labels: ["Personal", "bad:label", "Work"])
        #expect(f.registry.labels == ["Personal", "Work"])
    }

    @Test func saveReplacesKeepsOrDeletes() throws {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "old", "cf_clearance:Work": "cf"], labels: ["Work"])
        try f.registry.save("Work", sessionKey: "new")
        #expect(f.registry.credentials(for: "Work") == AccountCredentials(sessionKey: "new", cfClearance: "cf"))
        try f.registry.save("Work", cfClearance: "")
        #expect(f.keychain.items == ["sessionKey:Work": "new"])
        #expect(throws: AccountError.unknownLabel) { try f.registry.save("Home", sessionKey: "x") }
    }

    @Test func renameMovesTheSecretsAndTheActiveLabel() throws {
        let f = RegistryFixture(keychain: ["sessionKey:Personal": "sk-p", "cf_clearance:Personal": "cf-p",
                                           "sessionKey:Work": "sk-w"],
                                labels: ["Personal", "Work"], active: "Personal")
        try f.registry.rename("Personal", to: "Home")
        #expect(f.registry.labels == ["Home", "Work"])
        #expect(f.registry.active == "Home")
        #expect(f.keychain.items == ["sessionKey:Home": "sk-p", "cf_clearance:Home": "cf-p",
                                     "sessionKey:Work": "sk-w"])
    }

    @Test func renameChangingOnlyCaseIsAllowed() throws {
        let f = RegistryFixture(keychain: ["sessionKey:work": "sk-w"], labels: ["work"])
        try f.registry.rename("work", to: "Work")
        #expect(f.registry.labels == ["Work"])
        #expect(f.keychain.items == ["sessionKey:Work": "sk-w"])
    }

    @Test func renameRefusesClashesAndUnknowns() {
        let f = RegistryFixture(labels: ["Personal", "Work"])
        #expect(throws: AccountError.duplicateLabel) { try f.registry.rename("Personal", to: "WORK") }
        #expect(throws: AccountError.unknownLabel) { try f.registry.rename("Home", to: "Office") }
        #expect(throws: AccountError.invalidLabel) { try f.registry.rename("Work", to: "") }
    }

    @Test func aFailedRenameChangesNothing() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "sk-w", "cf_clearance:Work": "cf-w"], labels: ["Work"])
        f.keychain.corrupt = ["cf_clearance:Office"]
        #expect(throws: AccountError.readBackMismatch(slot: "cf_clearance")) {
            try f.registry.rename("Work", to: "Office")
        }
        #expect(f.registry.labels == ["Work"])
        #expect(f.keychain.items == ["sessionKey:Work": "sk-w", "cf_clearance:Work": "cf-w"])
    }

    @Test func signOutKeepsTheAccountAndClearsBothStores() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "sk-k", "sessionKey:Home": "sk-h"],
                                file: ["sessionKey:Work": "sk-f", "cf_clearance:Work": "cf-f", "sessionKey": "legacy"],
                                labels: ["Home", "Work"], active: "Work")
        #expect(f.registry.anythingToSignOut("Work"))
        let result = f.registry.signOut("Work")
        #expect(result.succeeded)
        #expect(f.registry.labels == ["Home", "Work"])
        #expect(f.registry.active == "Work")
        #expect(f.keychain.items == ["sessionKey:Home": "sk-h"])
        // The legacy slot belongs to Personal: signing out Work leaves it, so a launch still
        // running on it keeps its key.
        #expect(f.file.items == ["sessionKey": "legacy"])
        #expect(!f.registry.anythingToSignOut("Work"))
        #expect(f.registry.anythingToSignOut("Home"))
    }

    @Test func signingOutPersonalSweepsTheLegacySlots() {
        let f = RegistryFixture(keychain: ["sessionKey:Personal": "sk-p", "sessionKey": "legacy-k"],
                                file: ["sessionKey": "legacy-f", "cf_clearance": "cf-legacy"],
                                labels: ["Personal"], active: "Personal")
        #expect(f.registry.signOut("Personal").succeeded)
        #expect(f.keychain.items.isEmpty)
        #expect(f.file.items.isEmpty)
        #expect(AccountRegistry.signOutSlots(for: "Work") == ["sessionKey:Work", "cf_clearance:Work"])
        #expect(AccountRegistry.signOutSlots(for: nil) == ["sessionKey", "cf_clearance"])
    }

    @Test func aDevBuildClearsOnlyTheFile() {
        let keychain = FakeBackend(["sessionKey": "release-key"])
        let file = FakeBackend(["sessionKey:Personal": "dev-copy"])
        let dev = AccountRegistry.clearedStores(isSigned: false, keychain: keychain, file: file)
        #expect(dev.map(\.kind) == [.file])
        let result = SignOut.run(backends: dev, accounts: AccountRegistry.signOutSlots(for: "Personal"))
        #expect(result.succeeded)
        #expect(file.items.isEmpty)
        #expect(keychain.items == ["sessionKey": "release-key"])   // the release build's key survives
        let signed = AccountRegistry.clearedStores(isSigned: true, keychain: keychain, file: file)
        #expect(signed.map(\.kind) == [.keychain, .file])
    }

    @Test func removeDropsTheAccountAndMovesActiveToTheNext() {
        let f = RegistryFixture(keychain: ["sessionKey:A": "a", "sessionKey:B": "b", "sessionKey:C": "c"],
                                labels: ["A", "B", "C"], active: "B")
        #expect(f.registry.remove("B").succeeded)
        #expect(f.registry.labels == ["A", "C"])
        #expect(f.registry.active == "C")
        #expect(f.keychain.items == ["sessionKey:A": "a", "sessionKey:C": "c"])
        f.registry.remove("C")
        #expect(f.registry.active == "A")      // the last one wraps to the first
        f.registry.remove("A")
        #expect(f.registry.labels.isEmpty)
        #expect(f.registry.active == nil)
        #expect(f.defaults.object(forKey: AccountRegistry.activeKey) == nil)
    }

    @Test func removingAnInactiveAccountKeepsTheActiveOne() {
        let f = RegistryFixture(labels: ["A", "B"], active: "A")
        f.registry.remove("B")
        #expect(f.registry.active == "A")
        #expect(f.registry.remove("Missing").outcomes.isEmpty)
    }

    @Test func hasKeyIsPerAccountAndInThisStoreOnly() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "sk-w", "cf_clearance:Home": "cf-h"],
                                file: ["sessionKey:Home": "sk-f"], labels: ["Home", "Work"])
        #expect(f.registry.hasKey("Work"))
        // A cf_clearance alone is not signed in, and the other store's key does not count.
        #expect(!f.registry.hasKey("Home"))
        f.registry.signOut("Work")
        #expect(!f.registry.hasKey("Work"))
    }

    @Test func nextLabelCycles() {
        #expect(RegistryFixture(labels: ["A"]).registry.nextLabel == nil)
        #expect(RegistryFixture(labels: ["A", "B", "C"], active: "B").registry.nextLabel == "C")
        #expect(RegistryFixture(labels: ["A", "B", "C"], active: "C").registry.nextLabel == "A")
    }
}

@Suite("Legacy promotion")
struct LegacyPromotionTests {
    @Test func theLegacyKeyBecomesPersonal() {
        let f = RegistryFixture(keychain: ["sessionKey": "sk-1", "cf_clearance": "cf-1"])
        #expect(f.registry.promoteLegacy() == .promoted)
        #expect(f.registry.labels == ["Personal"])
        #expect(f.registry.active == "Personal")
        #expect(f.keychain.items == ["sessionKey:Personal": "sk-1", "cf_clearance:Personal": "cf-1"])
    }

    @Test func nothingToPromoteWithoutALegacyKey() {
        let fresh = RegistryFixture()
        #expect(fresh.registry.promoteLegacy() == .nothingToPromote)
        #expect(fresh.registry.labels.isEmpty)
        let cfOnly = RegistryFixture(keychain: ["cf_clearance": "cf"])
        #expect(cfOnly.registry.promoteLegacy() == .nothingToPromote)
    }

    @Test func runsOnlyOnce() {
        let f = RegistryFixture(keychain: ["sessionKey:Personal": "sk-p", "sessionKey": "stray"],
                                labels: ["Personal"])
        #expect(f.registry.promoteLegacy() == .nothingToPromote)
        #expect(f.keychain.items["sessionKey:Personal"] == "sk-p")
    }

    @Test func aFailedWriteKeepsTheLegacyKeyAndNoAccount() {
        let f = RegistryFixture(keychain: ["sessionKey": "sk-1", "cf_clearance": "cf-1"])
        f.keychain.failSet = ["cf_clearance:Personal"]
        #expect(f.registry.promoteLegacy() == .keptLegacyBecause(.writeFailed(account: "cf_clearance")))
        #expect(f.keychain.items == ["sessionKey": "sk-1", "cf_clearance": "cf-1"])
        #expect(f.registry.labels.isEmpty)
        // With no accounts the launch runs on the legacy slots.
        #expect(f.registry.active == nil)
        #expect(AccountRegistry.slot(KeychainAccount.sessionKey, for: f.registry.active) == "sessionKey")
    }

    @Test func aReadBackMismatchKeepsTheLegacyKey() {
        let f = RegistryFixture(keychain: ["sessionKey": "sk-1"])
        f.keychain.corrupt = ["sessionKey:Personal"]
        #expect(f.registry.promoteLegacy() == .keptLegacyBecause(.readBackMismatch(account: "sessionKey")))
        #expect(f.keychain.items == ["sessionKey": "sk-1"])
        #expect(f.registry.labels.isEmpty)
    }

    @Test func aStaleCFClearanceUnderPersonalIsCleared() {
        let f = RegistryFixture(keychain: ["sessionKey": "sk-1", "cf_clearance:Personal": "stale"])
        #expect(f.registry.promoteLegacy() == .promoted)
        #expect(f.keychain.items == ["sessionKey:Personal": "sk-1"])
    }

    @Test func fillsAListedPersonalThatHasNoKeyInThisStore() {
        // A dev build on the file beside a release build's registry.
        let f = RegistryFixture(keychain: ["sessionKey": "sk-1"], labels: ["Personal", "Work"], active: "Work")
        #expect(f.registry.promoteLegacy() == .promoted)
        #expect(f.registry.labels == ["Personal", "Work"])
        #expect(f.registry.active == "Work")
        #expect(f.keychain.items == ["sessionKey:Personal": "sk-1"])
    }

    @Test func leavesARegistryWithoutPersonalAlone() {
        let f = RegistryFixture(keychain: ["sessionKey": "sk-1"], labels: ["Home"])
        #expect(f.registry.promoteLegacy() == .nothingToPromote)
        #expect(f.keychain.items == ["sessionKey": "sk-1"])
    }
}

@Suite("Credential migration with accounts")
struct CredentialMigrationGroupTests {
    let groups = [AccountRegistry.slots(for: nil), AccountRegistry.slots(for: "Personal"), AccountRegistry.slots(for: "Work")]

    @Test func movesEveryAccountsPair() {
        let file = FakeBackend(["sessionKey:Personal": "p", "sessionKey:Work": "w", "cf_clearance:Work": "cw"])
        let keychain = FakeBackend()
        #expect(CredentialMigration.run(from: file, to: keychain, groups: groups) == .migrated)
        #expect(keychain.items == ["sessionKey:Personal": "p", "sessionKey:Work": "w", "cf_clearance:Work": "cw"])
        #expect(file.items.isEmpty)
    }

    @Test func aFileWithOnlyTheLegacyKeyLeavesTheAccountsAlone() {
        let file = FakeBackend(["sessionKey": "legacy"])
        let keychain = FakeBackend(["sessionKey:Work": "w", "cf_clearance:Work": "cw", "cf_clearance": "old"])
        #expect(CredentialMigration.run(from: file, to: keychain, groups: groups) == .migrated)
        #expect(keychain.items == ["sessionKey": "legacy", "sessionKey:Work": "w", "cf_clearance:Work": "cw"])
    }

    @Test func aFailedWriteInAnyGroupKeepsTheWholeFile() {
        let file = FakeBackend(["sessionKey:Personal": "p", "sessionKey:Work": "w"])
        let keychain = FakeBackend()
        keychain.failSet = ["sessionKey:Work"]
        let outcome = CredentialMigration.run(from: file, to: keychain, groups: groups)
        #expect(outcome == .keptFileBecause(.writeFailed(account: "sessionKey:Work")))
        #expect(file.items == ["sessionKey:Personal": "p", "sessionKey:Work": "w"])
    }

    @Test func resolveSeesLabelledSlots() {
        let file = FakeBackend(["sessionKey:Work": "w"])
        let keychain = FakeBackend()
        let r = CredentialMigration.resolve(isSigned: true, file: file, keychain: keychain, groups: groups)
        #expect(r.store == .keychain)
        #expect(r.outcome == .migrated)
        #expect(keychain.items == ["sessionKey:Work": "w"])
    }
}

/// History files in a temporary folder, never the real Application Support one.
struct TempHistory {
    let files: HistoryStore.Files

    init() {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-history-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        files = HistoryStore.Files(dir: dir)
    }

    func cleanUp() { try? FileManager.default.removeItem(at: files.dir) }

    func exists(_ name: String) -> Bool {
        FileManager.default.fileExists(atPath: files.dir.appendingPathComponent(name).path)
    }

    /// The file names in the folder, sorted (exact case, unlike a fileExists check).
    var names: [String] {
        ((try? FileManager.default.contentsOfDirectory(atPath: files.dir.path)) ?? []).sorted()
    }
}

@Suite("History per account")
struct HistoryPerAccountTests {
    @Test func eachAccountHasItsOwnFile() {
        let h = TempHistory()
        defer { h.cleanUp() }
        h.files.append(.fiveHour, utilization: 10, account: "Personal")
        h.files.append(.fiveHour, utilization: 70, account: "Work")
        #expect(h.files.load("Personal")["five_hour"]?.map(\.v) == [10])
        #expect(h.files.load("Work")["five_hour"]?.map(\.v) == [70])
        #expect(h.names == ["history.Personal.json", "history.Work.json"])
        #expect(h.files.url(for: nil).lastPathComponent == "history.json")
    }

    @Test func theLegacyFileBecomesPersonalsOnce() {
        let h = TempHistory()
        defer { h.cleanUp() }
        h.files.append(.sevenDay, utilization: 42, account: nil)
        #expect(h.files.adoptLegacy(into: "Personal"))
        #expect(h.names == ["history.Personal.json"])
        #expect(h.files.load("Personal")["seven_day"]?.map(\.v) == [42])
        #expect(!h.files.adoptLegacy(into: "Personal"))
    }

    @Test func theLegacyFileNeverClobbersPersonals() {
        let h = TempHistory()
        defer { h.cleanUp() }
        h.files.append(.sevenDay, utilization: 1, account: nil)
        h.files.append(.sevenDay, utilization: 2, account: "Personal")
        #expect(!h.files.adoptLegacy(into: "Personal"))
        #expect(h.files.load("Personal")["seven_day"]?.map(\.v) == [2])
        #expect(h.exists("history.json"))
    }

    @Test func registryAdoptsOnlyOncePersonalIsListed() {
        let h = TempHistory()
        defer { h.cleanUp() }
        h.files.append(.sevenDay, utilization: 5, account: nil)
        let defaults = MemoryDefaults()
        let backend = FakeBackend(["sessionKey": "sk-1"])
        let registry = AccountRegistry(backend: backend, stores: [(kind: .file, backend: backend)],
                                       defaults: defaults, history: h.files)
        registry.adoptLegacyHistory()
        #expect(h.names == ["history.json"])          // no accounts yet: the legacy launch uses it
        #expect(registry.promoteLegacy() == .promoted)
        registry.adoptLegacyHistory()
        #expect(h.names == ["history.Personal.json"])
    }

    @Test func renameMovesTheHistoryAndTheMarker() throws {
        let h = TempHistory()
        defer { h.cleanUp() }
        let f = RegistryFixture(keychain: ["sessionKey:Work": "sk-w"], labels: ["Work"])
        let registry = AccountRegistry(backend: f.keychain, stores: f.registry.stores, defaults: f.defaults,
                                       history: h.files)
        h.files.append(.fiveHour, utilization: 33, account: "Work")
        SignInGate.record(fetched: true, needsSignIn: false, account: "Work", in: f.defaults)
        try registry.rename("Work", to: "Office")
        #expect(h.names == ["history.Office.json"])
        #expect(h.files.load("Office")["five_hour"]?.map(\.v) == [33])
        #expect(SignInGate.labels(in: f.defaults) == ["Office"])
        try registry.rename("Office", to: "office")
        #expect(h.names == ["history.office.json"])
    }

    @Test func signOutKeepsTheHistoryAndRemoveDropsIt() {
        let h = TempHistory()
        defer { h.cleanUp() }
        let f = RegistryFixture(keychain: ["sessionKey:Work": "sk-w", "sessionKey:Home": "sk-h"], labels: ["Home", "Work"])
        let registry = AccountRegistry(backend: f.keychain, stores: f.registry.stores, defaults: f.defaults,
                                       history: h.files)
        h.files.append(.fiveHour, utilization: 1, account: "Home")
        h.files.append(.fiveHour, utilization: 2, account: "Work")
        SignInGate.record(fetched: true, needsSignIn: false, account: "Work", in: f.defaults)
        SignInGate.record(fetched: true, needsSignIn: false, account: "Home", in: f.defaults)
        registry.signOut("Work")
        #expect(registry.labels == ["Home", "Work"])
        #expect(h.names == ["history.Home.json", "history.Work.json"])
        #expect(SignInGate.labels(in: f.defaults) == ["Home"])
        registry.remove("Work")
        #expect(registry.labels == ["Home"])
        #expect(h.names == ["history.Home.json"])
    }
}

@Suite("Snapshot account_ref")
struct SnapshotAccountRefTests {
    /// Expected values computed outside the app, as Windows' SnapshotContract.AccountRef does:
    /// `printf %s Personal | shasum -a 256`, first 8 hex digits.
    @Test func matchesTheWindowsHash() {
        #expect(AccountRef.of("Personal") == "845f9286")
        #expect(AccountRef.of("Work") == "104ab921")
        #expect(AccountRef.of(nil) == nil)
        #expect(AccountRef.of("") == nil)
    }

    @Test func signedOutSnapshotCarriesTheRefNeverTheLabel() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-snap-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("snapshot.json")
        SnapshotWriter.writeSignedOut(accountRef: AccountRef.of("Work"), now: Date(timeIntervalSince1970: 0), to: url)
        let text = try String(contentsOf: url, encoding: .utf8)
        #expect(text.contains("\"account_ref\":\"104ab921\""))
        #expect(!text.contains("Work"))
    }

    @Test func switchingIsNeitherAnErrorNorASignIn() {
        let s = UsageViewModel.StatusMessage.switching
        #expect(s.text == "Switching account…")
        #expect(!s.isError)
        #expect(!s.needsSignIn)
    }

    @Test func aSwitchDeletesTheSnapshot() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-snap-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("snapshot.json")
        SnapshotWriter.writeSignedOut(accountRef: nil, to: url)
        #expect(FileManager.default.fileExists(atPath: url.path))
        SnapshotWriter.delete(at: url)
        #expect(!FileManager.default.fileExists(atPath: url.path))
        SnapshotWriter.delete(at: url)      // already gone is fine
    }
}

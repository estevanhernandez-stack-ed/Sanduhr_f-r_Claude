import Foundation

/// One place credentials can live: the 0600 file or the Keychain (KeychainStore.swift).
/// Reads return nil for missing or empty; writes and deletes throw so a migration can tell a
/// failed write from a good one. Errors never carry a credential value.
protocol CredentialBackend: Sendable {
    func get(account: String) -> String?
    func set(_ value: String, account: String) throws
    /// Deleting a missing account is not an error.
    func delete(account: String) throws
    /// Whether the account is there, without reading its value where that could prompt.
    func holds(account: String) -> Bool
}

extension CredentialBackend {
    func exists(account: String) -> Bool { get(account: account) != nil }

    func holds(account: String) -> Bool { exists(account: account) }

    /// True when any of `accounts` holds a value.
    func hasAny(_ accounts: [String]) -> Bool { accounts.contains { exists(account: $0) } }
}

/// Which backend a launch uses.
enum CredentialStoreKind: String, Sendable {
    case keychain, file
}

/// The pure decision: which store this launch uses, and whether to move the file into it first.
struct CredentialStorePlan: Equatable, Sendable {
    var store: CredentialStoreKind
    var migrate: Bool

    /// - Signed with the team's Developer ID: the Keychain. A file with anything in it is moved
    ///   over first, even when the Keychain already holds values: a signed build deletes the file
    ///   once it has moved it, so a file still present was written later (by an ad-hoc build, or
    ///   a move whose delete failed) and is the newer copy.
    /// - Ad-hoc or unsigned: the file, whatever the Keychain holds (an ad-hoc signature would
    ///   prompt for every Keychain read after each rebuild).
    static func decide(isSigned: Bool, fileHas: Bool, keychainHas: Bool) -> CredentialStorePlan {
        _ = keychainHas
        guard isSigned else { return CredentialStorePlan(store: .file, migrate: false) }
        return CredentialStorePlan(store: .keychain, migrate: fileHas)
    }
}

/// Why a migration left the file in place. Carries account names, never values.
enum CredentialMigrationFailure: Equatable, Sendable {
    case writeFailed(account: String)
    case readBackMismatch(account: String)
    case clearFailed(account: String)
    case sourceDeleteFailed(account: String)
}

enum CredentialMigrationOutcome: Equatable, Sendable {
    case migrated
    case nothingToMove
    case keptFileBecause(CredentialMigrationFailure)

    /// The store to use after this outcome: the destination unless the file was kept.
    var store: CredentialStoreKind {
        if case .keptFileBecause = self { return .file }
        return .keychain
    }
}

enum CredentialMigration {
    /// Copies every account `from` holds into `to`, reads each one back and compares, clears
    /// accounts the source lacks from `to` (the source is the newer copy), and only then deletes
    /// the source. Any failure stops before the source is touched, so the file and the app keep
    /// working; a later launch tries again.
    static func run(from source: any CredentialBackend, to destination: any CredentialBackend,
                    accounts: [String]) -> CredentialMigrationOutcome {
        let values: [(String, String)] = accounts.compactMap { a in source.get(account: a).map { (a, $0) } }
        guard !values.isEmpty else { return .nothingToMove }
        for (account, value) in values {
            do { try destination.set(value, account: account) } catch {
                return .keptFileBecause(.writeFailed(account: account))
            }
            guard destination.get(account: account) == value else {
                return .keptFileBecause(.readBackMismatch(account: account))
            }
        }
        let moved = Set(values.map(\.0))
        for account in accounts where !moved.contains(account) {
            do { try destination.delete(account: account) } catch {
                return .keptFileBecause(.clearFailed(account: account))
            }
        }
        for (account, _) in values {
            do { try source.delete(account: account) } catch {
                return .keptFileBecause(.sourceDeleteFailed(account: account))
            }
        }
        return .migrated
    }

    /// The whole launch-time choice: plan, then migrate when the plan says so. Returns the
    /// store to use and, when a migration ran, its outcome.
    static func resolve(isSigned: Bool, file: any CredentialBackend, keychain: any CredentialBackend,
                        accounts: [String]) -> (store: CredentialStoreKind, outcome: CredentialMigrationOutcome?) {
        // Ask the Keychain only on a signed build: an ad-hoc one could prompt just for asking.
        let keychainHas = isSigned && keychain.hasAny(accounts)
        let plan = CredentialStorePlan.decide(isSigned: isSigned, fileHas: file.hasAny(accounts),
                                              keychainHas: keychainHas)
        guard plan.migrate else { return (plan.store, nil) }
        let outcome = run(from: file, to: keychain, accounts: accounts)
        return (outcome.store, outcome)
    }
}

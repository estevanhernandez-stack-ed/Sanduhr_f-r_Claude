import Foundation

/// What signing out did to one account in one store. Carries names and an error description,
/// never a credential value.
struct SignOutOutcome: Equatable, Sendable {
    var store: CredentialStoreKind
    var account: String
    /// Nil when the account is gone from the store (deleted, or never there).
    var error: String?

    var removed: Bool { error == nil }
}

struct SignOutResult: Equatable, Sendable {
    /// One per store and account, in the order they were tried.
    var outcomes: [SignOutOutcome]

    var failures: [SignOutOutcome] { outcomes.filter { !$0.removed } }
    var succeeded: Bool { failures.isEmpty }
}

/// Settings, Credentials, Sign Out: deletes every account from every store, whichever one this
/// launch uses, so nothing is left behind in the other (a file an ad-hoc build wrote, a Keychain
/// item a signed build wrote). A failed delete is recorded and the rest still run.
enum SignOut {
    static func run(backends: [(kind: CredentialStoreKind, backend: any CredentialBackend)],
                    accounts: [String]) -> SignOutResult {
        var outcomes: [SignOutOutcome] = []
        for (kind, backend) in backends {
            for account in accounts {
                var error: String?
                do {
                    try backend.delete(account: account)
                    // A delete that returns but leaves the value behind still failed.
                    if backend.exists(account: account) { error = "still present after delete" }
                } catch let e {
                    error = String(describing: e)
                }
                outcomes.append(SignOutOutcome(store: kind, account: account, error: error))
            }
        }
        return SignOutResult(outcomes: outcomes)
    }
}

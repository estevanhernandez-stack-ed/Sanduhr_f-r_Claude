import Foundation

/// The accounts whose saved key claude.ai refused at their last fetch (2.42.5). Before this,
/// Settings, Accounts read only the active account's status, so another account whose key had
/// expired still showed Signed in in green. Every fetch of any account feeds it: the active
/// account's own, Following's check of an inactive one, and a per-seat meter round. Nothing is
/// asked for it; an account nothing fetches keeps what it last showed.
struct ExpiredAccounts: Equatable {
    private(set) var labels: Set<String> = []

    func contains(_ label: String) -> Bool { labels.contains(label) }

    /// A refused key marks the account; a good answer clears it. A Cloudflare challenge or a
    /// network failure says nothing about the key, so it changes nothing.
    mutating func answered(_ label: String?, _ result: Result<UsageResponse, Error>) {
        guard let label else { return }
        switch result {
        case .success: labels.remove(label)
        case .failure(let e):
            if case .unauthorized? = e as? ClaudeAPI.APIError { labels.insert(label) }
        }
    }

    /// A new key, a sign-out or a removal: what the old key got no longer applies.
    mutating func forget(_ label: String) { labels.remove(label) }

    mutating func rename(_ old: String, to new: String) {
        if labels.remove(old) != nil { labels.insert(new) }
    }
}

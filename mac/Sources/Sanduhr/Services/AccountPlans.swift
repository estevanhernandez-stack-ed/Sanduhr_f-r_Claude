import Foundation

/// Each named account's resolved plan name ("Max ×20", "Pro"), kept in the registry's defaults
/// under `accountPlans` as `{label: name}`. The name only: never the riffs, the tier or the
/// billing type. Settings, Accounts shows it and a seat's snapshot carries it.
///
/// Set from the active client's fetch and from Following's check of an inactive account. A nil
/// plan clears the entry (an account can move to an API org). Nothing is kept for a nil or
/// unnamed account (a launch on the legacy key with no accounts). Follows renames and goes with
/// Sign Out and Remove Account; Reset Sanduhr removes the whole defaults domain.
enum AccountPlans {
    static let key = "accountPlans"

    private static func all(in store: DefaultsStore) -> [String: String] {
        store.object(forKey: key) as? [String: String] ?? [:]
    }

    private static func write(_ all: [String: String], in store: DefaultsStore) {
        store.set(all.isEmpty ? nil : all, forKey: key)
    }

    static func plan(for account: String?, in store: DefaultsStore) -> String? {
        guard let account else { return nil }
        return all(in: store)[account]
    }

    /// Every account's plan name, for the view model.
    static func allPlans(in store: DefaultsStore) -> [String: String] { all(in: store) }

    /// Keeps `name` for `account`; nil or empty clears it. A nil or empty account is ignored.
    static func set(_ name: String?, for account: String?, in store: DefaultsStore) {
        guard let account, !account.isEmpty else { return }
        var everything = all(in: store)
        if let name, !name.isEmpty {
            guard everything[account] != name else { return }
            everything[account] = name
        } else {
            guard everything.removeValue(forKey: account) != nil else { return }
        }
        write(everything, in: store)
    }

    static func rename(_ old: String, to new: String, in store: DefaultsStore) {
        var everything = all(in: store)
        guard let name = everything.removeValue(forKey: old) else { return }
        everything[new] = name
        write(everything, in: store)
    }

    /// Sign Out and Remove Account: the plan is not kept for an account with no sign-in.
    static func forget(_ account: String, in store: DefaultsStore) {
        var everything = all(in: store)
        guard everything.removeValue(forKey: account) != nil else { return }
        write(everything, in: store)
    }

    /// What a row or detail header shows beside the account name: the plan, or nothing.
    static func caption(_ plan: String?) -> String? {
        guard let plan, !plan.isEmpty else { return nil }
        return plan
    }

    /// The row's spoken label: "Work, Max ×20", or just the name when the plan is unknown.
    static func spokenLabel(_ label: String, plan: String?) -> String {
        caption(plan).map { "\(label), \($0)" } ?? label
    }
}

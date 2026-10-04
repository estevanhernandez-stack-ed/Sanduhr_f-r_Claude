import Foundation

/// When the widget shows on its own (Settings, General, Surfaces). Standard defaults, key
/// `widgetVisibility`; absent means `always`, so an updated install keeps today's behavior.
/// A brand-new install starts with `whileDeskOff` (DeskFirstRun). `panelHidden` still means
/// "hidden right now": this setting only decides what happens at launch, when Desk turns on or
/// off, and when sign-in finishes.
enum WidgetVisibility: String, CaseIterable, Identifiable {
    /// Never hidden by Sanduhr: it shows unless you hide it, and stays as you left it.
    case always
    /// Hidden whenever Desk is on, shown when Desk is off.
    case whileDeskOff
    /// Only when asked: Show Widget (any menu, a Desk meter's included), Tools.
    case onRequest

    static let key = "widgetVisibility"

    var id: String { rawValue }

    var label: String {
        switch self {
        case .always: "Always shown"
        case .whileDeskOff: "Hidden while Desk is on"
        case .onRequest: "Only when I open it"
        }
    }

    /// The saved choice, or `always` when none is saved (or it is not one this version knows).
    static func saved(in store: DefaultsStore = UserDefaults.standard) -> WidgetVisibility {
        (store.object(forKey: key) as? String).flatMap(WidgetVisibility.init(rawValue:)) ?? .always
    }
}

/// The moments the choice takes over again. Showing or hiding from a menu in between is a
/// one-off that lasts until the next of these.
enum WidgetVisibilityEvent: Equatable {
    case launch
    case deskChanged
    /// The first successful fetch after a launch without a session key: onboarding is done,
    /// so the widget follows the choice as it would have at launch.
    case signedIn
    /// Sign Out in Settings, Credentials: like a launch without a session key, the widget shows
    /// for sign-in.
    case signedOut
    /// The choice itself was changed in Settings.
    case choiceChanged
}

/// Whether this Mac is still signing in: the widget shows for sign-in until the active
/// account's saved session key has fetched once. A key that only exists (expired, mistyped, or
/// no network yet) does not count, so a relaunch never hides the widget the user needs to fix it.
enum SignInGate {
    /// Standard defaults: the labels of the accounts whose key has fetched. A label is added by
    /// its first successful fetch and dropped by its Sign Out or by claude.ai refusing its key.
    static let key = "signedInAccounts"
    /// The single flag through 2.3.x (#105). When set, it seeds the set with Personal (the
    /// account the legacy key became) and is removed. Absent on an install updated from 2.3.2 or
    /// earlier, so its first launch shows the widget until a fetch succeeds.
    static let legacyKey = "signedInFetchDone"

    /// The label the marker is kept under: the active account, or Personal while a launch runs
    /// on the legacy key with no accounts.
    static func label(_ account: String?) -> String {
        account ?? AccountRegistry.defaultLabel
    }

    static func labels(in store: DefaultsStore) -> [String] {
        migrate(in: store)
        return store.object(forKey: key) as? [String] ?? []
    }

    private static func write(_ labels: [String], in store: DefaultsStore) {
        store.set(labels.isEmpty ? nil : labels, forKey: key)
    }

    /// The one-time seed from the 2.3.x flag.
    static func migrate(in store: DefaultsStore) {
        guard let old = store.object(forKey: legacyKey) else { return }
        if (old as? Bool) == true {
            var set = store.object(forKey: key) as? [String] ?? []
            if !set.contains(AccountRegistry.defaultLabel) { set.append(AccountRegistry.defaultLabel) }
            write(set, in: store)
        }
        store.set(nil, forKey: legacyKey)
    }

    static func fetched(_ account: String?, in store: DefaultsStore) -> Bool {
        labels(in: store).contains(label(account))
    }

    /// True while the widget should show for sign-in: a brand-new install, no session key, or a
    /// key of the active account that has not fetched yet.
    static func awaitingSignIn(fresh: Bool, hasSessionKey: Bool, account: String?, in store: DefaultsStore) -> Bool {
        fresh || !hasSessionKey || !fetched(account, in: store)
    }

    /// After every update from the view model: a successful fetch marks the active account; Sign
    /// Out or an auth error (`needsSignIn`) clears it, so the next launch waits for a good fetch.
    static func record(fetched: Bool, needsSignIn: Bool, account: String?, in store: DefaultsStore) {
        let name = label(account)
        var set = labels(in: store)
        if fetched {
            if !set.contains(name) { set.append(name); write(set, in: store) }
        } else if needsSignIn, set.contains(name) {
            set.removeAll { $0 == name }
            write(set, in: store)
        }
    }

    /// The marker follows a renamed account.
    static func rename(_ old: String, to new: String, in store: DefaultsStore) {
        var set = labels(in: store)
        guard let i = set.firstIndex(of: old) else { return }
        set[i] = new
        write(set, in: store)
    }

    /// Sign Out and Remove Account forget the account's fetch.
    static func forget(_ account: String?, in store: DefaultsStore) {
        var set = labels(in: store)
        let name = label(account)
        guard set.contains(name) else { return }
        set.removeAll { $0 == name }
        write(set, in: store)
    }
}

enum WidgetVisibilityRule {
    /// Whether the widget should show after `event`, or nil to leave it as it is. `always` shows
    /// it when it is chosen, so a widget an earlier choice hid comes back; after that it never
    /// moves it (at launch `panelHidden` decides, as before). Without a session key the other
    /// choices always show it, so sign-in can run.
    static func shouldShow(setting: WidgetVisibility, deskOn: Bool, hasSessionKey: Bool,
                           event: WidgetVisibilityEvent) -> Bool? {
        switch setting {
        case .always: return event == .choiceChanged ? true : nil
        case _ where !hasSessionKey: return true
        case .whileDeskOff: return !deskOn
        case .onRequest: return false
        }
    }

    /// The widget's state after `event`, given whether it shows now (a manual show or hide
    /// included).
    static func resolve(showing: Bool, setting: WidgetVisibility, deskOn: Bool,
                        hasSessionKey: Bool, event: WidgetVisibilityEvent) -> Bool {
        shouldShow(setting: setting, deskOn: deskOn, hasSessionKey: hasSessionKey, event: event) ?? showing
    }
}

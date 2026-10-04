import Foundation

/// One item of a limit's two-finger menu (item 39), on a Desk meter row or a widget tier card.
enum LimitMenuEntry: Equatable {
    /// The Accounts submenu, with two or more accounts. Its titles are labels: never in state.yaml.
    case accounts(AccountsMenu)
    /// Hide this limit (MeterVisibility, the Settings "Show this limit" switch turned off).
    case hide(Tier)
    /// Turn this limit's "Warn when nearly full" off (`on` true) or back on (`on` false).
    case warnings(Tier, on: Bool)
    /// Settings, Desk, Meters.
    case meterSettings

    var title: String {
        switch self {
        case .accounts: AccountsMenu.title
        case .hide(let tier): LimitMenu.hideTitle(tier)
        case .warnings(_, let on): on ? LimitMenu.stopWarnings : LimitMenu.warnAgain
        case .meterSettings: LimitMenu.meterSettings
        }
    }
}

/// What a limit's two-finger menu holds, described without AppKit so it tests on its own. Desk
/// (AppDelegate.addLimitMenuItems) and the widget's cards (LimitContextMenu) both render it, then
/// the shared menu (SanduhrMenu) under a separator. Hiding and silencing write the same desk-suite
/// keys as Settings, Desk, Meters, so Settings and the menus always agree.
enum LimitMenu {
    static let stopWarnings = "Stop warnings for this limit"
    static let warnAgain = "Warn again for this limit"
    static let meterSettings = "Meter Settings…"

    static func hideTitle(_ tier: Tier) -> String { "Hide \(tier.label)" }

    /// The items in runs between separators: the Accounts submenu (two or more accounts), then the
    /// limit's own items, then Meter Settings…. `tier` is the row or card the menu opened on; nil
    /// (a click beside the rows) leaves the limit's own items out. Hide shows only for a limit that
    /// can be hidden and still shows; `warningsOn` is the limit's "Warn when nearly full".
    static func groups(tier: Tier?, accounts: AccountsMenu?, hidden: Set<Tier>,
                       warningsOn: Bool) -> [[LimitMenuEntry]] {
        var groups: [[LimitMenuEntry]] = []
        if let accounts { groups.append([.accounts(accounts)]) }
        if let tier {
            var own: [LimitMenuEntry] = []
            if MeterVisibility.canHide(tier) && !hidden.contains(tier) { own.append(.hide(tier)) }
            own.append(.warnings(tier, on: warningsOn))
            groups.append(own)
        }
        groups.append([.meterSettings])
        return groups
    }

    /// The same, with the hidden limits and the warning switch read from `store` (the desk suite).
    static func groups(tier: Tier?, accounts: AccountsMenu?, store: DefaultsStore) -> [[LimitMenuEntry]] {
        groups(tier: tier, accounts: accounts, hidden: MeterVisibility.hidden(in: store),
               warningsOn: tier.map { MeterWarningSettings.saved($0, in: store).enabled } ?? false)
    }

    /// Does what Hide and the warnings item say, in `store`. The Accounts submenu and Meter
    /// Settings… are the caller's (a switch, a window), so they change nothing here.
    static func apply(_ entry: LimitMenuEntry, to store: DefaultsStore) {
        switch entry {
        case .hide(let tier) where MeterVisibility.canHide(tier):
            store.set(false, forKey: MeterVisibility.showKey(tier))
        case .warnings(let tier, let on):
            store.set(!on, forKey: MeterWarningSettings.onKey(tier))
        default:
            break
        }
    }

    /// The limits whose "Warn when nearly full" is off, in display order (the debug state's
    /// `silenced_limits`). The session is off until switched on.
    static func silenced(in store: DefaultsStore) -> [Tier] {
        Tier.allCases.filter { !MeterWarningSettings.saved($0, in: store).enabled }
    }
}

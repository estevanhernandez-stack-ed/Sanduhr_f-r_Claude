import Foundation

/// One item of a limit's two-finger menu (item 39), on a Desk meter row or a widget tier card.
enum LimitMenuEntry: Equatable {
    /// Show Widget, or Hide Widget when it shows (`visible`): on a Desk meter's menu only, at the
    /// top, since a plain click on the meters does nothing (item 41).
    case widget(visible: Bool)
    /// The Accounts submenu, with two or more accounts. Its titles are labels: never in state.yaml.
    case accounts(AccountsMenu)
    /// Hide this limit (MeterVisibility, the Settings "Show this limit" switch turned off). Only
    /// for a limit believed temporary (LimitLifetime).
    case hide(Tier)
    /// The Hidden Limits submenu, when something is hidden: one `show` item per hidden limit, in
    /// display order. Its titles are labels: never in state.yaml.
    case hiddenLimits([Tier])
    /// Show this hidden limit again (an item of the Hidden Limits submenu).
    case show(Tier)
    /// Turn this limit's "Warn when nearly full" off (`on` true) or back on (`on` false).
    case warnings(Tier, on: Bool)
    /// Settings, Alerts, Each limit.
    case meterSettings

    var title: String {
        switch self {
        case .widget(let visible): visible ? "Hide Widget" : "Show Widget"
        case .accounts: AccountsMenu.title
        case .hide(let tier): LimitMenu.hideTitle(tier)
        case .hiddenLimits: LimitMenu.hiddenLimits
        case .show(let tier): tier.label
        case .warnings(_, let on): on ? LimitMenu.stopWarnings : LimitMenu.warnAgain
        case .meterSettings: LimitMenu.meterSettings
        }
    }
}

/// What a limit's two-finger menu holds, described without AppKit so it tests on its own. Desk
/// (AppDelegate.addLimitMenuItems) and the widget's cards (LimitContextMenu) both render it, then
/// the shared menu (SanduhrMenu) under a separator. Hiding and silencing write the same desk-suite
/// keys as Settings, Alerts, Each limit, so Settings and the menus always agree.
enum LimitMenu {
    static let stopWarnings = "Stop warnings for this limit"
    static let warnAgain = "Warn again for this limit"
    /// "Alerts Settings…": the page that holds each limit's warning and Show this limit (Settings
    /// v2, slice 2, was Meters), by its sidebar title, opened at Each limit.
    static let meterSection = SettingsSection.alerts
    static let meterAnchor = SettingsAnchor.eachLimit
    static let meterSettings = meterSection.linkTitle
    static let hiddenLimits = "Hidden Limits"

    static func hideTitle(_ tier: Tier) -> String { "Hide \(tier.label)" }

    /// The items in runs between separators: Show or Hide Widget (Desk only: `widgetVisible` is
    /// nil on a widget card), the Accounts submenu (two or more accounts), then the limit's own
    /// items, then the Hidden Limits submenu (when something is hidden) and Alerts Settings….
    /// `tier` is the row or card the menu opened on; nil (a click beside the rows) leaves the
    /// limit's own items out. Hide shows only for a limit in `temporary` that still shows;
    /// `warningsOn` is the limit's "Warn when nearly full".
    static func groups(tier: Tier?, accounts: AccountsMenu?, hidden: Set<Tier>, temporary: Set<Tier>,
                       warningsOn: Bool, widgetVisible: Bool? = nil) -> [[LimitMenuEntry]] {
        var groups: [[LimitMenuEntry]] = []
        if let widgetVisible { groups.append([.widget(visible: widgetVisible)]) }
        if let accounts { groups.append([.accounts(accounts)]) }
        if let tier {
            var own: [LimitMenuEntry] = []
            if temporary.contains(tier) && !hidden.contains(tier) { own.append(.hide(tier)) }
            own.append(.warnings(tier, on: warningsOn))
            groups.append(own)
        }
        var last: [LimitMenuEntry] = []
        let hiddenInOrder = Tier.allCases.filter(hidden.contains)
        if !hiddenInOrder.isEmpty { last.append(.hiddenLimits(hiddenInOrder)) }
        last.append(.meterSettings)
        groups.append(last)
        return groups
    }

    /// The same, with the hidden limits, the temporary ones (for these numbers) and the warning
    /// switch read from `store` (the desk suite).
    static func groups(tier: Tier?, accounts: AccountsMenu?, store: DefaultsStore,
                       usage: UsageResponse?, now: Date, widgetVisible: Bool? = nil) -> [[LimitMenuEntry]] {
        groups(tier: tier, accounts: accounts, hidden: MeterVisibility.hidden(in: store),
               temporary: MeterVisibility.temporary(usage, now: now, store: store),
               warningsOn: tier.map { MeterWarningSettings.saved($0, in: store).enabled } ?? false,
               widgetVisible: widgetVisible)
    }

    /// Does what Hide, a Hidden Limits item and the warnings item say, in `store`. Hide records
    /// what the limit reads in `usage`, and does nothing for a limit that is not temporary. The
    /// widget item, the Accounts submenu and Alerts Settings… are the caller's (a window, a
    /// switch), so they change nothing here.
    static func apply(_ entry: LimitMenuEntry, to store: DefaultsStore,
                      usage: UsageResponse? = nil, now: Date = Date()) {
        switch entry {
        case .hide(let tier):
            MeterVisibility.hide(tier, usage: usage, now: now, store: store)
        case .show(let tier):
            MeterVisibility.show(tier, store: store)
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

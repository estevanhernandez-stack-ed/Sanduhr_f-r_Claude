import Foundation

/// What a menu item does. The menu bar item's menu, the widget's two-finger menu and Desk's
/// clock menu all hand these to AppDelegate.perform, so one choice does the same thing from
/// any of them.
enum MenuCommand: Int, CaseIterable {
    case showHide, deepWork, pacing, snake, cameraLight, usage, refresh, settings, arrangeDesk, checkForUpdates, whatsNew, tour, quit
}

/// One item: its title, its Command-key equivalent ("" for none), whether it shows a checkmark,
/// and whether it can be chosen, with the reason when not (`note`, shown under or beside it).
struct MenuEntry: Equatable {
    let command: MenuCommand
    let title: String
    var key: String = ""
    var checked: Bool = false
    var enabled: Bool = true
    var note: String?
}

/// A run of items between separators, with an optional section header.
struct MenuGroup: Equatable {
    var header: String?
    let entries: [MenuEntry]
}

/// The one menu Sanduhr shows everywhere, described without AppKit so it tests on its own.
/// AppKit menus (AppDelegate.addMenuItems) and the widget's SwiftUI context menu (RootView)
/// both render it, separators between the groups.
enum SanduhrMenu {
    static let settingsTitle = "Settings…"
    static let allSettingsTitle = "All Settings…"

    /// `widgetVisible` picks Show or Hide; `deepWork`, `pacing` and `snake` are the tools'
    /// checkmarks (Deep Work or Cooldown Snake open on the widget, the pacing calculators pinned);
    /// `cameraLight` is the camera light switched on by hand. Claude Usage… opens Settings at the
    /// Claude Usage page (item 48). What's New… (item 57) reopens the release highlights, Take the Tour… (item 61) the welcome tour.
    /// Arrange Desk… (item 60) starts Arrange mode on the desktop; with `deskOn` false it is off,
    /// and says why. `allSettings` names Settings… "All Settings…", for a menu that also has a
    /// page's own Settings item (the Desk clock's and message's menus, the meter menus).
    static func groups(widgetVisible: Bool, deepWork: Bool, pacing: Bool, snake: Bool,
                       cameraLight: Bool = false, deskOn: Bool = true,
                       allSettings: Bool = false) -> [MenuGroup] {
        [
            MenuGroup(entries: [
                MenuEntry(command: .showHide, title: widgetVisible ? "Hide Widget" : "Show Widget"),
            ]),
            MenuGroup(header: "Tools", entries: [
                MenuEntry(command: .deepWork, title: "Deep Work", key: "p", checked: deepWork),
                MenuEntry(command: .pacing, title: "Pacing Calculators", checked: pacing),
                MenuEntry(command: .snake, title: "Cooldown Snake", checked: snake),
                MenuEntry(command: .cameraLight, title: "Camera Fill Light", checked: cameraLight),
                MenuEntry(command: .usage, title: "Claude Usage…"),
            ]),
            MenuGroup(entries: [
                MenuEntry(command: .refresh, title: "Refresh", key: "r"),
                MenuEntry(command: .settings, title: allSettings ? allSettingsTitle : settingsTitle, key: ","),
                MenuEntry(command: .arrangeDesk, title: DeskArrangeCopy.menuItem, enabled: deskOn,
                          note: deskOn ? nil : DeskArrangeCopy.settingsDeskOff),
                MenuEntry(command: .checkForUpdates, title: SettingsNames.checkForUpdates),
                MenuEntry(command: .whatsNew, title: "What's New…"),
                MenuEntry(command: .tour, title: "Take the Tour…"),
            ]),
            MenuGroup(entries: [
                MenuEntry(command: .quit, title: "Quit Sanduhr für Claude", key: "q"),
            ]),
        ]
    }
}

extension SanduhrMenu {
    /// `groups` without `command`'s item, dropping a group it leaves empty.
    static func without(_ command: MenuCommand, in groups: [MenuGroup]) -> [MenuGroup] {
        groups.compactMap { group in
            let entries = group.entries.filter { $0.command != command }
            return entries.isEmpty ? nil : MenuGroup(header: group.header, entries: entries)
        }
    }
}

/// One account in the Accounts submenu: the active one checked, one that following saw in use
/// marked "· in use".
struct AccountMenuItem: Equatable {
    let label: String
    var checked = false
    var inUse = false

    var title: String { inUse ? "\(label) · in use" : label }
}

/// The Accounts submenu (item 36): each account, then Manage Accounts…, which opens Settings,
/// Accounts. Kept apart from `MenuGroup` because its titles are labels, which never go into
/// state.yaml (the debug state lists the groups' titles).
struct AccountsMenu: Equatable {
    static let title = "Accounts"
    static let manage = "Manage Accounts…"
    let items: [AccountMenuItem]
}

extension SanduhrMenu {
    /// The Accounts submenu, nil with fewer than two accounts (one account needs no choosing).
    /// `inUse` marks the inactive accounts following saw in use; the active one is never marked.
    static func accounts(_ labels: [String], active: String?, inUse: Set<String> = []) -> AccountsMenu? {
        guard labels.count > 1 else { return nil }
        return AccountsMenu(items: labels.map { label in
            AccountMenuItem(label: label, checked: label == active,
                            inUse: label != active && inUse.contains(label))
        })
    }
}

/// One choice in the Menu Bar Shows submenu.
struct MenuBarModeItem: Equatable {
    let mode: MenuBarMode
    var checked = false

    /// The same words as Settings, General, Menu bar, Menu Bar Shows.
    var title: String { mode.label }
}

/// The Menu Bar Shows submenu (item 40): the four Menu bar choices, the current one checked.
/// Choosing one writes the same `menuBarMode` default as Settings, so Settings follows. Every
/// Sanduhr menu carries it since Settings v2's slice 1, not only the menu bar item's own.
struct MenuBarModeMenu: Equatable {
    static let title = SettingsNames.menuBarShows
    let items: [MenuBarModeItem]
}

extension SanduhrMenu {
    /// The Menu Bar Shows submenu with `current` checked, in Settings' order.
    static func menuBarModes(current: MenuBarMode) -> MenuBarModeMenu {
        MenuBarModeMenu(items: MenuBarMode.allCases.map { MenuBarModeItem(mode: $0, checked: $0 == current) })
    }

    /// The submenus every Sanduhr menu shows after Show or Hide Widget (at the top of the shared
    /// items where a menu leaves Show or Hide out): Accounts with two or more accounts, then Menu
    /// Bar Shows. AppDelegate.addMenuItems and the widget's SanduhrMenuItems both follow it.
    static func submenus(accounts: AccountsMenu?) -> [String] {
        (accounts == nil ? [] : [AccountsMenu.title]) + [MenuBarModeMenu.title]
    }
}

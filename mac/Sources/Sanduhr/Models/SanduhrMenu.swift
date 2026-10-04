import Foundation

/// What a menu item does. The menu bar item's menu, the widget's two-finger menu and Desk's
/// clock menu all hand these to AppDelegate.perform, so one choice does the same thing from
/// any of them.
enum MenuCommand: Int, CaseIterable {
    case showHide, deepWork, pacing, snake, cameraLight, refresh, settings, checkForUpdates, quit
}

/// One item: its title, its Command-key equivalent ("" for none) and whether it shows a checkmark.
struct MenuEntry: Equatable {
    let command: MenuCommand
    let title: String
    var key: String = ""
    var checked: Bool = false
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
    /// `widgetVisible` picks Show or Hide; `deepWork`, `pacing` and `snake` are the tools'
    /// checkmarks (Deep Work or Cooldown Snake open on the widget, the pacing calculators pinned);
    /// `cameraLight` is the camera light switched on by hand.
    static func groups(widgetVisible: Bool, deepWork: Bool, pacing: Bool, snake: Bool,
                       cameraLight: Bool = false) -> [MenuGroup] {
        [
            MenuGroup(entries: [
                MenuEntry(command: .showHide, title: widgetVisible ? "Hide Widget" : "Show Widget"),
            ]),
            MenuGroup(header: "Tools", entries: [
                MenuEntry(command: .deepWork, title: "Deep Work", key: "p", checked: deepWork),
                MenuEntry(command: .pacing, title: "Pacing Calculators", checked: pacing),
                MenuEntry(command: .snake, title: "Cooldown Snake", checked: snake),
                MenuEntry(command: .cameraLight, title: "Camera Light", checked: cameraLight),
            ]),
            MenuGroup(entries: [
                MenuEntry(command: .refresh, title: "Refresh", key: "r"),
                MenuEntry(command: .settings, title: "Settings…", key: ","),
                MenuEntry(command: .checkForUpdates, title: "Check for Updates…"),
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

    /// The same words as Settings, General, Menu bar.
    var title: String { mode.label }
}

/// The menu bar item's own Menu Bar Shows submenu (item 40): the four Menu bar choices, the
/// current one checked. Choosing one writes the same `menuBarMode` default as Settings, so
/// Settings follows.
struct MenuBarModeMenu: Equatable {
    static let title = "Menu Bar Shows"
    let items: [MenuBarModeItem]
}

extension SanduhrMenu {
    /// The Menu Bar Shows submenu with `current` checked, in Settings' order.
    static func menuBarModes(current: MenuBarMode) -> MenuBarModeMenu {
        MenuBarModeMenu(items: MenuBarMode.allCases.map { MenuBarModeItem(mode: $0, checked: $0 == current) })
    }
}

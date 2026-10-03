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
                MenuEntry(command: .showHide, title: widgetVisible ? "Hide Sanduhr" : "Show Sanduhr"),
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
                MenuEntry(command: .quit, title: "Quit Sanduhr", key: "q"),
            ]),
        ]
    }
}

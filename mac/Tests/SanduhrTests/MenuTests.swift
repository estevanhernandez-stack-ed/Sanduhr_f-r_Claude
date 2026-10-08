import Testing
@testable import Sanduhr

/// The one menu the menu bar item, the widget and Desk's clock menu all render, and the
/// Settings window's sidebar.

@Suite("Sanduhr menu")
struct SanduhrMenuTests {
    func menu(visible: Bool = true, deepWork: Bool = false, pacing: Bool = false, snake: Bool = false,
              camera: Bool = false) -> [MenuGroup] {
        SanduhrMenu.groups(widgetVisible: visible, deepWork: deepWork, pacing: pacing, snake: snake,
                           cameraLight: camera)
    }

    /// Titles in order, "-" for each separator the renderers put between groups.
    func flat(_ groups: [MenuGroup]) -> [String] {
        groups.enumerated().flatMap { i, g in (i > 0 ? ["-"] : []) + g.entries.map(\.title) }
    }

    @Test func itemsInOrder() {
        #expect(flat(menu()) == [
            "Hide Widget", "-",
            "Deep Work", "Pacing Calculators", "Cooldown Snake", "Camera Light", "Claude Usage…", "-",
            "Refresh", "Settings…", "Arrange Desk…", "Check for Updates…", "What's New…", "Take the Tour…", "-",
            "Quit Sanduhr für Claude",
        ])
        #expect(menu().map(\.header) == [nil, "Tools", nil, nil])
    }

    @Test func showOrHideFollowsTheWidget() {
        #expect(menu(visible: true)[0].entries[0].title == "Hide Widget")
        #expect(menu(visible: false)[0].entries[0].title == "Show Widget")
        #expect(menu(visible: false)[0].entries[0].command == .showHide)
    }

    @Test func toolCheckmarks() {
        func checks(_ g: [MenuGroup]) -> [MenuCommand: Bool] {
            Dictionary(uniqueKeysWithValues: g[1].entries.map { ($0.command, $0.checked) })
        }
        #expect(checks(menu()) == [.deepWork: false, .pacing: false, .snake: false, .cameraLight: false, .usage: false])
        #expect(checks(menu(deepWork: true)) == [.deepWork: true, .pacing: false, .snake: false, .cameraLight: false, .usage: false])
        #expect(checks(menu(pacing: true, snake: true)) == [.deepWork: false, .pacing: true, .snake: true, .cameraLight: false, .usage: false])
        #expect(checks(menu(camera: true)) == [.deepWork: false, .pacing: false, .snake: false, .cameraLight: true, .usage: false])
        // Only tools carry checkmarks.
        let rest = menu(deepWork: true, pacing: true, snake: true, camera: true).enumerated()
            .filter { $0.offset != 1 }.flatMap(\.element.entries)
        #expect(rest.allSatisfy { !$0.checked })
    }

    @Test func keyEquivalents() {
        let keys = Dictionary(uniqueKeysWithValues: menu().flatMap(\.entries).map { ($0.command, $0.key) })
        #expect(keys == [.showHide: "", .deepWork: "p", .pacing: "", .snake: "", .cameraLight: "", .usage: "",
                         .refresh: "r", .settings: ",", .arrangeDesk: "", .checkForUpdates: "", .whatsNew: "", .tour: "",
                         .quit: "q"])
    }

    /// Item 60: Arrange Desk… in the menu bar item's, the widget's and every Desk menu that carries
    /// the shared items; off with its reason while Desk is off.
    @Test func arrangeDeskIsEverywhereAndSaysWhyWhenOff() throws {
        let on = try #require(menu().flatMap(\.entries).first { $0.command == .arrangeDesk })
        #expect(on.title == "Arrange Desk…")
        #expect(on.enabled)
        #expect(on.note == nil)
        let offMenu = SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: false, snake: false, deskOn: false)
        let off = try #require(offMenu.flatMap(\.entries).first { $0.command == .arrangeDesk })
        #expect(!off.enabled)
        #expect(off.note == "Turn on Desk to arrange it on the desktop.")
        // Every other item stays on, and the Desk menus' shared part (less Show or Hide) keeps it.
        #expect(offMenu.flatMap(\.entries).filter { !$0.enabled }.map(\.command) == [.arrangeDesk])
        #expect(SanduhrMenu.without(.showHide, in: menu()).flatMap(\.entries).contains { $0.command == .arrangeDesk })
    }

    /// The Desk pieces with a two-finger menu: each one's menu has Arrange Desk…, either from the
    /// shared items (meters, meetings, the account name) or at its end (now playing, watchers,
    /// the indicators).
    @Test func everyDeskPieceWithAMenuOffersArrange() {
        for kind in [DeskElement.Kind.meterRow, .meters, .meetingRow, .note, .account, .nowPlaying,
                     .nowPlayingNext, .watcher, .avIndicators] {
            #expect(DeskHitTest.hasMenu(DeskElement(kind: kind, frame: .zero)), "\(kind)")
        }
        #expect(DeskHitTest.hasSharedMenu(DeskElement(kind: .meetingRow, frame: .zero)))
        #expect(DeskHitTest.hasSharedMenu(DeskElement(kind: .account, frame: .zero)))
        #expect(!DeskHitTest.hasSharedMenu(DeskElement(kind: .meters, frame: .zero)))
        // The meetings block as a whole takes no click, so it opens nothing.
        #expect(!DeskHitTest.hasMenu(DeskElement(kind: .meetings, frame: .zero)))
        #expect(!DeskHitTest.hasMenu(nil))
    }

    @Test func everyCommandOnce() {
        let commands = menu().flatMap(\.entries).map(\.command)
        #expect(commands == MenuCommand.allCases)
    }
}

@Suite("Accounts submenu")
struct AccountsMenuTests {
    @Test func onlyWithTwoOrMoreAccounts() {
        #expect(SanduhrMenu.accounts([], active: nil) == nil)
        #expect(SanduhrMenu.accounts(["Personal"], active: "Personal") == nil)
        #expect(SanduhrMenu.accounts(["Personal", "Work"], active: "Personal") != nil)
    }

    @Test func eachAccountInOrderWithTheActiveOneChecked() throws {
        let menu = try #require(SanduhrMenu.accounts(["Personal", "Work", "Team 2"], active: "Work"))
        #expect(menu.items.map(\.title) == ["Personal", "Work", "Team 2"])
        #expect(menu.items.map(\.checked) == [false, true, false])
        #expect(AccountsMenu.title == "Accounts")
        #expect(AccountsMenu.manage == "Manage Accounts…")
    }

    @Test func inUseMarksOnlyAnInactiveAccount() throws {
        let menu = try #require(SanduhrMenu.accounts(["Personal", "Work"], active: "Personal",
                                                     inUse: ["Personal", "Work"]))
        #expect(menu.items.map(\.title) == ["Personal", "Work · in use"])
        #expect(menu.items.map(\.label) == ["Personal", "Work"])
    }

    @Test func labelsStayOutOfTheSharedGroups() {
        // state.yaml lists the shared groups' titles; the submenu's labels never join them.
        let titles = SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: false, snake: false)
            .flatMap(\.entries).map(\.title)
        #expect(!titles.contains("Accounts"))
    }
}

@Suite("Settings sidebar")
struct SettingsSidebarTests {
    @Test func everySectionOnceInOrder() {
        let listed = SettingsSection.groups.flatMap(\.sections)
        #expect(listed == SettingsSection.allCases)
        #expect(SettingsSection.groups.map(\.header) == [nil, "Desk", "Widget", "Sanduhr"])
        #expect(SettingsSection.groups.last?.sections == [.updates, .about])
        #expect(SettingsSection.allCases.suffix(2).map(\.title) == ["Updates", "About"])
    }

    @Test func accountsKeepsTheCredentialsLink() {
        // Item 36: Accounts replaced Credentials; the raw value stays for links and smoke.
        #expect(SettingsSection(rawValue: "credentials") == .credentials)
        #expect(SettingsSection.credentials.title == "Accounts")
        #expect(SettingsSection.groups.first?.sections == [.general, .alerts, .credentials, .usage, .integrations, .mods])
        // Item 49: Integrations follows Claude Usage. Item 64: Mods follows Integrations.
        #expect(SettingsSection(rawValue: "integrations")?.title == "Integrations")
        #expect(SettingsSection(rawValue: "mods")?.title == "Mods")
    }

    @Test func claudeUsageSitsUnderAccounts() {
        // Item 48: the page is per account; its setup is in Accounts, Data, one row up.
        #expect(SettingsSection(rawValue: "usage") == .usage)
        #expect(SettingsSection.usage.title == "Claude Usage")
        #expect(SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: false, snake: false)[1]
            .entries.last == MenuEntry(command: .usage, title: "Claude Usage…"))
    }
}

@Suite("Menu Bar Shows submenu")
struct MenuBarModeMenuTests {
    @Test func fourChoicesInSettingsOrderWithTheCurrentChecked() {
        for current in MenuBarMode.allCases {
            let menu = SanduhrMenu.menuBarModes(current: current)
            #expect(menu.items.map(\.mode) == MenuBarMode.allCases)
            #expect(menu.items.filter(\.checked).map(\.mode) == [current])
        }
        #expect(MenuBarModeMenu.title == "Menu Bar Shows")
    }

    @Test func titlesMatchSettings() {
        #expect(SanduhrMenu.menuBarModes(current: .higher).items.map(\.title)
                == ["Session", "Weekly", "Whichever is higher", "Rotate (session and weekly)"])
    }

    @Test func choosingWritesTheSettingsKey() {
        let d = MemoryDefaults()
        MenuBarMode.save(.rotate, in: d)
        #expect(d.object(forKey: "menuBarMode") as? String == "rotate")
        #expect(MenuBarMode.saved(in: d) == .rotate)
        MenuBarMode.save(.session, in: d)
        #expect(SanduhrMenu.menuBarModes(current: .saved(in: d)).items.first { $0.checked }?.mode == .session)
    }

    @Test func staysOutOfTheSharedGroups() {
        // The widget's menu and Desk's clock menu render the shared groups; the submenu is the
        // menu bar item's own.
        let titles = SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: false, snake: false)
            .flatMap(\.entries).map(\.title)
        #expect(!titles.contains(MenuBarModeMenu.title))
    }
}

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
            "Deep Work", "Pacing Calculators", "Cooldown Snake", "Camera Light", "-",
            "Refresh", "Settings…", "Check for Updates…", "-",
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
        #expect(checks(menu()) == [.deepWork: false, .pacing: false, .snake: false, .cameraLight: false])
        #expect(checks(menu(deepWork: true)) == [.deepWork: true, .pacing: false, .snake: false, .cameraLight: false])
        #expect(checks(menu(pacing: true, snake: true)) == [.deepWork: false, .pacing: true, .snake: true, .cameraLight: false])
        #expect(checks(menu(camera: true)) == [.deepWork: false, .pacing: false, .snake: false, .cameraLight: true])
        // Only tools carry checkmarks.
        let rest = menu(deepWork: true, pacing: true, snake: true, camera: true).enumerated()
            .filter { $0.offset != 1 }.flatMap(\.element.entries)
        #expect(rest.allSatisfy { !$0.checked })
    }

    @Test func keyEquivalents() {
        let keys = Dictionary(uniqueKeysWithValues: menu().flatMap(\.entries).map { ($0.command, $0.key) })
        #expect(keys == [.showHide: "", .deepWork: "p", .pacing: "", .snake: "", .cameraLight: "",
                         .refresh: "r", .settings: ",", .checkForUpdates: "", .quit: "q"])
    }

    @Test func everyCommandOnce() {
        let commands = menu().flatMap(\.entries).map(\.command)
        #expect(commands == MenuCommand.allCases)
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
}

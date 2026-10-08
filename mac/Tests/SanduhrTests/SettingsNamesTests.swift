import Foundation
import Testing
@testable import Sanduhr

/// Settings v2, slice 1 (item 72): one name per control, and every button that opens Settings
/// names the page it opens.

@Suite("Settings names")
struct SettingsNamesTests {
    @Test func everyPageHasALinkTitleThatFindsItAgain() {
        for s in SettingsSection.allCases {
            #expect(s.linkTitle == "\(s.title) Settings…")
            #expect(SettingsSection.linked(s.linkTitle) == s)
        }
        #expect(SettingsSection.linked("Settings…") == nil)
        #expect(SettingsSection.linked("All Settings…") == nil)
        #expect(SettingsSection.linked("Desk Layout Settings…") == nil)
    }

    @Test func linksThatNamedPagesThatDontExistNowNameTheirs() {
        // W1, W4: "Desk Layout Settings…" opened a page titled Layout, "Meter Settings…" Meters,
        // "Watcher Settings…" Integrations and "Data Settings…" Accounts.
        #expect(SettingsSection.deskLayout.linkTitle == "Layout Settings…")
        #expect(LimitMenu.meterSettings == "Meters Settings…")
        #expect(LimitMenuEntry.meterSettings.title == SettingsSection.deskMeters.linkTitle)
        #expect(WatcherMenu.settingsSection.linkTitle == "Integrations Settings…")
        #expect(SettingsSection.credentials.linkTitle == "Accounts Settings…")
    }

    @Test func menuItemsThatOpenSettingsNameThePageTheyOpen() {
        #expect(AVIndicatorMenu.settingsTitle == AVIndicatorMenu.settingsSection.linkTitle)
        #expect(AVIndicatorMenu.settingsSection == .notch)
        #expect(AVIndicatorMenu.items(AVIndicators(mic: true)).last?.title == "Notch Settings…")
        let clock = DeskPieceClicks.menuItem(DeskElement.Kind.clock)
        #expect(clock?.title == clock?.section.linkTitle)
        #expect(clock?.section == .deskLook)
    }

    @Test func theIslandOpensNotch() {
        // F8: the island's click said it opened "these settings" and opened wherever Settings was.
        #expect(NotchIsland.clickOpens == .notch)
        #expect(NotchIsland.clickOpens.linkTitle == "Notch Settings…")
    }

    @Test func tableNamesAreUniqueAndOnRealPages() {
        let names = SettingsNames.table.map(\.name)
        #expect(Set(names).count == names.count)
        #expect(SettingsNames.table.first { $0.name == "Menu Bar Shows" }?.page == .general)
        #expect(SettingsNames.table.first { $0.name == "Claude Code glow hook" }?.page == .integrations)
        // A retired name is never a current one.
        for old in SettingsNames.retired.map(\.old) { #expect(!names.contains(old), "\(old)") }
        // Each retired name's replacement is a current name or a link title.
        for r in SettingsNames.retired {
            let ok = names.contains(r.now) || SettingsSection.linked(r.now) != nil || r.now.hasPrefix("Notch, ")
            #expect(ok, "\(r.old) → \(r.now)")
        }
    }

    @Test func oneNameEverywhere() {
        #expect(MenuBarModeMenu.title == SettingsNames.menuBarShows)
        #expect(IntegrationKind.hooks.title == SettingsNames.claudeCodeGlowHook)
        #expect(DeskLayout.widgets.first { $0.key == "claude" }?.name == "Claude meters (line)")
        #expect(DeskLayout.widgets.first { $0.key == "meters" }?.name == "Claude meters (bars)")
        let check = SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: false, snake: false)
            .flatMap(\.entries).first { $0.command == .checkForUpdates }
        #expect(check?.title == SettingsNames.checkForUpdates)
    }

    @Test func retiredNamesAndUnknownLinksAreFound() {
        #expect(SettingsNames.retiredNames(in: "Settings, General, Percent beside the hourglass") == ["Percent beside the hourglass"])
        #expect(SettingsNames.retiredNames(in: "Choose what the menu bar shows.").isEmpty)
        #expect(SettingsNames.unknownLinks(in: "Click Desk Layout Settings… or Notch Settings…") == ["Click Desk Layout Settings…"])
        #expect(SettingsNames.unknownLinks(in: "Notch Settings…, Desk Look Settings… and All Settings…").isEmpty)
        #expect(SettingsNames.unknownLinks(in: "Pacing & Focus Settings…").isEmpty)
        #expect(SettingsNames.unknownLinks(in: "arg=<Page> Settings…").isEmpty)
    }
}

@Suite("Settings names in the tour, What's New and the menus")
struct SettingsNamesUseTests {
    /// Every word a person reads in a menu: the shared groups in each form, the submenus and the
    /// Desk and limit menus' own items.
    static var menuTitles: [String] {
        var out: [String] = []
        for visible in [true, false] {
            for all in [true, false] {
                out += SanduhrMenu.groups(widgetVisible: visible, deepWork: false, pacing: false, snake: false,
                                          deskOn: visible, allSettings: all)
                    .flatMap(\.entries).flatMap { [$0.title, $0.note ?? ""] }
            }
        }
        out += SanduhrMenu.submenus(accounts: SanduhrMenu.accounts(["A", "B"], active: "A"))
        out += SanduhrMenu.menuBarModes(current: .higher).items.map(\.title)
        out += [AccountsMenu.manage, LimitMenu.meterSettings, LimitMenu.stopWarnings, LimitMenu.warnAgain,
                LimitMenu.hiddenLimits, AVIndicatorMenu.settingsTitle,
                WatcherMenu.settingsSection.linkTitle, SettingsSection.nowPlaying.linkTitle]
        out += [DeskElement.Kind.clock, .message].compactMap { DeskPieceClicks.menuItem($0)?.title }
        return out
    }

    @Test func menusUseNoRetiredName() {
        for t in Self.menuTitles {
            #expect(SettingsNames.retiredNames(in: t).isEmpty, "\(t)")
            #expect(SettingsNames.unknownLinks(in: t).isEmpty, "\(t)")
        }
    }

    @Test func tourUsesNoRetiredName() {
        for c in WelcomeTour.table {
            for text in [c.title, c.body] {
                #expect(SettingsNames.retiredNames(in: text).isEmpty, "\(c.id): \(text)")
                #expect(SettingsNames.unknownLinks(in: text).isEmpty, "\(c.id): \(text)")
            }
        }
        #expect(SettingsNames.retiredNames(in: WelcomeTour.againLine).isEmpty)
    }

    @Test func whatsNewUsesNoRetiredName() {
        for c in WhatsNew.table {
            for text in [c.title, c.body] {
                #expect(SettingsNames.retiredNames(in: text).isEmpty, "\(c.id): \(text)")
                #expect(SettingsNames.unknownLinks(in: text).isEmpty, "\(c.id): \(text)")
            }
        }
    }

    /// The Swift sources' string literals, outside comments and the names table itself: none uses
    /// a retired name, and every "<Page> Settings…" names a sidebar page. Catches a label typed
    /// in place where the table's name belongs.
    @Test func sourcesUseNoRetiredNameAndNoUnknownLink() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Sources/Sanduhr")
        let files = try #require(FileManager.default.enumerator(at: root, includingPropertiesForKeys: nil))
            .compactMap { $0 as? URL }
            .filter { $0.pathExtension == "swift" && $0.lastPathComponent != "SettingsNames.swift" }
        #expect(files.count > 50)
        let literal = try NSRegularExpression(pattern: #""(?:[^"\\\n]|\\.)*""#)
        var problems: [String] = []
        for file in files {
            let lines = try String(contentsOf: file, encoding: .utf8).components(separatedBy: "\n")
            for (n, line) in lines.enumerated() {
                let code = line.trimmingCharacters(in: .whitespaces)
                if code.hasPrefix("//") { continue }
                let ns = line as NSString
                for m in literal.matches(in: line, range: NSRange(location: 0, length: ns.length)) {
                    let text = ns.substring(with: m.range)
                    let found = SettingsNames.retiredNames(in: text) + SettingsNames.unknownLinks(in: text)
                    if !found.isEmpty { problems.append("\(file.lastPathComponent):\(n + 1): \(found)") }
                }
            }
        }
        #expect(problems.isEmpty, "\(problems)")
    }
}

@Suite("Settings v2 copy fixes")
struct SettingsCopyTests {
    @Test func plansLineNamesMax() {
        // F28: "Pro / Team / Enterprise" left out Max. About and the tour footer read it.
        #expect(AppInfo.independence.contains("Pro, Max, Team or Enterprise"))
        #expect(AppInfo.independence.hasPrefix("Independent third-party tool. Not affiliated with Anthropic."))
    }

    @Test func calendarSaysElseOnlyAfterAMeeting() {
        #expect(MeetingsCopy.none(hadEarlier: false) == "Nothing on the calendar today")
        #expect(MeetingsCopy.none(hadEarlier: true) == "Nothing else on the calendar today")
    }

    @MainActor @Test func mondayLineMentionsMix() {
        let mon = MessageTagReference.entries.first { $0.tag == "Mon: text" }?.meaning ?? ""
        #expect(mon.contains("Mix"))
        #expect(!mon.contains("instead of every-day lines"))
    }

    @Test func modsTilesSayWhatTheyCount() {
        // F21: "27 on" didn't say what was on.
        let c = ModCounts(folders: 2, mods: 1, plugins: 27, on: 27, missing: 0)
        #expect(c.tiles.map { "\($0.number) \($0.title)" } == ["1 mod", "27 plugins", "27 enabled plugins", "2 folders"])
        let one = ModCounts(folders: 1, mods: 2, plugins: 1, on: 1, missing: 1)
        #expect(one.tiles.map { "\($0.number) \($0.title)" } == ["2 mods", "1 plugin", "1 enabled plugin", "1 folder", "1 missing"])
        #expect(c.summary == "1 mod, 27 plugins, 27 enabled plugins, 2 folders.")
    }
}

@Suite("Settings links from the smoke hooks")
struct SettingsLinkActionTests {
    func parse(_ s: String) -> DebugRequest { DebugLink.parse(URL(string: s)!) }

    @Test func settingsLinkParsesAPageTitle() {
        // As the smoke CLI encodes it: UTF-8, percent-escaped.
        #expect(parse("sanduhr://debug/action?name=settings-link&arg=Notch%20Settings%E2%80%A6").command
                == .action(.settingsLink(.notch), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings-link&arg=Layout%20Settings...").command
                == .action(.settingsLink(.deskLayout), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings-link&arg=Integrations%20Settings%E2%80%A6").command
                == .action(.settingsLink(.integrations), dir: nil))
        let wrong = parse("sanduhr://debug/action?name=settings-link&arg=Desk%20Layout%20Settings%E2%80%A6")
        #expect(wrong.command == nil)
        #expect(wrong.error?.hasPrefix("settings-link needs") == true)
        #expect(DebugAction.names.contains("settings-link"))
    }
}

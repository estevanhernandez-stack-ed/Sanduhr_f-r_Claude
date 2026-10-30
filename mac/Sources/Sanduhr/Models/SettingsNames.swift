import Foundation

/// One name per control (Settings v2, item 72, slice 1). A control has the same name in Settings,
/// the menus, the tour, What's New and the guide: Settings and the menus read it from here, and
/// `SettingsNamesTests` checks the tour, What's New and every menu against `retired`, the names a
/// control used to go by. A button that opens Settings reads `SettingsSection.linkTitle`.
enum SettingsNames {
    /// General, Menu bar, and the shared Sanduhr menu's submenu: what the hourglass's percent
    /// follows (key `menuBarMode`). Was also "Percent beside the hourglass".
    static let menuBarShows = "Menu Bar Shows"
    /// General, Menu bar: the hourglass itself (key `menuBarItemHidden`, inverted).
    static let showMenuBarItem = "Show the hourglass in the menu bar"
    /// General, Menu bar: the Desk's meetings menu as its own menu bar item (desk key `menuIcon`).
    static let meetingsMenu = "Meetings menu in the menu bar"
    /// General, Shortcuts: check recorded keys against other apps' menus through Accessibility
    /// (item 73 b; desk key `hotKeyCheckAppMenus`, off by default).
    static let checkAppMenus = "Also check other apps' menus"
    /// The Desk's switch, at the top of the Desk page (desk key `deskEnabled`).
    static let deskSwitch = "Desk: clock, meters, meetings and the message on the desktop"
    /// Desk, OLED care: drift, dim while away, no pure white (desk key `deskOledCare`, off by default).
    static let oledCareSwitch = OledCare.title
    /// The notch island's switch, at the top of the Notch page (desk key `notch`).
    static let notchSwitch = "Notch: the island around the camera"
    /// Widget: when the widget shows on its own (key `widgetVisibility`), moved from General.
    static let showWidget = "Show the widget"
    /// Notch, Camera and mic: the one placement picker (desk key `avPlace`).
    static let cameraMicPlace = AVPlace.pickerTitle
    /// Watchers, Where they show: the notch place and the Desk place, one stored value each.
    static let watchersOnNotch = "On the notch"
    static let watchersOnDesk = "On the Desk"
    /// Mods & Config, Sanduhr's own mod at the top: the meters mod's switch, Update and Remove per
    /// folder (was Claude Code's folder row in slice 2, and Mods' "Sanduhr's mod" before that).
    static let metersAbovePrompt = "Meters above the prompt"
    /// Mods & Config, the second tab: one Claude Code folder's report. The page says "folder"
    /// for a Claude Code folder everywhere (was "By Seat" until 2026-10-17; "seat" stays in code).
    static let byFolder = "By Folder"
    /// Notch's one glow section: Sanduhr alerts, meetings, the camera light and Claude Code.
    static let notchGlow = "Notch glow"
    /// The hooks Claude Code runs to tell Sanduhr it waits or finished (IntegrationKind.hooks).
    static let claudeCodeGlowHook = "Claude Code glow hook"
    /// The Desk pieces with the Claude numbers: one line of text, and the bars.
    static let claudeMetersLine = "Claude meters (line)"
    static let claudeMetersBars = "Claude meters (bars)"
    /// The Desk piece with the machine's own readings: CPU, memory and disk (layout word `telemetry`).
    static let system = "System"
    /// Weather: looks a typed city up with Open-Meteo.
    static let weatherLookUp = "Look up"
    /// Weather: the nickname the Desk shows instead of the place's name (desk key `desk_weather`, member `nickname`, local only).
    static let weatherShowAs = "Show it as"
    /// Updates' button and every menu's item.
    static let checkForUpdates = "Check for Updates…"
    /// Themes and a theme suggestion's card.
    static let saveAndApply = "Save & Apply"
    /// Accounts, Data: an account with no Claude Code folder links one (item 74), through the
    /// folder picker the Claude Code folder menu's Choose… opens.
    static let linkFolder = "Link a Claude Code Folder…"
    /// Message's two editing modes, a segmented control.
    static let listMode = "List"
    static let textMode = "Text"
    /// About: the sheet that removes what Sanduhr wrote, Claude Code entries first (slice 5).
    static let resetSanduhr = "Reset Sanduhr…"

    /// Every current name, with the page it lives on.
    static let table: [(name: String, page: SettingsSection)] = [
        (menuBarShows, .general), (showMenuBarItem, .general), (meetingsMenu, .general),
        (SanduhrHotKeys.Shortcut.settings.name, .general), (SanduhrHotKeys.Shortcut.join.name, .general),
        (checkAppMenus, .general),
        (deskSwitch, .deskLayout), (oledCareSwitch, .deskLayout),
        (notchSwitch, .notch), (notchGlow, .notch), (cameraMicPlace, .notch),
        (claudeCodeGlowHook, .integrations), (metersAbovePrompt, .mods), (byFolder, .mods),
        (claudeMetersLine, .deskLayout), (claudeMetersBars, .deskLayout),
        (watchersOnNotch, .watchers), (watchersOnDesk, .watchers),
        (showWidget, .widgetLook), (checkForUpdates, .updates), (saveAndApply, .themes),
        (listMode, .message), (textMode, .message),
        (linkFolder, .credentials),
        (weatherLookUp, .weather), (weatherShowAs, .weather),
        (resetSanduhr, .about),
    ]

    /// Names that are gone, each with the name that replaced it. None may appear in a menu, the
    /// tour, What's New or a Settings label (matched as written, case included).
    static let retired: [(old: String, now: String)] = [
        ("Percent beside the hourglass", menuBarShows),
        ("Menu bar shows", menuBarShows),
        ("Desk menu in the menu bar", meetingsMenu),
        ("Extend the camera notch", notchSwitch),
        ("Glow for Claude Code", notchGlow),
        ("Notch, Glow", "Notch, \(notchGlow)"),
        ("Notch glow when Claude needs you", claudeCodeGlowHook),
        ("notch glow hook", claudeCodeGlowHook),
        ("notch glow for when Claude needs you", claudeCodeGlowHook),
        ("Claude line", claudeMetersLine),
        ("Check Now", checkForUpdates),
        ("Save and Apply", saveAndApply),
        ("Edit as text", textMode),
        ("Edit as a list", listMode),
        ("Desk Layout Settings…", SettingsSection.deskLayout.linkTitle),
        ("Meter Settings…", SettingsSection.alerts.linkTitle),
        ("Watcher Settings…", SettingsSection.watchers.linkTitle),
        ("Data Settings…", SettingsSection.credentials.linkTitle),
        // Slice 2: pages that merged or were renamed, and controls that moved into another.
        ("Layout Settings…", SettingsSection.deskLayout.linkTitle),
        ("Meters Settings…", SettingsSection.alerts.linkTitle),
        ("Integrations Settings…", SettingsSection.integrations.linkTitle),
        ("Claude Usage Settings…", SettingsSection.usage.linkTitle),
        ("Mods Settings…", SettingsSection.mods.linkTitle),
        ("Widget Look Settings…", SettingsSection.widgetLook.linkTitle),
        ("Pacing & Focus Settings…", SettingsSection.widgetLook.linkTitle),
        ("Show the Claude meters on the desktop", SettingsSection.deskLayout.linkTitle),
        ("Widget: the floating window with the tools", showWidget),
        ("Option+J joins the next meeting, Option+S opens these settings", SanduhrHotKeys.Shortcut.settings.name),
        // 2026-10-08: the keys can be changed, so a label naming them is read from the saved combo.
        ("Option+S opens Settings", SanduhrHotKeys.Shortcut.settings.name),
        ("Option+J joins the next meeting", SanduhrHotKeys.Shortcut.join.name),
        ("Sanduhr's mod: ", metersAbovePrompt),
        // 2026-10-17: one word per concept on Mods & Config; a Claude Code folder is a folder.
        ("By Seat", byFolder),
    ]

    /// The retired names `text` still uses, empty when it uses none.
    static func retiredNames(in text: String) -> [String] {
        retired.map(\.old).filter { text.contains($0) }
    }

    /// Every "<Page> Settings…" in `text` whose page is not a sidebar title, empty when each one
    /// names a real page. The page is the run of capitalized words (and "&") right before
    /// " Settings…"; "Settings…" alone, "All Settings…", macOS's "System Settings…" (with a verb
    /// before it, "Open System Settings…") and a placeholder such as "<Page>" pass.
    static func unknownLinks(in text: String) -> [String] {
        let suffix = " Settings…"
        var out: [String] = []
        var rest = Substring(text)
        while let r = rest.range(of: suffix) {
            var words: [Substring] = []
            for word in rest[..<r.lowerBound].split(separator: " ").reversed() {
                // A quote or bracket before a word starts the run there.
                let core = word.drop { !$0.isLetter && $0 != "&" }
                guard core == "&" || (core.first?.isUppercase == true && core.allSatisfy { $0.isLetter }) else { break }
                words.insert(core, at: 0)
                if core.count != word.count { break }
            }
            let title = words.joined(separator: " ") + suffix
            if !words.isEmpty, words != ["All"], words.last != "System", SettingsSection.linked(title) == nil { out.append(title) }
            rest = rest[r.upperBound...]
        }
        return out
    }
}

extension SettingsSection {
    /// The title of a button or menu item that opens this page: "<Page> Settings…", with the
    /// page's sidebar title exactly (Settings v2's rule: a button names the page it opens).
    var linkTitle: String { "\(title) Settings…" }

    /// The page a "<Page> Settings…" title opens, nil for a title that names no page.
    static func linked(_ title: String) -> SettingsSection? {
        allCases.first { $0.linkTitle == title }
    }
}

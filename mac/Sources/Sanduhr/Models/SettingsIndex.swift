import Foundation
import CoreGraphics

/// Settings v2, item 72, slice 3: reach. Every section of every page has an anchor, a page-local
/// id that `sanduhr://settings/<page>#<anchor>`, the smoke hook's `settings <page> <anchor>`, every
/// "<Page> Settings…" button that names a section and the sidebar search open the page at. The
/// views mark each one with `.settingsAnchor(_:)` (SettingsReach.swift).

/// One searchable place in Settings: a page (anchor nil), a section or a control.
struct SettingsEntry: Equatable {
    let title: String
    let page: SettingsSection
    /// The section or control it opens at, nil for the page's top.
    let anchor: String?
    /// Other words a person might type for it ("percent" for Menu Bar Shows).
    var synonyms: [String] = []
    /// Folded under the page's Advanced disclosure: opening it opens the disclosure.
    var advanced = false
}

/// The anchors, page by page. Ids are page-local: "glow" on Notch is not "glow" on Watchers.
enum SettingsAnchor {
    // General
    static let surfaces = "surfaces"
    static let menuBarShows = "menu-bar-shows"
    static let meetingsMenu = "meetings-menu"
    static let startup = "startup"
    static let shortcuts = "shortcuts"
    static let quit = "quit"
    // Accounts
    static let accounts = "accounts"
    /// Accounts, the selected account's Data section.
    static let data = "data"
    // Alerts
    static let notifications = "notifications"
    static let quietHours = "quiet-hours"
    static let signInReminder = "sign-in-reminder"
    /// Alerts, Each limit (was the Meters page, raw value `deskMeters`).
    static let eachLimit = "each-limit"
    // Desk
    static let desk = "desk"
    static let arrange = "arrange"
    static let pieces = "pieces"
    static let order = "order"
    static let clicks = "clicks"
    static let margins = "margins"
    // Desk Look
    static let fonts = "fonts"
    static let sizes = "sizes"
    static let colors = "colors"
    static let hex = "hex"
    // Message
    static let editor = "editor"
    static let rotation = "rotation"
    static let askClaude = "ask-claude"
    /// The row Add Line just put in, its editor open (not searchable: it exists only after an add).
    static let newLine = "new-line"
    // Notch
    static let notch = "notch"
    static let text = "text"
    static let glow = "glow"
    static let cameraMic = "camera-mic"
    static let cameraLight = "camera-light"
    static let size = "size"
    static let textColor = "text-color"
    // Now Playing
    static let source = "source"
    static let show = "show"
    static let looks = "looks"
    static let idle = "idle"
    static let apps = "apps"
    static let fallback = "fallback"
    // Watchers
    static let switches = "switches"
    static let whereTheyShow = "where"
    static let abovePrompt = "above-prompt"
    // Claude Code
    static let python = "python"
    static let folders = "folders"
    /// Claude Code, the line under the folders that links to where the notch glow is switched on.
    static let glowHint = "glow-hint"
    // Mods & Config
    /// Sanduhr's own mod, Meters above the prompt, at the top (its one home since 2026-10-08).
    static let metersMod = "meters"
    static let inventory = "inventory"
    // Widget
    /// Widget, Pacing calculators (was the Pacing & Focus page, raw value `pacing`).
    static let pacing = "pacing"
    static let font = "font"
    // Themes
    static let gallery = "gallery"
    static let ownThemes = "own"
    static let claudeThemes = "claude"
    // Updates
    static let check = "check"
    static let automatic = "automatic"
    // About
    static let about = "about"
    static let links = "links"
    static let notices = "notices"
    /// Every Advanced disclosure's own row.
    static let advanced = "advanced"

    /// Every anchor in sidebar order, page by page, with its title and synonyms.
    static let all: [SettingsEntry] = general + accountsAndAlerts + deskPages + notchPages + claudeCodePages + widgetAndHelp

    private static let general: [SettingsEntry] = [
        SettingsEntry(title: "Surfaces", page: .general, anchor: surfaces, synonyms: ["status", "desk on", "notch on"]),
        SettingsEntry(title: "Menu bar", page: .general, anchor: menuBarShows),
        SettingsEntry(title: SettingsNames.menuBarShows, page: .general, anchor: menuBarShows,
                      synonyms: ["percent", "hourglass", "rotate", "session", "weekly"]),
        SettingsEntry(title: SettingsNames.meetingsMenu, page: .general, anchor: meetingsMenu, synonyms: ["calendar", "join"]),
        SettingsEntry(title: "Startup", page: .general, anchor: startup, synonyms: ["Open Sanduhr at login", "login", "launch"]),
        SettingsEntry(title: "Shortcuts", page: .general, anchor: shortcuts,
                      synonyms: ["Option+S", "Option+J", "⌥S", "⌥J", "hotkey", "keyboard", "shortcut", "record", "keys",
                                 "Accessibility", "app menus"]),
        SettingsEntry(title: "Quit Sanduhr für Claude", page: .general, anchor: quit, synonyms: ["quit", "exit"]),
    ]

    private static let accountsAndAlerts: [SettingsEntry] = [
        SettingsEntry(title: "Accounts", page: .credentials, anchor: accounts,
                      synonyms: ["sign in", "session key", "sessionKey", "cf_clearance", "cookie", "add account", "rename",
                                 "sign out", "remove account", "make active", "follow"]),
        SettingsEntry(title: "Data", page: .credentials, anchor: data,
                      synonyms: ["meter history", "Claude Code activity", "project names", "Share with your agents",
                                 "work account", "erase", "privacy", "Claude Code folder"]),
        SettingsEntry(title: "Notifications", page: .alerts, anchor: notifications,
                      synonyms: ["alert", "banner", "threshold", "Where alerts show", "sound", "Send a Test"]),
        SettingsEntry(title: "Quiet hours", page: .alerts, anchor: quietHours, synonyms: ["do not disturb", "night"]),
        SettingsEntry(title: "Sign-in reminders", page: .alerts, anchor: signInReminder,
                      synonyms: [SignInReminder.title, "sign in", "signed out", "without Claude"]),
        SettingsEntry(title: "Each limit", page: .alerts, anchor: eachLimit,
                      synonyms: ["Warn when nearly full", "red", "temporary limit", "Show this limit", "hide limit", "meters"]),
    ]

    private static let deskPages: [SettingsEntry] = [
        SettingsEntry(title: SettingsNames.deskSwitch, page: .deskLayout, anchor: desk, synonyms: ["turn on", "desktop"]),
        SettingsEntry(title: "Arrange Desk…", page: .deskLayout, anchor: arrange, synonyms: ["drag", "move", "arrange"]),
        SettingsEntry(title: "Where each piece sits", page: .deskLayout, anchor: pieces,
                      synonyms: ["corner", "position", "place", "hidden", "clock", "meetings", "Read today's meetings"]),
        SettingsEntry(title: "Order", page: .deskLayout, anchor: order, synonyms: ["stack", "reorder"]),
        SettingsEntry(title: "Clicks", page: .deskLayout, anchor: clicks, synonyms: [DeskPieceClicks.title, "click"]),
        SettingsEntry(title: "Margins", page: .deskLayout, anchor: margins, synonyms: ["padding", "edge", "inset"], advanced: true),
        SettingsEntry(title: "Fonts", page: .deskLook, anchor: fonts, synonyms: ["Desk font", "Message font", "typeface"]),
        SettingsEntry(title: "Sizes", page: .deskLook, anchor: sizes, synonyms: ["clock size", "message size"]),
        SettingsEntry(title: "Colors", page: .deskLook, anchor: colors,
                      synonyms: ["color", "gradient", "preset", "Glow around the message", "Drop shadow under the clock text"]),
        SettingsEntry(title: "Hex colors", page: .deskLook, anchor: hex, synonyms: ["hex", "custom color"], advanced: true),
        SettingsEntry(title: "Add Line", page: .message, anchor: editor,
                      synonyms: ["new line", "List", "Text", "Save", "Revert", "messages.txt"]),
        SettingsEntry(title: "Rotation", page: .message, anchor: rotation,
                      synonyms: ["Change the line", "hourly", "Mix every-day lines", "On special days"]),
        SettingsEntry(title: "Ask Claude", page: .message, anchor: askClaude, synonyms: ["suggest", "Let Claude change the messages"]),
    ]

    private static let notchPages: [SettingsEntry] = [
        SettingsEntry(title: SettingsNames.notchSwitch, page: .notch, anchor: notch, synonyms: ["island", "camera notch"]),
        SettingsEntry(title: "Text", page: .notch, anchor: text,
                      synonyms: ["wing", "Left wing", "Right wing", "Text beside the camera", "Text under the camera too"]),
        SettingsEntry(title: SettingsNames.notchGlow, page: .notch, anchor: glow,
                      synonyms: ["Test Glow", "Claude Code waiting", "Claude Code finishes"]),
        SettingsEntry(title: "Camera and mic", page: .notch, anchor: cameraMic,
                      synonyms: ["red dot", "microphone", "indicator", AVPlace.pickerTitle]),
        SettingsEntry(title: "Camera fill light", page: .notch, anchor: cameraLight, synonyms: ["light", "brightness"]),
        SettingsEntry(title: "Size", page: .notch, anchor: size,
                      synonyms: ["Extra width each side", "Extra height below", "chin", "height"], advanced: true),
        SettingsEntry(title: "Notch text color", page: .notch, anchor: textColor, synonyms: ["color"], advanced: true),
        SettingsEntry(title: "Now playing", page: .nowPlaying, anchor: source, synonyms: ["source", "place it"]),
        SettingsEntry(title: "Show", page: .nowPlaying, anchor: show, synonyms: ["Hide while paused"]),
        SettingsEntry(title: "Looks", page: .nowPlaying, anchor: looks),
        SettingsEntry(title: "When nothing is playing", page: .nowPlaying, anchor: idle, synonyms: ["idle", "paused"]),
        SettingsEntry(title: "Apps", page: .nowPlaying, anchor: apps, synonyms: ["Music", "Spotify", "browser"]),
        SettingsEntry(title: "When the system now playing is unavailable", page: .nowPlaying, anchor: fallback,
                      synonyms: ["Ask Music and Spotify directly"]),
        SettingsEntry(title: "Let agents show watchers", page: .watchers, anchor: switches,
                      synonyms: ["agents", "Show Claude Code's background work", "background work"]),
        SettingsEntry(title: "Where they show", page: .watchers, anchor: whereTheyShow,
                      synonyms: [SettingsNames.watchersOnNotch, SettingsNames.watchersOnDesk, "placement"]),
        SettingsEntry(title: "Glow when a watcher waits on you", page: .watchers, anchor: glow),
        SettingsEntry(title: "Above the prompt", page: .watchers, anchor: abovePrompt,
                      synonyms: ["Show watchers above the prompt", "band"]),
    ]

    private static let claudeCodePages: [SettingsEntry] = [
        SettingsEntry(title: "Python", page: .integrations, anchor: python, synonyms: ["python3", "Command Line Tools"]),
        SettingsEntry(title: "Folders", page: .integrations, anchor: folders,
                      synonyms: ["Add Folder", "MCP server", "statusline", "prompt", "hooks", "install", "account"]),
        SettingsEntry(title: "Turning the glow on", page: .integrations, anchor: glowHint,
                      synonyms: ["Notch Settings…", "glow hook"]),
        SettingsEntry(title: SettingsNames.metersAbovePrompt, page: .mods, anchor: metersMod,
                      synonyms: [IntegrationScripts.modName, "Sanduhr's own mod", "mod", "band", "bars", "animated",
                                 "Update", "Remove", "enabledPlugins"]),
        SettingsEntry(title: "Mods and plugins", page: .mods, anchor: inventory, synonyms: ["mods", "plugins", "Check", "risk"]),
    ]

    private static let widgetAndHelp: [SettingsEntry] = [
        SettingsEntry(title: SettingsNames.showWidget, page: .widgetLook, anchor: show,
                      synonyms: ["floating window", "Show the widget now", "hide the widget"]),
        SettingsEntry(title: "Font", page: .widgetLook, anchor: font, synonyms: ["Subtle mode", "Font Book", "system font"]),
        SettingsEntry(title: "Pacing calculators", page: .widgetLook, anchor: pacing, synonyms: ["pin", "cool down", "surplus"]),
        SettingsEntry(title: "Themes", page: .themes, anchor: gallery, synonyms: ["theme", "gallery"]),
        SettingsEntry(title: "Your own themes", page: .themes, anchor: ownThemes,
                      synonyms: ["import", "json", SettingsNames.saveAndApply, "Copy Agent Prompt"]),
        SettingsEntry(title: "Let Claude change themes directly", page: .themes, anchor: claudeThemes),
        SettingsEntry(title: SettingsNames.checkForUpdates, page: .updates, anchor: check, synonyms: ["update", "Sparkle", "version"]),
        SettingsEntry(title: "Automatic updates", page: .updates, anchor: automatic, synonyms: ["download", "install"]),
        SettingsEntry(title: "About", page: .about, anchor: about, synonyms: ["version", "What's New", "Take the Tour", "plans"]),
        SettingsEntry(title: "Links", page: .about, anchor: links, synonyms: ["GitHub", "website", "privacy policy"]),
        SettingsEntry(title: "Third-Party Notices", page: .about, anchor: notices, synonyms: ["license", "credits"]),
    ]

    /// The anchors of `page`, in page order.
    static func entries(on page: SettingsSection) -> [SettingsEntry] { all.filter { $0.page == page } }

    /// `id` is an anchor on `page`. New line is one on Message, though search never offers it.
    static func exists(_ id: String, on page: SettingsSection) -> Bool {
        (page == .message && id == newLine) || all.contains { $0.page == page && $0.anchor == id }
    }

    /// `id` on `page` sits under the page's Advanced disclosure.
    static func isAdvanced(_ id: String?, on page: SettingsSection) -> Bool {
        guard let id else { return false }
        return id == advanced || all.contains { $0.page == page && $0.anchor == id && $0.advanced }
    }
}

extension SettingsNames {
    /// Where each name in `table` lives on its page: the anchor search and links open it at.
    static let anchors: [String: String] = [
        menuBarShows: SettingsAnchor.menuBarShows, meetingsMenu: SettingsAnchor.meetingsMenu,
        SanduhrHotKeys.Shortcut.settings.name: SettingsAnchor.shortcuts,
        SanduhrHotKeys.Shortcut.join.name: SettingsAnchor.shortcuts,
        checkAppMenus: SettingsAnchor.shortcuts,
        deskSwitch: SettingsAnchor.desk, notchSwitch: SettingsAnchor.notch, notchGlow: SettingsAnchor.glow,
        cameraMicPlace: SettingsAnchor.cameraMic,
        claudeCodeGlowHook: SettingsAnchor.folders, metersAbovePrompt: SettingsAnchor.metersMod,
        claudeMetersLine: SettingsAnchor.pieces, claudeMetersBars: SettingsAnchor.pieces,
        watchersOnNotch: SettingsAnchor.whereTheyShow, watchersOnDesk: SettingsAnchor.whereTheyShow,
        showWidget: SettingsAnchor.show, checkForUpdates: SettingsAnchor.check, saveAndApply: SettingsAnchor.ownThemes,
        listMode: SettingsAnchor.editor, textMode: SettingsAnchor.editor,
    ]
}

/// Sidebar search (slice 3): pages, sections and controls by name, prefix and word match only.
enum SettingsSearch {
    /// Words a page goes by besides its title.
    static let pageSynonyms: [SettingsSection: [String]] = [
        .general: ["menu bar", "login"],
        .credentials: ["sign in", "account"],
        .usage: ["history", "tokens", "records", "trends", "sessions", "meter history", "chart", "csv", "export"],
        .alerts: ["notification", "warning"],
        .integrations: ["statusline", "prompt", "MCP", "Integrations"],
        .mods: ["mods", "plugins", "config"],
        .widgetLook: ["Widget Look", "Pacing & Focus"],
        .updates: ["update", "Sparkle"],
    ]

    /// The whole index: every page, every anchor and every name in `SettingsNames.table`.
    static let index: [SettingsEntry] = {
        var out = SettingsSection.groups.flatMap(\.sections).map {
            SettingsEntry(title: $0.title, page: $0, anchor: nil, synonyms: pageSynonyms[$0] ?? [])
        }
        out += SettingsAnchor.all
        for (name, page) in SettingsNames.table {
            let anchor = SettingsNames.anchors[name]
            if !out.contains(where: { $0.title == name && $0.page == page && $0.anchor == anchor }) {
                out.append(SettingsEntry(title: name, page: page, anchor: anchor,
                                         advanced: SettingsAnchor.isAdvanced(anchor, on: page)))
            }
        }
        return out
    }()

    /// A match: the entry, and 0 when its title matched, 1 when only a synonym did.
    struct Hit: Equatable {
        let entry: SettingsEntry
        let rank: Int
    }

    /// The entries `query` finds, best first: title matches before synonym matches, then in
    /// sidebar order. Empty for an empty query.
    static func hits(_ query: String, in index: [SettingsEntry] = index) -> [Hit] {
        let q = words(query)
        guard !q.isEmpty else { return [] }
        let order = SettingsSection.groups.flatMap(\.sections)
        var out: [(hit: Hit, i: Int)] = []
        for (i, e) in index.enumerated() {
            if matches(q, e.title) {
                out.append((Hit(entry: e, rank: 0), i))
            } else if e.synonyms.contains(where: { matches(q, $0) }) {
                out.append((Hit(entry: e, rank: 1), i))
            }
        }
        out.sort {
            if $0.hit.rank != $1.hit.rank { return $0.hit.rank < $1.hit.rank }
            let a = order.firstIndex(of: $0.hit.entry.page) ?? 0, b = order.firstIndex(of: $1.hit.entry.page) ?? 0
            return a != b ? a < b : $0.i < $1.i
        }
        return out.map(\.hit)
    }

    /// The hits grouped by page, for the sidebar: each page once, in the order of its best hit (so
    /// the top group is what Return opens), its matching sections and controls under it (the
    /// page's own entry is the page row, not a line under it).
    static func grouped(_ hits: [Hit]) -> [(page: SettingsSection, entries: [SettingsEntry])] {
        var pages: [SettingsSection] = []
        for h in hits where !pages.contains(h.entry.page) { pages.append(h.entry.page) }
        return pages.map { page in
            let mine = hits.filter { $0.entry.page == page }
            var seen: [SettingsEntry] = []
            for h in mine where h.entry.anchor != nil && !seen.contains(where: { $0.title == h.entry.title }) {
                seen.append(h.entry)
            }
            return (page, seen)
        }
    }

    /// Each query word is the start of a word in `text` (so "glow" finds Notch glow and "men bar"
    /// finds Menu Bar Shows), or the whole query starts `text`.
    static func matches(_ query: [String], _ text: String) -> Bool {
        let t = words(text)
        let phrase = query.joined(separator: " ")
        if !phrase.isEmpty, t.joined(separator: " ").hasPrefix(phrase) { return true }
        return query.allSatisfy { q in t.contains { $0.hasPrefix(q) } }
    }

    /// Lowercased words, accents folded, split at anything that is not a letter or a digit.
    static func words(_ s: String) -> [String] {
        s.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: nil)
            .split(whereSeparator: { !$0.isLetter && !$0.isNumber })
            .map(String.init)
    }
}

/// `sanduhr://settings/<page>#<anchor>` and `estedesk://` the same (slice 3). The host alone opens
/// Settings where it was left, as before.
enum SettingsLink {
    /// The page and anchor a settings link names: nil page for the host alone or a page this
    /// build does not know; a retired raw value opens the page it merged into at its anchor; an
    /// anchor the page does not have is dropped.
    static func target(_ url: URL) -> (section: SettingsSection?, anchor: String?) {
        let raw = url.path.split(separator: "/").first.map(String.init) ?? ""
        guard !raw.isEmpty, let page = DebugLink.page(raw) else { return (nil, nil) }
        let fragment = url.fragment.flatMap { $0.isEmpty ? nil : $0.lowercased() }
        let anchor = fragment.map { SettingsAnchor.exists($0, on: page.section) ? $0 : nil } ?? page.anchor
        return (page.section, anchor)
    }

    /// "notch glow", "notch#glow" or "notch": the smoke hook's argument as a page and an anchor.
    /// An anchor the page does not have fails, naming the ones it has.
    static func parse(_ arg: String) -> Result<(section: SettingsSection, anchor: String?), DebugLink.Failure> {
        let parts = arg.split(whereSeparator: { $0 == " " || $0 == "#" || $0 == "/" }).map(String.init)
        guard let first = parts.first, let page = DebugLink.page(first) else {
            let names = SettingsSection.allCases.map(\.rawValue) + SettingsSection.aliases.keys.sorted()
            return .failure(.init(message: "unknown settings section: \(arg) (one of \(names.joined(separator: ", ")))"))
        }
        guard parts.count > 1 else { return .success(page) }
        let anchor = parts[1...].joined(separator: "-").lowercased()
        guard SettingsAnchor.exists(anchor, on: page.section) else {
            let ids = SettingsAnchor.entries(on: page.section).compactMap(\.anchor)
            return .failure(.init(message: "\(page.section.rawValue) has no anchor \(anchor) (one of \(ids.joined(separator: ", ")))"))
        }
        return .success((page.section, anchor))
    }
}

/// Whether a marked section or control is on screen: its top inside the page's scrolling area,
/// with at least its first line showing (state.yaml `settings_anchor_visible`).
enum SettingsAnchorVisibility {
    static func isVisible(_ frame: CGRect, in viewport: CGRect) -> Bool {
        guard frame.height > 0, frame.width > 0, viewport.height > 0 else { return false }
        let line = min(frame.height, 20)
        return frame.minY >= viewport.minY - 1 && frame.minY + line <= viewport.maxY + 1
            && frame.maxX > viewport.minX && frame.minX < viewport.maxX
    }
}

/// Below 600 pt of window height a page's preview folds to a 44 pt strip (slice 3; was 720 until
/// 2026-10-08, which folded the default 680 pt window). The default window and anything taller show
/// full previews; a 13-inch screen squeezed small still folds.
enum SettingsPreviewFold {
    static let threshold: CGFloat = 600
    static let stripHeight: CGFloat = 44
    /// The Settings window's height when it first opens, where the screen allows (was 600).
    static let defaultWindowHeight: CGFloat = 680

    static func folds(windowHeight: CGFloat) -> Bool { windowHeight > 0 && windowHeight < threshold }
}

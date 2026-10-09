import Foundation

/// Whether the debug hooks answer. Off unless `defaults write com.626labs.sanduhr debugHooks
/// -bool true` is set or Sanduhr was started with SANDUHR_DEBUG_HOOKS=1, so a shipped build
/// ignores every sanduhr://debug/... link.
enum DebugGate {
    static let defaultsKey = "debugHooks"
    static let environmentKey = "SANDUHR_DEBUG_HOOKS"

    static func isOn(defaultsFlag: Bool, environment: [String: String]) -> Bool {
        if defaultsFlag { return true }
        guard let value = environment[environmentKey]?.lowercased() else { return false }
        return ["1", "true", "yes"].contains(value)
    }
}

/// What a hook action does, checked and parsed from the link.
enum DebugAction: Equatable {
    case showWidget, hideWidget
    /// Open Settings at a section, or where it was left (nil). A retired raw value (`deskMeters`,
    /// `pacing`) opens the page it merged into, at its anchor (Settings v2, slice 2).
    case settings(SettingsSection?, anchor: String? = nil)
    /// Settings at the page a "<Page> Settings…" button names (Settings v2, slice 1), opened the
    /// way SettingsLinkButton opens it. `settings-names.yaml` checks each button lands where it says.
    case settingsLink(SettingsSection)
    /// Settings v2, slice 3: the sidebar search's Return, the best match for these words opened,
    /// scrolled into view and lit, as a person typing them would get.
    case settingsSearch(String)
    case closeSettings
    case refresh
    case testAlert
    case pulse(Tier)
    /// One of the Tools items: .deepWork, .pacing or .snake, done as the menus do it.
    case tool(MenuCommand)
    case desk(Bool)
    case notch(Bool)
    /// The camera light shown or hidden by hand, as Tools, Camera Light does.
    case cameraLight(Bool)
    /// The notch glow for one kind of event, whatever its switch says.
    case glow(NotchGlowEvent.Kind)
    /// The widget's theme by id, picked as the Theme menu and the gallery pick it.
    case theme(String)
    /// Demo data on the Desk for screenshots: made-up meetings and a pinned message, held in
    /// memory only (the calendar is never touched). Off puts the real ones back.
    case demo(Bool)
    /// The next account, as the widget's chip cycles it. Safe: nothing is added, signed out or
    /// removed, and no hook can do those.
    case cycleAccount
    /// Settings at the Claude Usage page (item 48), on a tab. Reads only; erases nothing.
    case usage(UsageTab)
    /// The What's New window (item 57) with every card up to this version, as About opens it, or
    /// closed. Records nothing: the last-seen version is left as it was.
    case whatsNew(Bool)
    /// The welcome tour (item 61) at a step (1-based), as Take the Tour… opens it, or closed.
    /// Records nothing: the tour's state and What's New's last-seen version stay as they were.
    case tour(step: Int)
    case closeTour
    /// A made-up watcher (item 66), whatever the Watchers switches say: start puts one up, wait
    /// sets it waiting on you, pass and fail end it, clear dismisses every watcher.
    case watchTest(WatchTest)

    enum WatchTest: String, CaseIterable {
        case start, wait, pass, fail, clear
    }
    /// A faked camera or microphone signal (item 67), shown through the indicators' own switches;
    /// off hands the indicator back to the real signal. Held in memory only.
    case avTest(AVTest, on: Bool)

    enum AVTest: String, CaseIterable {
        case camera, mic
    }
    /// Settings, Message's editor (item 69): add puts in the smoke's own line (Fridays, "ship it.",
    /// a sunset gradient in script with a sweep), held unsaved; revert drops unsaved edits. Neither
    /// saves: messages.txt is never written by a hook.
    case messageEditor(MessageEditorStep)

    enum MessageEditorStep: String, CaseIterable {
        case add, revert
    }
    /// Arrange mode on the Desk (item 60): start enters it as Arrange Desk… does, test makes the
    /// smoke's own edit (the clock to Top right at 120%, unsaved), done ends it writing the layout
    /// once when it changed, cancel ends it writing nothing.
    case deskArrange(DeskArrangeStep)

    enum DeskArrangeStep: String, CaseIterable {
        case start, test, done, cancel
    }

    /// A shortcut's keys set as General's recorder sets them (2026-10-08), with its rules: nil is
    /// the default. Refused (an error, nothing saved) without ⌘, ⌃ or ⌥ or when the other
    /// shortcut uses them; keys macOS uses are refused in state.yaml unless `anyway` (item 73). Writes the two desk keys; Restore puts the keys back after a run.
    case hotKey(SanduhrHotKeys.Shortcut, HotKeyCombo?, anyway: Bool = false)

    static let names = ["show-widget", "hide-widget", "settings", "settings-link", "settings-search", "close-settings", "refresh",
                        "test-alert", "pulse", "tool", "desk", "notch", "camera-light", "glow",
                        "theme", "account", "usage", "whats-new", "close-whats-new",
                        "tour", "tour-step", "close-tour", "watch-test", "av-test", "message-editor",
                        "desk-arrange", "hot-key"]
}

enum DebugCommand: Equatable {
    case snapshot(dir: String)
    case action(DebugAction, dir: String?)
}

/// A parsed sanduhr://debug/... link: the command, or why there is none. `dir` is kept either
/// way, so the error can be written where the caller is waiting.
struct DebugRequest: Equatable {
    var command: DebugCommand?
    var error: String?
    var dir: String?
}

enum DebugLink {
    /// sanduhr://debug/<command>?…
    static func isDebug(_ url: URL) -> Bool {
        url.scheme?.lowercased() == "sanduhr" && url.host?.lowercased() == "debug"
    }

    static func parse(_ url: URL) -> DebugRequest {
        let parts = URLComponents(url: url, resolvingAgainstBaseURL: false)
        var query: [String: String] = [:]
        for item in parts?.queryItems ?? [] where query[item.name] == nil {
            query[item.name] = item.value ?? ""
        }
        let dir = query["dir"].flatMap { $0.isEmpty ? nil : $0 }
        let name = url.path.split(separator: "/").first.map(String.init)?.lowercased() ?? ""
        var request = DebugRequest(dir: dir)
        switch name {
        case "snapshot":
            if let dir { request.command = .snapshot(dir: dir) } else { request.error = "snapshot needs dir=<path>" }
        case "action":
            switch action(query["name"] ?? "", arg: query["arg"].flatMap { $0.isEmpty ? nil : $0 }) {
            case .success(let a): request.command = .action(a, dir: dir)
            case .failure(let e): request.error = e.message
            }
        default:
            request.error = name.isEmpty ? "no debug command" : "unknown debug command: \(name)"
        }
        return request
    }

    struct Failure: Error { let message: String }

    static func action(_ name: String, arg: String?) -> Result<DebugAction, Failure> {
        func bad(_ why: String) -> Result<DebugAction, Failure> { .failure(Failure(message: why)) }
        func onOff(_ make: (Bool) -> DebugAction) -> Result<DebugAction, Failure> {
            switch arg?.lowercased() {
            case "on", "true", "1": return .success(make(true))
            case "off", "false", "0": return .success(make(false))
            default: return bad("\(name) needs arg=on or arg=off")
            }
        }
        switch name.lowercased() {
        case "show-widget": return .success(.showWidget)
        case "hide-widget": return .success(.hideWidget)
        case "settings":
            guard let arg else { return .success(.settings(nil)) }
            // "notch", "notch glow" or "notch#glow" (slice 3: an anchor on the page).
            switch SettingsLink.parse(arg) {
            case .success(let page): return .success(.settings(page.section, anchor: page.anchor))
            case .failure(let e): return .failure(e)
            }
        case "settings-search":
            guard let arg, !SettingsSearch.hits(arg).isEmpty else {
                return bad("settings-search needs arg=<words> that find something in Settings")
            }
            return .success(.settingsSearch(arg))
        case "settings-link":
            // "Notch Settings…", or with three dots for a terminal.
            let title = (arg ?? "").replacingOccurrences(of: "...", with: "…")
            guard let section = SettingsSection.linked(title) else {
                return bad("settings-link needs arg=<Page> Settings… (one of \(SettingsSection.allCases.map(\.linkTitle).joined(separator: ", ")))")
            }
            return .success(.settingsLink(section))
        case "close-settings": return .success(.closeSettings)
        case "refresh": return .success(.refresh)
        case "test-alert": return .success(.testAlert)
        case "pulse":
            guard let arg else { return .success(.pulse(.fiveHour)) }
            guard let tier = Tier(rawValue: arg.lowercased()) else { return bad("unknown tier: \(arg)") }
            return .success(.pulse(tier))
        case "tool":
            switch arg?.lowercased() {
            case "deep-work": return .success(.tool(.deepWork))
            case "pacing": return .success(.tool(.pacing))
            case "snake": return .success(.tool(.snake))
            default: return bad("tool needs arg=deep-work, pacing or snake")
            }
        case "desk": return onOff(DebugAction.desk)
        case "notch": return onOff(DebugAction.notch)
        case "camera-light": return onOff(DebugAction.cameraLight)
        case "demo": return onOff(DebugAction.demo)
        case "glow":
            guard let arg else { return .success(.glow(.alert)) }
            guard let kind = NotchGlowEvent.Kind(rawValue: arg.lowercased()) else {
                return bad("glow needs arg=alert, meeting, camera, claude-waiting or claude-done")
            }
            return .success(.glow(kind))
        case "theme":
            guard let arg else { return bad("theme needs arg=<theme id>") }
            return .success(.theme(arg.lowercased()))
        case "account":
            guard arg?.lowercased() == "next" else { return bad("account needs arg=next") }
            return .success(.cycleAccount)
        case "usage":
            guard let arg else { return .success(.usage(.overview)) }
            guard let tab = UsageTab(rawValue: arg.lowercased()) else {
                return bad("usage needs arg=overview, trends, sessions or meters")
            }
            return .success(.usage(tab))
        case "whats-new": return .success(.whatsNew(true))
        case "close-whats-new": return .success(.whatsNew(false))
        case "tour": return .success(.tour(step: 1))
        case "tour-step":
            guard let arg, let n = Int(arg), n >= 1 else { return bad("tour-step needs arg=<step number from 1>") }
            return .success(.tour(step: n))
        case "close-tour": return .success(.closeTour)
        case "watch-test":
            guard let test = arg.flatMap({ DebugAction.WatchTest(rawValue: $0.lowercased()) }) else {
                return bad("watch-test needs arg=start, wait, pass, fail or clear")
            }
            return .success(.watchTest(test))
        case "av-test":
            // "camera on", "mic off" (also "camera-on", "mic:off").
            let words = (arg ?? "").lowercased().split(whereSeparator: { " +-:_".contains($0) }).map(String.init)
            guard words.count == 2, let which = DebugAction.AVTest(rawValue: words[0]),
                  let on = ["on": true, "off": false][words[1]] else {
                return bad("av-test needs arg=camera on, camera off, mic on or mic off")
            }
            return .success(.avTest(which, on: on))
        case "message-editor":
            guard let step = arg.flatMap({ DebugAction.MessageEditorStep(rawValue: $0.lowercased()) }) else {
                return bad("message-editor needs arg=add or revert")
            }
            return .success(.messageEditor(step))
        case "desk-arrange":
            guard let step = arg.flatMap({ DebugAction.DeskArrangeStep(rawValue: $0.lowercased()) }) else {
                return bad("desk-arrange needs arg=start, test, done or cancel")
            }
            return .success(.deskArrange(step))
        case "hot-key":
            // "settings ctrl-opt-s", "join ⌃⌥J", "settings default"; "anyway" after the keys saves
            // keys macOS also uses (Use Anyway, item 73).
            let parts = (arg ?? "").split(separator: " ", maxSplits: 1).map(String.init)
            let which: [String: SanduhrHotKeys.Shortcut] = ["settings": .settings, "join": .join]
            guard parts.count == 2, let s = which[parts[0].lowercased()] else {
                return bad("hot-key needs arg=settings|join <keys> [anyway] (ctrl-opt-s, ⌃⌥S) or default")
            }
            if parts[1].lowercased() == "default" { return .success(.hotKey(s, nil)) }
            let anyway = parts[1].lowercased().hasSuffix(" anyway")
            let keys = anyway ? String(parts[1].dropLast(" anyway".count)) : parts[1]
            guard let combo = HotKeyCombo.parse(keys) else { return bad("hot-key: unknown keys \(keys)") }
            return .success(.hotKey(s, combo, anyway: anyway))
        case "": return bad("action needs name=<action>")
        default: return bad("unknown action: \(name) (one of \(DebugAction.names.joined(separator: ", ")))")
        }
    }

    /// A section by its raw value, ignoring case and dashes ("desk-layout" is deskLayout).
    static func section(_ s: String) -> SettingsSection? {
        let key = s.lowercased().replacingOccurrences(of: "-", with: "")
        return SettingsSection.allCases.first { $0.rawValue.lowercased() == key }
    }

    /// A raw value or a retired one (SettingsSection.aliases), ignoring case and dashes, as the
    /// page and anchor it opens.
    static func page(_ s: String) -> (section: SettingsSection, anchor: String?)? {
        if let section = section(s) { return (section, nil) }
        let key = s.lowercased().replacingOccurrences(of: "-", with: "")
        guard let raw = SettingsSection.aliases.keys.first(where: { $0.lowercased() == key }) else { return nil }
        return SettingsSection.resolve(raw)
    }
}

import Carbon.HIToolbox
import Foundation

/// A global shortcut's keys: a virtual key code (the key's place on the keyboard, as Carbon's
/// RegisterEventHotKey takes it) and Carbon's modifier mask (cmdKey, shiftKey, optionKey,
/// controlKey). General, Shortcuts records one per shortcut (2026-10-08).
struct HotKeyCombo: Equatable, Hashable {
    var keyCode: UInt32
    var modifiers: UInt32

    static let command = UInt32(cmdKey)
    static let shift = UInt32(shiftKey)
    static let option = UInt32(optionKey)
    static let control = UInt32(controlKey)
    /// The modifiers a combo may carry, in the order macOS writes them: ⌃⌥⇧⌘.
    static let allModifiers = control | option | shift | command

    init(keyCode: UInt32, modifiers: UInt32) {
        self.keyCode = keyCode
        self.modifiers = modifiers & Self.allModifiers
    }

    /// At least one of ⌘, ⌃ or ⌥: Shift alone would take a capital letter from every app.
    var hasRequiredModifier: Bool { modifiers & (Self.command | Self.control | Self.option) != 0 }

    /// "⌃⌥S": the modifiers in macOS order, then the key.
    var display: String {
        var out = ""
        if modifiers & Self.control != 0 { out += "⌃" }
        if modifiers & Self.option != 0 { out += "⌥" }
        if modifiers & Self.shift != 0 { out += "⇧" }
        if modifiers & Self.command != 0 { out += "⌘" }
        return out + Self.keyName(keyCode)
    }

    // MARK: Key names

    /// The keys a shortcut can use, by the name shown for them (US layout positions).
    static let keyNames: [(code: Int, name: String)] = [
        (kVK_ANSI_A, "A"), (kVK_ANSI_B, "B"), (kVK_ANSI_C, "C"), (kVK_ANSI_D, "D"), (kVK_ANSI_E, "E"),
        (kVK_ANSI_F, "F"), (kVK_ANSI_G, "G"), (kVK_ANSI_H, "H"), (kVK_ANSI_I, "I"), (kVK_ANSI_J, "J"),
        (kVK_ANSI_K, "K"), (kVK_ANSI_L, "L"), (kVK_ANSI_M, "M"), (kVK_ANSI_N, "N"), (kVK_ANSI_O, "O"),
        (kVK_ANSI_P, "P"), (kVK_ANSI_Q, "Q"), (kVK_ANSI_R, "R"), (kVK_ANSI_S, "S"), (kVK_ANSI_T, "T"),
        (kVK_ANSI_U, "U"), (kVK_ANSI_V, "V"), (kVK_ANSI_W, "W"), (kVK_ANSI_X, "X"), (kVK_ANSI_Y, "Y"),
        (kVK_ANSI_Z, "Z"),
        (kVK_ANSI_0, "0"), (kVK_ANSI_1, "1"), (kVK_ANSI_2, "2"), (kVK_ANSI_3, "3"), (kVK_ANSI_4, "4"),
        (kVK_ANSI_5, "5"), (kVK_ANSI_6, "6"), (kVK_ANSI_7, "7"), (kVK_ANSI_8, "8"), (kVK_ANSI_9, "9"),
        (kVK_ANSI_Minus, "-"), (kVK_ANSI_Equal, "="), (kVK_ANSI_LeftBracket, "["), (kVK_ANSI_RightBracket, "]"),
        (kVK_ANSI_Semicolon, ";"), (kVK_ANSI_Quote, "'"), (kVK_ANSI_Comma, ","), (kVK_ANSI_Period, "."),
        (kVK_ANSI_Slash, "/"), (kVK_ANSI_Backslash, "\\"), (kVK_ANSI_Grave, "`"),
        (kVK_Space, "Space"), (kVK_Return, "↩"), (kVK_Tab, "⇥"), (kVK_Delete, "⌫"), (kVK_ForwardDelete, "⌦"),
        (kVK_LeftArrow, "←"), (kVK_RightArrow, "→"), (kVK_UpArrow, "↑"), (kVK_DownArrow, "↓"),
        (kVK_Home, "↖"), (kVK_End, "↘"), (kVK_PageUp, "⇞"), (kVK_PageDown, "⇟"),
        (kVK_F1, "F1"), (kVK_F2, "F2"), (kVK_F3, "F3"), (kVK_F4, "F4"), (kVK_F5, "F5"), (kVK_F6, "F6"),
        (kVK_F7, "F7"), (kVK_F8, "F8"), (kVK_F9, "F9"), (kVK_F10, "F10"), (kVK_F11, "F11"), (kVK_F12, "F12"),
    ]

    static func keyName(_ code: UInt32) -> String {
        keyNames.first { $0.code == Int(code) }?.name ?? "Key \(code)"
    }

    // MARK: Text

    /// "⌃⌥S", "ctrl-opt-s", "cmd+shift+F5" or "option j": the form state.yaml shows, or modifier
    /// words and a key name. Nil for an unknown key or modifier. Case is ignored.
    static func parse(_ text: String) -> HotKeyCombo? {
        let symbols: [Character: UInt32] = ["⌃": control, "⌥": option, "⇧": shift, "⌘": command]
        let words: [String: UInt32] = ["ctrl": control, "control": control, "opt": option, "option": option,
                                       "alt": option, "shift": shift, "cmd": command, "command": command]
        var mods: UInt32 = 0
        var rest = Substring(text.trimmingCharacters(in: .whitespaces))
        while let c = rest.first, let m = symbols[c] { mods |= m; rest = rest.dropFirst() }
        var parts = rest.split(whereSeparator: { " +-".contains($0) }).map(String.init)
        // "-" names the minus key when it is the last thing typed ("ctrl-opt--").
        if rest.hasSuffix("-"), rest.count > 1 || parts.isEmpty { parts.append("-") }
        guard let key = parts.popLast() else { return nil }
        for p in parts {
            guard let m = words[p.lowercased()] else { return nil }
            mods |= m
        }
        guard let code = keyNames.first(where: { $0.name.lowercased() == key.lowercased() })?.code else { return nil }
        return HotKeyCombo(keyCode: UInt32(code), modifiers: mods)
    }

    // MARK: Storage

    /// Two integers in the Desk suite, `<shortcut key>KeyCode` and `<shortcut key>Modifiers`
    /// (hotKeySettingsKeyCode, hotKeySettingsModifiers). Absent, or not a usable combo: nil, and
    /// the shortcut keeps its default, so an install from before this reads as it did.
    static func load(prefix: String, from d: DefaultsStore) -> HotKeyCombo? {
        guard let code = (d.object(forKey: prefix + "KeyCode") as? NSNumber)?.intValue,
              let mods = (d.object(forKey: prefix + "Modifiers") as? NSNumber)?.intValue,
              (0...0xFFFF).contains(code), mods >= 0 else { return nil }
        let combo = HotKeyCombo(keyCode: UInt32(code), modifiers: UInt32(mods))
        return combo.hasRequiredModifier ? combo : nil
    }

    /// Writes both integers, or removes both for nil (back to the default).
    static func store(_ combo: HotKeyCombo?, prefix: String, in d: DefaultsStore) {
        d.set(combo.map { Int($0.keyCode) }, forKey: prefix + "KeyCode")
        d.set(combo.map { Int($0.modifiers) }, forKey: prefix + "Modifiers")
    }
}

extension SanduhrHotKeys {
    /// Why a recorded combo was refused.
    enum Refusal: Equatable {
        /// Shift alone, or no modifier.
        case needsModifier
        /// The other Sanduhr shortcut already uses it.
        case taken(by: Shortcut)
        /// macOS has its own shortcut on these keys (SystemShortcuts, item 73); Use Anyway saves it.
        case macOS(name: String, combo: HotKeyCombo)
        /// Another app's menu has an item on these keys (AppMenuShortcuts, item 73 b); Use Anyway
        /// saves it.
        case appMenu(app: String, item: String, combo: HotKeyCombo)

        var note: String {
            switch self {
            case .needsModifier: "Add ⌘, ⌃ or ⌥: Shift alone isn't enough."
            case .taken(let other): "\(SanduhrHotKeys.combo(other).display) already \(other.action)."
            case .macOS(let name, let combo): "macOS uses \(combo.display) for \(Self.quoted(name)), so it would get there first."
            case .appMenu(let app, let item, let combo): SanduhrHotKeys.appNote(combo, app: app, item: item)
            }
        }

        /// Only the macOS and app menu refusals can be overridden (Use Anyway).
        var canInsist: Bool {
            switch self {
            case .macOS, .appMenu: true
            case .needsModifier, .taken: false
            }
        }

        /// The keys Use Anyway saves, nil when it isn't offered.
        var insistCombo: HotKeyCombo? {
            switch self {
            case .macOS(_, let combo), .appMenu(_, _, let combo): combo
            case .needsModifier, .taken: nil
            }
        }

        /// state.yaml's `hot_keys.<shortcut>_refusal`.
        var kind: String {
            switch self {
            case .needsModifier: "modifier"
            case .taken: "taken"
            case .macOS: "macos"
            case .appMenu: "app"
            }
        }

        /// "Show Spotlight search" in quotes; the generic "a macOS shortcut" as it is.
        fileprivate static func quoted(_ name: String) -> String {
            name == SystemShortcuts.unnamed ? "another of its shortcuts" : "“\(name)”"
        }
    }

    /// The note under a shortcut whose combo another app holds (registration failed).
    static func takenNote(_ combo: HotKeyCombo) -> String {
        "Another app already uses \(combo.display). Pick another."
    }

    /// The quiet note under a shortcut that is on while macOS has its own shortcut on the same
    /// keys (saved with Use Anyway, or turned on in System Settings later).
    static func clashNote(_ combo: HotKeyCombo, name: String) -> String {
        "macOS also uses \(combo.display) for \(Refusal.quoted(name)), so it may not reach Sanduhr."
    }

    /// The refusal's note for keys an app's menu uses (item 73 b): "Finder uses ⇧⌘N for “New
    /// Folder”, so it may not reach Sanduhr while Finder is in front."
    static func appNote(_ combo: HotKeyCombo, app: String, item: String) -> String {
        "\(app) uses \(combo.display) for “\(item)”, so it may not reach Sanduhr while \(app) is in front."
    }

    /// The quiet note under a shortcut that is on while an app's menu uses the same keys.
    static func appClashNote(_ combo: HotKeyCombo, app: String, item: String) -> String {
        "\(app) also uses \(combo.display) for “\(item)”, so it may not reach Sanduhr while \(app) is in front."
    }

    /// The combo saved for a shortcut, or its default.
    static func combo(_ s: Shortcut, in d: DefaultsStore = UserDefaults.desk) -> HotKeyCombo {
        HotKeyCombo.load(prefix: s.key, from: d) ?? s.defaultCombo
    }

    /// Whether `combo` may be saved for `s`: a ⌘, ⌃ or ⌥ in it, not the other shortcut's, and,
    /// unless `insist`, not one of macOS's own shortcuts in `system`, then not an item in another
    /// app's menu in `appMenus` (empty unless the user turned that check on).
    static func check(_ combo: HotKeyCombo, for s: Shortcut, in d: DefaultsStore = UserDefaults.desk,
                      system: SystemShortcuts = .none, appMenus: AppMenuShortcuts = .none,
                      insist: Bool = false) -> Refusal? {
        guard combo.hasRequiredModifier else { return .needsModifier }
        if let other = Shortcut.allCases.first(where: { $0 != s && Self.combo($0, in: d) == combo }) {
            return .taken(by: other)
        }
        if insist { return nil }
        if let name = system.collision(combo) { return .macOS(name: name, combo: combo) }
        if let hit = appMenus.collision(combo) { return .appMenu(app: hit.app, item: hit.item, combo: combo) }
        return nil
    }

    /// Saves `combo` for `s` when check(_:for:in:system:appMenus:insist:) allows it; the default
    /// is stored as no value. Returns the refusal, nil when saved.
    @discardableResult
    static func setCombo(_ combo: HotKeyCombo, for s: Shortcut, in d: DefaultsStore = UserDefaults.desk,
                         system: SystemShortcuts = .none, appMenus: AppMenuShortcuts = .none,
                         insist: Bool = false) -> Refusal? {
        if let refusal = check(combo, for: s, in: d, system: system, appMenus: appMenus, insist: insist) { return refusal }
        HotKeyCombo.store(combo == s.defaultCombo ? nil : combo, prefix: s.key, in: d)
        return nil
    }

    /// Back to the default (⌥S, ⌥J). Refused only when the other shortcut was moved onto it; the
    /// default is never refused for macOS's or another app's sake.
    @discardableResult
    static func reset(_ s: Shortcut, in d: DefaultsStore = UserDefaults.desk) -> Refusal? {
        setCombo(s.defaultCombo, for: s, in: d)
    }

    /// The caption under "Also check other apps' menus" (item 73 b).
    static let appMenusCaption = "Needs Accessibility, which Sanduhr asks for only when you click Allow. It then reads the "
        + "names of the menu items in the apps you have open and their shortcuts, nothing else, and no keystroke."

    /// General, Shortcuts' caption, following the combos: with the defaults it says which
    /// characters they take; otherwise only that the keys go to Sanduhr.
    static func caption(settings: HotKeyCombo, join: HotKeyCombo) -> String {
        let lead = "Both work in every app whenever Sanduhr runs, with or without the Desk."
        if settings == Shortcut.settings.defaultCombo, join == Shortcut.join.defaultCombo {
            return lead + " While one is on, ⌥S no longer types ß, or ⌥J ∆."
        }
        return lead + " While one is on, its keys go to Sanduhr instead of the app in front."
    }
}

/// Test It on General, Shortcuts (item 73): press the shortcut once and Sanduhr says whether it
/// arrived. While a test waits, the shortcut's own action is held back, so testing ⌥S doesn't
/// open Settings again. Only the shortcut's own registered keys are heard; no other keystroke is
/// read.
struct HotKeyProbe: Equatable {
    typealias Shortcut = SanduhrHotKeys.Shortcut

    enum Result: Equatable {
        case waiting, arrived, missed
    }

    /// How long a test waits for the keys before saying they didn't arrive.
    static let timeout: TimeInterval = 10

    private(set) var results: [Shortcut: Result] = [:]

    mutating func start(_ s: Shortcut) { results[s] = .waiting }

    /// The shortcut's keys arrived. True when a test was waiting for them: the test takes them and
    /// the shortcut's action is skipped.
    mutating func arrive(_ s: Shortcut) -> Bool {
        guard results[s] == .waiting else { return false }
        results[s] = .arrived
        return true
    }

    /// The wait ran out with nothing heard.
    mutating func expire(_ s: Shortcut) {
        if results[s] == .waiting { results[s] = .missed }
    }

    mutating func clear(_ s: Shortcut) { results[s] = nil }

    /// The line under the shortcut for a result.
    static func note(_ result: Result, combo: HotKeyCombo) -> String {
        switch result {
        case .waiting: "Press \(combo.display) now…"
        case .arrived: "\(combo.display) reached Sanduhr."
        case .missed: "\(combo.display) didn't reach Sanduhr in \(Int(timeout)) seconds: macOS or another app takes it first. Pick other keys."
        }
    }
}

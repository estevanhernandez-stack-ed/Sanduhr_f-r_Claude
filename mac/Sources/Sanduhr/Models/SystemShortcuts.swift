import Carbon.HIToolbox
import Foundation

/// macOS's own keyboard shortcuts (System Settings, Keyboard, Keyboard Shortcuts), so General,
/// Shortcuts can name the one a recorded combo collides with (item 73). Carbon's
/// RegisterEventHotKey accepts ⌘Space without error even while Spotlight holds it, and macOS
/// then gets the keystroke first.
///
/// Which combos are on comes from Carbon's CopySymbolicHotKeys (macOS's live list, its defaults
/// included); their names come from `com.apple.symbolichotkeys` (AppleSymbolicHotKeys: id →
/// enabled, parameters [character, key code, modifier flags]) and the id table below. Nothing
/// here needs a permission, and no keystroke is read.
struct SystemShortcuts: Equatable {
    /// The combos macOS has on now.
    var enabled: Set<HotKeyCombo> = []
    /// What macOS calls each one, where known.
    var names: [HotKeyCombo: String] = [:]

    static let none = SystemShortcuts()

    /// The name for a combo macOS has on that the id table doesn't know.
    static let unnamed = "a macOS shortcut"

    /// The macOS shortcut `combo` collides with, by name, or nil when macOS doesn't use it.
    func collision(_ combo: HotKeyCombo) -> String? {
        enabled.contains(combo) ? names[combo] ?? Self.unnamed : nil
    }

    // MARK: Reading

    /// macOS's shortcuts now: the live list, named from the preferences file.
    static func current() -> SystemShortcuts {
        let plist = CFPreferencesCopyAppValue("AppleSymbolicHotKeys" as CFString,
                                              "com.apple.symbolichotkeys" as CFString) as? [String: Any]
        return build(live: liveList(), plist: plist)
    }

    /// CopySymbolicHotKeys' dictionaries, nil when the call fails.
    private static func liveList() -> [[String: Any]]? {
        var out: Unmanaged<CFArray>?
        guard CopySymbolicHotKeys(&out) == noErr, let array = out?.takeRetainedValue() else { return nil }
        return array as? [[String: Any]]
    }

    /// The live list decides what is on; without it, the preferences file's enabled entries and
    /// the defaults it doesn't turn off.
    static func build(live: [[String: Any]]?, plist: [String: Any]?) -> SystemShortcuts {
        let entries = parse(plist)
        var names: [HotKeyCombo: String] = [:]
        for (id, d) in defaults {
            // Moved to other keys or turned off in System Settings: the default keys aren't its.
            if let e = entries.first(where: { $0.id == id }), !e.enabled || (e.combo.map { $0 != d.combo } ?? false) { continue }
            names[d.combo] = d.name
        }
        for e in entries where e.enabled {
            if let combo = e.combo, let name = idNames[e.id] { names[combo] = name }
        }
        let enabled: Set<HotKeyCombo>
        if let live {
            enabled = Set(live.compactMap(parseLive))
        } else {
            let turnedOff = Set(entries.filter { !$0.enabled }.map(\.id))
            var on = Set(defaults.filter { !offByDefault.contains($0.key) && !turnedOff.contains($0.key) }.map(\.value.combo))
            on.formUnion(entries.filter(\.enabled).compactMap(\.combo))
            enabled = on
        }
        return SystemShortcuts(enabled: enabled.filter(\.hasRequiredModifier), names: names)
    }

    /// One entry of `com.apple.symbolichotkeys`: its id, whether it is on, and its keys when it
    /// carries standard ones (a modifier-only entry, or a key code of 65535, has none).
    struct Entry: Equatable {
        let id: Int
        let enabled: Bool
        let combo: HotKeyCombo?
    }

    /// AppleSymbolicHotKeys' entries; ids that aren't numbers are skipped.
    static func parse(_ plist: [String: Any]?) -> [Entry] {
        guard let plist else { return [] }
        return plist.compactMap { key, raw -> Entry? in
            guard let id = Int(key), let d = raw as? [String: Any] else { return nil }
            let enabled = (d["enabled"] as? NSNumber)?.boolValue ?? false
            var combo: HotKeyCombo?
            if let value = d["value"] as? [String: Any], (value["type"] as? String ?? "standard") == "standard",
               let params = value["parameters"] as? [Any], params.count >= 3,
               let code = (params[1] as? NSNumber)?.intValue, let flags = (params[2] as? NSNumber)?.intValue,
               (0..<0xFFFF).contains(code) {
                combo = HotKeyCombo(keyCode: UInt32(code), modifiers: carbon(cocoaFlags: flags))
            }
            return Entry(id: id, enabled: enabled, combo: combo)
        }.sorted { $0.id < $1.id }
    }

    /// One of CopySymbolicHotKeys' dictionaries as a combo, nil when it is off. Its modifiers are
    /// Carbon's mask plus bits Sanduhr never records (fn), which HotKeyCombo drops.
    static func parseLive(_ d: [String: Any]) -> HotKeyCombo? {
        guard (d[kHISymbolicHotKeyEnabled] as? NSNumber)?.boolValue == true,
              let code = (d[kHISymbolicHotKeyCode] as? NSNumber)?.intValue,
              let mods = (d[kHISymbolicHotKeyModifiers] as? NSNumber)?.intValue,
              (0..<0xFFFF).contains(code), mods >= 0 else { return nil }
        return HotKeyCombo(keyCode: UInt32(code), modifiers: UInt32(truncatingIfNeeded: mods))
    }

    /// The preferences file's modifier flags (AppKit's device-independent ones) as Carbon's mask.
    static func carbon(cocoaFlags flags: Int) -> UInt32 {
        var m: UInt32 = 0
        if flags & 0x20000 != 0 { m |= HotKeyCombo.shift }
        if flags & 0x40000 != 0 { m |= HotKeyCombo.control }
        if flags & 0x80000 != 0 { m |= HotKeyCombo.option }
        if flags & 0x100000 != 0 { m |= HotKeyCombo.command }
        return m
    }

    // MARK: Names

    /// System Settings' names for the symbolic hotkey ids, as macOS words them.
    static let idNames: [Int: String] = {
        var n: [Int: String] = [
            7: "Move focus to the menu bar", 8: "Move focus to the Dock",
            9: "Move focus to active or next window", 10: "Move focus to window toolbar",
            11: "Move focus to floating window", 12: "Turn keyboard access on or off",
            13: "Change the way Tab moves focus", 15: "Turn zoom on or off", 17: "Zoom in", 19: "Zoom out",
            21: "Invert colors", 23: "Turn image smoothing on or off", 25: "Increase contrast",
            26: "Decrease contrast", 27: "Move focus to next window",
            28: "Save picture of screen as a file", 29: "Copy picture of screen to the clipboard",
            30: "Save picture of selected area as a file", 31: "Copy picture of selected area to the clipboard",
            32: "Mission Control", 33: "Application windows", 36: "Show Desktop",
            52: "Turn Dock hiding on or off", 57: "Move focus to status menus", 59: "Turn VoiceOver on or off",
            60: "Select the previous input source", 61: "Select next source in Input menu",
            64: "Show Spotlight search", 65: "Show Finder search window",
            79: "Move left a space", 81: "Move right a space", 162: "Show Accessibility controls",
            184: "Screenshot and recording options",
        ]
        for i in 0..<16 { n[118 + i] = "Switch to Desktop \(i + 1)" }
        return n
    }()

    /// Ids whose default keys macOS ships turned off (the Accessibility zoom and display ones).
    static let offByDefault: Set<Int> = [15, 17, 19, 21, 23, 25, 26]

    /// macOS's default keys for the well-known ids, so a fresh preferences file (which lists few
    /// of them) still names them.
    static let defaults: [Int: (name: String, combo: HotKeyCombo)] = {
        let c = HotKeyCombo.command, o = HotKeyCombo.option, s = HotKeyCombo.shift, k = HotKeyCombo.control
        let keys: [Int: (Int, UInt32)] = [
            7: (kVK_F2, k), 8: (kVK_F3, k), 9: (kVK_F4, k), 10: (kVK_F5, k), 11: (kVK_F6, k),
            12: (kVK_F1, k), 13: (kVK_F7, k), 57: (kVK_F8, k),
            15: (kVK_ANSI_8, o | c), 17: (kVK_ANSI_Equal, o | c), 19: (kVK_ANSI_Minus, o | c),
            21: (kVK_ANSI_8, k | o | c), 23: (kVK_ANSI_Backslash, o | c),
            25: (kVK_ANSI_Period, k | o | c), 26: (kVK_ANSI_Comma, k | o | c),
            27: (kVK_ANSI_Grave, c),
            28: (kVK_ANSI_3, s | c), 29: (kVK_ANSI_3, k | s | c),
            30: (kVK_ANSI_4, s | c), 31: (kVK_ANSI_4, k | s | c), 184: (kVK_ANSI_5, s | c),
            32: (kVK_UpArrow, k), 33: (kVK_DownArrow, k), 79: (kVK_LeftArrow, k), 81: (kVK_RightArrow, k),
            52: (kVK_ANSI_D, o | c), 59: (kVK_F5, c), 162: (kVK_F5, o | c),
            60: (kVK_Space, k), 61: (kVK_Space, k | o), 64: (kVK_Space, c), 65: (kVK_Space, o | c),
        ]
        return keys.reduce(into: [:]) { out, e in
            out[e.key] = (idNames[e.key] ?? unnamed, HotKeyCombo(keyCode: UInt32(e.value.0), modifiers: e.value.1))
        }
    }()
}

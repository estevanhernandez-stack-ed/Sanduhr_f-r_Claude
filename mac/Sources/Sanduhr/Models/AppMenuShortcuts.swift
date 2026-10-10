import Carbon.HIToolbox
import Foundation

/// Other apps' menu shortcuts (item 73, slice b), so General, Shortcuts can name the menu item a
/// recorded combo is also on: "Finder: New Folder". Read through Accessibility by AppMenuReader,
/// only when the user turns on "Also check other apps' menus" and has allowed Sanduhr; this part
/// is the pure mapping from what Accessibility reports to HotKeyCombo, and needs no permission.
struct AppMenuShortcuts: Equatable {
    /// One menu item with a key equivalent, as Accessibility reports it.
    struct Item: Equatable {
        /// The app's name ("Finder").
        let app: String
        /// The menu item's title ("New Folder").
        let title: String
        /// kAXMenuItemCmdCharAttribute: the key's character ("N", "n", ","), nil or empty when none.
        var char: String?
        /// kAXMenuItemCmdVirtualKeyAttribute: the key's virtual key code, when the item has one.
        var virtualKey: Int?
        /// kAXMenuItemCmdGlyphAttribute: Carbon's menu glyph for keys without a character (arrows,
        /// F-keys, Return), when the item has one.
        var glyph: Int?
        /// kAXMenuItemCmdModifiersAttribute: 0 is ⌘ alone; bit 0 adds ⇧, bit 1 ⌥, bit 2 ⌃, and
        /// bit 3 takes ⌘ away.
        var modifiers: Int = 0
    }

    /// A combo an app's menu uses: which app and which item.
    struct Collision: Equatable {
        let app: String
        let item: String

        /// "Finder: New Folder", as state.yaml and the clash note name it.
        var name: String { "\(app): \(item)" }
    }

    /// Each combo with a modifier Sanduhr accepts and the items using it, in the order read (the
    /// frontmost app first).
    private(set) var entries: [(combo: HotKeyCombo, collision: Collision)] = []

    static let none = AppMenuShortcuts()

    static func == (a: AppMenuShortcuts, b: AppMenuShortcuts) -> Bool {
        a.entries.map(\.combo) == b.entries.map(\.combo) && a.entries.map(\.collision) == b.entries.map(\.collision)
    }

    var isEmpty: Bool { entries.isEmpty }

    /// The first menu item using `combo`, nil when no app's menu does.
    func collision(_ combo: HotKeyCombo) -> Collision? {
        entries.first { $0.combo == combo }?.collision
    }

    /// The items with a key equivalent Sanduhr could record; items without one, or whose keys
    /// carry no ⌘, ⌃ or ⌥, are skipped.
    static func build(_ items: [Item]) -> AppMenuShortcuts {
        var out = AppMenuShortcuts()
        for item in items {
            guard let combo = combo(item), combo.hasRequiredModifier else { continue }
            let title = item.title.trimmingCharacters(in: .whitespaces)
            guard !title.isEmpty else { continue }
            out.entries.append((combo, Collision(app: item.app, item: title)))
        }
        return out
    }

    /// An item's keys as a combo, nil when it has no key equivalent this can name.
    static func combo(_ item: Item) -> HotKeyCombo? {
        guard let code = keyCode(item) else { return nil }
        return HotKeyCombo(keyCode: code, modifiers: carbon(axModifiers: item.modifiers))
    }

    /// The key: the virtual key when the item has one, else its character, else its glyph. A
    /// virtual key of 0 (A's place) is AppKit's "none" unless the character is A too.
    static func keyCode(_ item: Item) -> UInt32? {
        let fromChar = item.char.flatMap(keyCode(char:))
        if let v = item.virtualKey, (1..<0xFFFF).contains(v) || (v == 0 && fromChar == 0) {
            return UInt32(v)
        }
        if let fromChar { return fromChar }
        return item.glyph.flatMap { glyphs[$0] }.map(UInt32.init)
    }

    /// Accessibility's modifier mask as Carbon's.
    static func carbon(axModifiers m: Int) -> UInt32 {
        var out: UInt32 = 0
        if m & 0b1000 == 0 { out |= HotKeyCombo.command }
        if m & 0b0001 != 0 { out |= HotKeyCombo.shift }
        if m & 0b0010 != 0 { out |= HotKeyCombo.option }
        if m & 0b0100 != 0 { out |= HotKeyCombo.control }
        return out
    }

    /// A key equivalent's character as a key code: the US-layout names HotKeyCombo shows (case
    /// ignored), Space, Return, Tab and the deletes, and AppKit's function-key characters (arrows,
    /// F1 to F12, Home, End, Page Up and Down). Nil for anything else.
    static func keyCode(char: String) -> UInt32? {
        guard char.unicodeScalars.count == 1, let scalar = char.unicodeScalars.first else {
            return char.isEmpty ? nil : named(char)
        }
        if let code = specialChars[scalar.value] { return UInt32(code) }
        return named(char)
    }

    private static func named(_ name: String) -> UInt32? {
        HotKeyCombo.keyNames.first { $0.name.lowercased() == name.lowercased() }.map { UInt32($0.code) }
    }

    /// Characters that aren't a key's name: whitespace and control characters, and AppKit's
    /// NSUpArrowFunctionKey and the rest.
    private static let specialChars: [UInt32: Int] = [
        0x20: kVK_Space, 0x0D: kVK_Return, 0x09: kVK_Tab, 0x08: kVK_Delete, 0x7F: kVK_Delete,
        0xF700: kVK_UpArrow, 0xF701: kVK_DownArrow, 0xF702: kVK_LeftArrow, 0xF703: kVK_RightArrow,
        0xF704: kVK_F1, 0xF705: kVK_F2, 0xF706: kVK_F3, 0xF707: kVK_F4, 0xF708: kVK_F5, 0xF709: kVK_F6,
        0xF70A: kVK_F7, 0xF70B: kVK_F8, 0xF70C: kVK_F9, 0xF70D: kVK_F10, 0xF70E: kVK_F11, 0xF70F: kVK_F12,
        0xF728: kVK_ForwardDelete, 0xF729: kVK_Home, 0xF72B: kVK_End, 0xF72C: kVK_PageUp, 0xF72D: kVK_PageDown,
    ]

    /// Carbon's menu glyphs (Menus.h, kMenuReturnGlyph and the rest) for the keys HotKeyCombo names.
    static let glyphs: [Int: Int] = [
        0x02: kVK_Tab, 0x0B: kVK_Return, 0x09: kVK_Space, 0x17: kVK_Delete, 0x0A: kVK_ForwardDelete,
        0x64: kVK_LeftArrow, 0x65: kVK_RightArrow, 0x68: kVK_UpArrow, 0x6A: kVK_DownArrow,
        0x66: kVK_Home, 0x69: kVK_End, 0x62: kVK_PageUp, 0x6B: kVK_PageDown,
        0x6F: kVK_F1, 0x70: kVK_F2, 0x71: kVK_F3, 0x72: kVK_F4, 0x73: kVK_F5, 0x74: kVK_F6,
        0x75: kVK_F7, 0x76: kVK_F8, 0x77: kVK_F9, 0x78: kVK_F10, 0x79: kVK_F11, 0x7A: kVK_F12,
    ]
}

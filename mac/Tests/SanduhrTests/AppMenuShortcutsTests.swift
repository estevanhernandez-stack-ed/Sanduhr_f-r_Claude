import Carbon.HIToolbox
import Foundation
import Testing
@testable import Sanduhr

/// Item 73, slice b: other apps' menu shortcuts (AppMenuShortcuts), as Accessibility reports them,
/// mapped to combos and named in a refusal or the quiet clash note. Fixtures only: nothing here
/// reads another app's menus or asks for Accessibility.
@Suite("App menu shortcuts")
struct AppMenuShortcutsTests {
    private func scratch() -> UserDefaults {
        let suite = "sanduhr.tests.appmenus.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    typealias Item = AppMenuShortcuts.Item
    let c = HotKeyCombo.command, s = HotKeyCombo.shift, o = HotKeyCombo.option, k = HotKeyCombo.control
    var shiftCmdN: HotKeyCombo { HotKeyCombo(keyCode: UInt32(kVK_ANSI_N), modifiers: s | c) }

    @Test func everyModifierBitDecodes() {
        // 0 is ⌘ alone; bit 0 ⇧, bit 1 ⌥, bit 2 ⌃; bit 3 takes ⌘ away.
        for m in 0..<16 {
            var want: UInt32 = m & 8 == 0 ? c : 0
            if m & 1 != 0 { want |= s }
            if m & 2 != 0 { want |= o }
            if m & 4 != 0 { want |= k }
            #expect(AppMenuShortcuts.carbon(axModifiers: m) == want, "\(m)")
        }
        #expect(AppMenuShortcuts.carbon(axModifiers: 0) == c)
        #expect(AppMenuShortcuts.carbon(axModifiers: 1) == s | c)
        #expect(AppMenuShortcuts.carbon(axModifiers: 2) == o | c)
        #expect(AppMenuShortcuts.carbon(axModifiers: 4) == k | c)
        #expect(AppMenuShortcuts.carbon(axModifiers: 8) == 0)
        #expect(AppMenuShortcuts.carbon(axModifiers: 0b1110) == k | o)
        #expect(AppMenuShortcuts.carbon(axModifiers: 0b0111) == HotKeyCombo.allModifiers)
    }

    @Test func charactersMapToUSLayoutKeys() {
        #expect(AppMenuShortcuts.keyCode(char: "N") == UInt32(kVK_ANSI_N))
        #expect(AppMenuShortcuts.keyCode(char: "n") == UInt32(kVK_ANSI_N))
        #expect(AppMenuShortcuts.keyCode(char: "a") == UInt32(kVK_ANSI_A))
        #expect(AppMenuShortcuts.keyCode(char: "5") == UInt32(kVK_ANSI_5))
        #expect(AppMenuShortcuts.keyCode(char: ",") == UInt32(kVK_ANSI_Comma))
        #expect(AppMenuShortcuts.keyCode(char: "/") == UInt32(kVK_ANSI_Slash))
        #expect(AppMenuShortcuts.keyCode(char: "`") == UInt32(kVK_ANSI_Grave))
        #expect(AppMenuShortcuts.keyCode(char: " ") == UInt32(kVK_Space))
        #expect(AppMenuShortcuts.keyCode(char: "\r") == UInt32(kVK_Return))
        #expect(AppMenuShortcuts.keyCode(char: "\t") == UInt32(kVK_Tab))
        #expect(AppMenuShortcuts.keyCode(char: "\u{7F}") == UInt32(kVK_Delete))
        #expect(AppMenuShortcuts.keyCode(char: "\u{F700}") == UInt32(kVK_UpArrow))
        #expect(AppMenuShortcuts.keyCode(char: "\u{F703}") == UInt32(kVK_RightArrow))
        #expect(AppMenuShortcuts.keyCode(char: "\u{F704}") == UInt32(kVK_F1))
        #expect(AppMenuShortcuts.keyCode(char: "\u{F70F}") == UInt32(kVK_F12))
        #expect(AppMenuShortcuts.keyCode(char: "\u{F72C}") == UInt32(kVK_PageUp))
        #expect(AppMenuShortcuts.keyCode(char: "F5") == UInt32(kVK_F5))
        #expect(AppMenuShortcuts.keyCode(char: "ß") == nil)
        #expect(AppMenuShortcuts.keyCode(char: "") == nil)
    }

    @Test func theVirtualKeyWinsThenTheCharacterThenTheGlyph() {
        // A virtual key beats the character (a layout that puts another letter there).
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", char: "Z", virtualKey: kVK_ANSI_Y)) == UInt32(kVK_ANSI_Y))
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", virtualKey: kVK_F5)) == UInt32(kVK_F5))
        // 0 beside another letter, or alone, is AppKit's "none"; beside A it is A.
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", char: "N", virtualKey: 0)) == UInt32(kVK_ANSI_N))
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", virtualKey: 0)) == nil)
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", char: "a", virtualKey: 0)) == UInt32(kVK_ANSI_A))
        // No character: the glyph.
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", glyph: 0x68)) == UInt32(kVK_UpArrow))
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", glyph: 0x0B)) == UInt32(kVK_Return))
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", glyph: 0x6F)) == UInt32(kVK_F1))
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T", glyph: 0x1B)) == nil)
        #expect(AppMenuShortcuts.keyCode(Item(app: "A", title: "T")) == nil)
    }

    @Test func buildsCombosAndSkipsItemsWithoutUsableKeys() {
        let menus = AppMenuShortcuts.build([
            Item(app: "Finder", title: "New Folder", char: "N", modifiers: 1),
            Item(app: "Finder", title: "About Finder"),                              // no key equivalent
            Item(app: "Finder", title: "Rename", char: "\r", modifiers: 8),          // no ⌘, ⌃ or ⌥
            Item(app: "Finder", title: "Shift only", char: "K", modifiers: 9),       // ⇧ alone
            Item(app: "Finder", title: "  ", char: "J", modifiers: 0),               // no title
            Item(app: "Safari", title: "Show Sidebar", char: "s", modifiers: 1 | 4 | 8 /* ⌃⇧ */ ),
            Item(app: "Safari", title: "Zoom In", char: "=", modifiers: 2),
        ])
        #expect(menus.entries.map(\.combo.display) == ["⇧⌘N", "⌃⇧S", "⌥⌘="])
        #expect(menus.entries.map(\.collision.name) == ["Finder: New Folder", "Safari: Show Sidebar", "Safari: Zoom In"])
        #expect(AppMenuShortcuts.build([]).isEmpty && AppMenuShortcuts.none.isEmpty)
    }

    @Test func theFirstCollisionIsNamed() {
        // The frontmost app is read first, so its item names the collision.
        let menus = AppMenuShortcuts.build([
            Item(app: "Finder", title: "New Folder", char: "n", modifiers: 1),
            Item(app: "Mail", title: "New Mailbox", char: "N", modifiers: 1),
        ])
        #expect(menus.collision(shiftCmdN) == AppMenuShortcuts.Collision(app: "Finder", item: "New Folder"))
        #expect(menus.collision(shiftCmdN)?.name == "Finder: New Folder")
        #expect(menus.collision(HotKeyCombo(keyCode: UInt32(kVK_ANSI_N), modifiers: c)) == nil)
        #expect(AppMenuShortcuts.none.collision(shiftCmdN) == nil)
    }

    @Test func refusesAppMenuKeysAfterMacOSUnlessInsisted() {
        let d = scratch()
        let menus = AppMenuShortcuts.build([
            Item(app: "Finder", title: "New Folder", char: "N", modifiers: 1),
            Item(app: "Finder", title: "Spotlight-ish", char: " ", modifiers: 0),
        ])
        let refusal = SanduhrHotKeys.setCombo(shiftCmdN, for: .settings, in: d, appMenus: menus)
        #expect(refusal == .appMenu(app: "Finder", item: "New Folder", combo: shiftCmdN))
        #expect(refusal?.note == "Finder uses ⇧⌘N for “New Folder”, so it may not reach Sanduhr while Finder is in front.")
        #expect(refusal?.canInsist == true && refusal?.kind == "app" && refusal?.insistCombo == shiftCmdN)
        #expect(d.object(forKey: "hotKeySettingsKeyCode") == nil)
        // macOS is named first when both use the keys.
        let cmdSpace = HotKeyCombo(keyCode: UInt32(kVK_Space), modifiers: c)
        let system = SystemShortcuts.build(live: [[kHISymbolicHotKeyCode: NSNumber(value: 49), kHISymbolicHotKeyModifiers: NSNumber(value: 256),
                                                    kHISymbolicHotKeyEnabled: NSNumber(value: true)]], plist: nil)
        #expect(SanduhrHotKeys.check(cmdSpace, for: .settings, in: d, system: system, appMenus: menus)
                == .macOS(name: "Show Spotlight search", combo: cmdSpace))
        // The hard rules still come first; without the menus nothing is refused.
        #expect(SanduhrHotKeys.check(SanduhrHotKeys.Shortcut.join.defaultCombo, for: .settings, in: d, appMenus: menus) == .taken(by: .join))
        #expect(SanduhrHotKeys.check(shiftCmdN, for: .settings, in: d) == nil)
        // Use Anyway saves it.
        #expect(SanduhrHotKeys.setCombo(shiftCmdN, for: .settings, in: d, appMenus: menus, insist: true) == nil)
        #expect(SanduhrHotKeys.combo(.settings, in: d) == shiftCmdN)
        #expect(SanduhrHotKeys.Refusal.needsModifier.insistCombo == nil)
    }

    @Test func notesNameTheAppAndItem() {
        #expect(SanduhrHotKeys.appClashNote(shiftCmdN, app: "Finder", item: "New Folder")
                == "Finder also uses ⇧⌘N for “New Folder”, so it may not reach Sanduhr while Finder is in front.")
        #expect(SanduhrHotKeys.appMenusCaption.contains("Accessibility"))
        #expect(SanduhrHotKeys.appMenusCaption.contains("nothing else"))
    }

    @Test func theSwitchIsOffByDefault() {
        let d = scratch()
        #expect(AppMenuReader.isOn(in: d) == false)
        d.set(true, forKey: AppMenuReader.switchKey)
        #expect(AppMenuReader.isOn(in: d))
        #expect(AppMenuReader.switchKey == "hotKeyCheckAppMenus")
        #expect(SettingsNames.table.contains { $0.name == SettingsNames.checkAppMenus && $0.page == .general })
        #expect(SettingsNames.anchors[SettingsNames.checkAppMenus] == SettingsAnchor.shortcuts)
        #expect(SettingsSearch.hits("Accessibility").first?.entry.anchor == SettingsAnchor.shortcuts)
    }
}

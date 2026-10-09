import Carbon.HIToolbox
import Foundation
import Testing
@testable import Sanduhr

/// General, Shortcuts' recorder (2026-10-08): the keys stored, shown and checked. Every test uses
/// a throwaway defaults suite, never the real one.
@Suite("Shortcut keys")
struct HotKeyComboTests {
    private func scratch() -> UserDefaults {
        let suite = "sanduhr.tests.hotkeys.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    private let ctrlOpt = HotKeyCombo.control | HotKeyCombo.option
    private var ctrlOptS: HotKeyCombo { HotKeyCombo(keyCode: UInt32(kVK_ANSI_S), modifiers: ctrlOpt) }

    @Test func storedAsTwoIntegersAndReadBack() {
        let d = scratch()
        #expect(HotKeyCombo.load(prefix: "hotKeySettings", from: d) == nil)
        HotKeyCombo.store(ctrlOptS, prefix: "hotKeySettings", in: d)
        #expect(d.integer(forKey: "hotKeySettingsKeyCode") == kVK_ANSI_S)
        #expect(d.integer(forKey: "hotKeySettingsModifiers") == Int(ctrlOpt))
        #expect(HotKeyCombo.load(prefix: "hotKeySettings", from: d) == ctrlOptS)
        HotKeyCombo.store(nil, prefix: "hotKeySettings", in: d)
        #expect(d.object(forKey: "hotKeySettingsKeyCode") == nil && d.object(forKey: "hotKeySettingsModifiers") == nil)
    }

    @Test func absentOrUnusableReadsAsTheDefault() {
        let d = scratch()
        #expect(SanduhrHotKeys.combo(.settings, in: d).display == "⌥S")
        #expect(SanduhrHotKeys.combo(.join, in: d).display == "⌥J")
        d.set(kVK_ANSI_K, forKey: "hotKeyJoinKeyCode")   // half a combo
        #expect(SanduhrHotKeys.combo(.join, in: d).display == "⌥J")
        d.set(Int(HotKeyCombo.shift), forKey: "hotKeyJoinModifiers")   // Shift alone
        #expect(SanduhrHotKeys.combo(.join, in: d).display == "⌥J")
        d.set(Int(HotKeyCombo.command | HotKeyCombo.shift), forKey: "hotKeyJoinModifiers")
        #expect(SanduhrHotKeys.combo(.join, in: d).display == "⇧⌘K")
    }

    @Test func displayInMacOSOrder() {
        let all = HotKeyCombo.allModifiers
        #expect(HotKeyCombo(keyCode: UInt32(kVK_ANSI_J), modifiers: all).display == "⌃⌥⇧⌘J")
        #expect(HotKeyCombo(keyCode: UInt32(kVK_F5), modifiers: HotKeyCombo.command).display == "⌘F5")
        #expect(HotKeyCombo(keyCode: UInt32(kVK_Space), modifiers: HotKeyCombo.control).display == "⌃Space")
        #expect(HotKeyCombo(keyCode: UInt32(kVK_ANSI_Minus), modifiers: ctrlOpt).display == "⌃⌥-")
        #expect(HotKeyCombo(keyCode: 999, modifiers: ctrlOpt).display == "⌃⌥Key 999")
    }

    @Test func parsesSymbolsAndWords() {
        #expect(HotKeyCombo.parse("⌃⌥S") == ctrlOptS)
        #expect(HotKeyCombo.parse("ctrl-opt-s") == ctrlOptS)
        #expect(HotKeyCombo.parse("Control+Option+S") == ctrlOptS)
        #expect(HotKeyCombo.parse("cmd shift f5")?.display == "⇧⌘F5")
        #expect(HotKeyCombo.parse("⌃⌥-")?.display == "⌃⌥-")
        #expect(HotKeyCombo.parse("ctrl-opt--")?.display == "⌃⌥-")
        #expect(HotKeyCombo.parse("hyper-s") == nil)
        #expect(HotKeyCombo.parse("ctrl-ß") == nil)
        #expect(HotKeyCombo.parse("") == nil)
        for c in [ctrlOptS, HotKeyCombo(keyCode: UInt32(kVK_ANSI_Slash), modifiers: HotKeyCombo.allModifiers)] {
            #expect(HotKeyCombo.parse(c.display) == c)
        }
    }

    @Test func needsCommandControlOrOption() {
        let d = scratch()
        let shiftS = HotKeyCombo(keyCode: UInt32(kVK_ANSI_S), modifiers: HotKeyCombo.shift)
        #expect(SanduhrHotKeys.check(shiftS, for: .settings, in: d) == .needsModifier)
        #expect(SanduhrHotKeys.check(HotKeyCombo(keyCode: UInt32(kVK_ANSI_S), modifiers: 0), for: .settings, in: d) == .needsModifier)
        for m in [HotKeyCombo.command, HotKeyCombo.control, HotKeyCombo.option] {
            #expect(SanduhrHotKeys.check(HotKeyCombo(keyCode: UInt32(kVK_ANSI_K), modifiers: m | HotKeyCombo.shift), for: .settings, in: d) == nil)
        }
        #expect(SanduhrHotKeys.setCombo(shiftS, for: .settings, in: d) == .needsModifier)
        #expect(d.object(forKey: "hotKeySettingsKeyCode") == nil)
        #expect(SanduhrHotKeys.Refusal.needsModifier.note == "Add ⌘, ⌃ or ⌥: Shift alone isn't enough.")
    }

    @Test func refusesTheOtherShortcutsKeys() {
        let d = scratch()
        let optJ = SanduhrHotKeys.Shortcut.join.defaultCombo
        #expect(SanduhrHotKeys.setCombo(optJ, for: .settings, in: d) == .taken(by: .join))
        #expect(SanduhrHotKeys.Refusal.taken(by: .join).note.hasSuffix("already joins the next meeting."))
        // Moved off ⌥J, it is free for Settings; Join can't go back to it until Settings moves.
        #expect(SanduhrHotKeys.setCombo(ctrlOptS, for: .join, in: d) == nil)
        #expect(SanduhrHotKeys.setCombo(optJ, for: .settings, in: d) == nil)
        #expect(SanduhrHotKeys.reset(.join, in: d) == .taken(by: .settings))
        #expect(SanduhrHotKeys.combo(.join, in: d) == ctrlOptS)
        // Its own keys again are fine.
        #expect(SanduhrHotKeys.check(ctrlOptS, for: .join, in: d) == nil)
    }

    @Test func theDefaultIsStoredAsNothing() {
        let d = scratch()
        SanduhrHotKeys.setCombo(ctrlOptS, for: .settings, in: d)
        #expect(SanduhrHotKeys.combo(.settings, in: d) == ctrlOptS)
        #expect(SanduhrHotKeys.reset(.settings, in: d) == nil)
        #expect(d.object(forKey: "hotKeySettingsKeyCode") == nil && d.object(forKey: "hotKeySettingsModifiers") == nil)
        #expect(SanduhrHotKeys.combo(.settings, in: d).display == "⌥S")
    }

    @Test func captionFollowsTheKeys() {
        let s = SanduhrHotKeys.Shortcut.settings.defaultCombo, j = SanduhrHotKeys.Shortcut.join.defaultCombo
        #expect(SanduhrHotKeys.caption(settings: s, join: j).hasSuffix("⌥S no longer types ß, or ⌥J ∆."))
        #expect(!SanduhrHotKeys.caption(settings: ctrlOptS, join: j).contains("ß"))
        #expect(SanduhrHotKeys.takenNote(ctrlOptS) == "Another app already uses ⌃⌥S. Pick another.")
    }

    @Test func recorderReadsAppKitModifiers() {
        #expect(ShortcutRecorderModel.carbon([.control, .option]) == ctrlOpt)
        #expect(ShortcutRecorderModel.carbon([.command, .shift, .capsLock]) == HotKeyCombo.command | HotKeyCombo.shift)
    }

    @Test func theSmokeActionParses() {
        #expect((try? DebugLink.action("hot-key", arg: "settings ctrl-opt-s").get()) == .hotKey(.settings, ctrlOptS))
        #expect((try? DebugLink.action("hot-key", arg: "join default").get()) == .hotKey(.join, nil))
        #expect((try? DebugLink.action("hot-key", arg: "settings cmd-space anyway").get())
                == .hotKey(.settings, HotKeyCombo(keyCode: UInt32(kVK_Space), modifiers: HotKeyCombo.command), anyway: true))
        #expect((try? DebugLink.action("hot-key", arg: "settings").get()) == nil)
        #expect((try? DebugLink.action("hot-key", arg: "settings nope-q").get()) == nil)
        #expect(DebugAction.names.contains("hot-key"))
    }
}

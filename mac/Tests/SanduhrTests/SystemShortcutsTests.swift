import Carbon.HIToolbox
import Foundation
import Testing
@testable import Sanduhr

/// Item 73: General, Shortcuts knows macOS's own shortcuts (SystemShortcuts), names the one a
/// recorded combo collides with and refuses it unless Use Anyway; Test It (HotKeyProbe) says
/// whether the keys arrived. Fixtures only: nothing here reads this Mac's shortcuts.
@Suite("Shortcut awareness")
struct SystemShortcutsTests {
    private func scratch() -> UserDefaults {
        let suite = "sanduhr.tests.syshotkeys.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    let cmdSpace = HotKeyCombo(keyCode: UInt32(kVK_Space), modifiers: HotKeyCombo.command)
    let ctrlUp = HotKeyCombo(keyCode: UInt32(kVK_UpArrow), modifiers: HotKeyCombo.control)
    let shiftCmd3 = HotKeyCombo(keyCode: UInt32(kVK_ANSI_3), modifiers: HotKeyCombo.shift | HotKeyCombo.command)
    let ctrlOptS = HotKeyCombo(keyCode: UInt32(kVK_ANSI_S), modifiers: HotKeyCombo.control | HotKeyCombo.option)

    /// A `com.apple.symbolichotkeys` entry as the file stores it.
    func entry(_ enabled: Bool, char: Int = 65535, code: Int, flags: Int, type: String = "standard") -> [String: Any] {
        ["enabled": NSNumber(value: enabled),
         "value": ["parameters": [NSNumber(value: char), NSNumber(value: code), NSNumber(value: flags)], "type": type]]
    }

    /// A CopySymbolicHotKeys dictionary.
    func live(_ code: Int, _ mods: Int, _ enabled: Bool = true) -> [String: Any] {
        [kHISymbolicHotKeyCode: NSNumber(value: code), kHISymbolicHotKeyModifiers: NSNumber(value: mods),
         kHISymbolicHotKeyEnabled: NSNumber(value: enabled)]
    }

    @Test func parsesThePreferencesFile() {
        let plist: [String: Any] = [
            "64": entry(true, char: 32, code: 49, flags: 1_048_576),            // ⌘Space
            "32": entry(true, code: 126, flags: 8_650_752),                     // ⌃↑ with the fn bit
            "28": entry(false, char: 51, code: 20, flags: 1_179_648),           // ⇧⌘3, off
            "15": ["enabled": NSNumber(value: false)],                          // off, no keys
            "164": entry(true, code: 0, flags: 1_048_584, type: "modifier"),    // a modifier entry
            "73": entry(true, code: 65535, flags: 0),                           // no key
            "junk": entry(true, code: 1, flags: 0),
        ]
        let entries = SystemShortcuts.parse(plist)
        #expect(entries.map(\.id) == [15, 28, 32, 64, 73, 164])
        #expect(entries.first { $0.id == 64 } == .init(id: 64, enabled: true, combo: cmdSpace))
        #expect(entries.first { $0.id == 32 }?.combo == ctrlUp)
        #expect(entries.first { $0.id == 28 } == .init(id: 28, enabled: false, combo: shiftCmd3))
        #expect(entries.first { $0.id == 15 }?.combo == nil)
        #expect(entries.first { $0.id == 164 }?.combo == nil)
        #expect(entries.first { $0.id == 73 }?.combo == nil)
        #expect(SystemShortcuts.parse(nil).isEmpty)
    }

    @Test func cocoaFlagsBecomeCarbonModifiers() {
        #expect(SystemShortcuts.carbon(cocoaFlags: 0x100000) == HotKeyCombo.command)
        #expect(SystemShortcuts.carbon(cocoaFlags: 0x20000 | 0x40000 | 0x80000) == HotKeyCombo.shift | HotKeyCombo.control | HotKeyCombo.option)
        #expect(SystemShortcuts.carbon(cocoaFlags: 0x800000) == 0)   // fn alone
    }

    @Test func theLiveListDecidesWhatIsOn() {
        // ⌘Space off (Spotlight moved to another launcher), ⌃↑ on with Carbon's fn bit, ⌥⌘Space on.
        let list = [live(49, 256, false), live(126, 135_168), live(49, 2304), live(103, 131_072)]
        let s = SystemShortcuts.build(live: list, plist: nil)
        #expect(s.collision(cmdSpace) == nil)
        #expect(s.collision(ctrlUp) == "Mission Control")
        #expect(s.collision(HotKeyCombo(keyCode: UInt32(kVK_Space), modifiers: HotKeyCombo.option | HotKeyCombo.command)) == "Show Finder search window")
        // F11 with fn only needs no ⌘, ⌃ or ⌥, so a Sanduhr combo can never be it.
        #expect(!s.enabled.contains { $0.keyCode == 103 })
        #expect(s.collision(ctrlOptS) == nil)
    }

    @Test func namesComeFromTheFileThenTheDefaults() {
        // The user put Spotlight on ⌃⌥S: the file names it; its default ⌘Space loses the name.
        let plist: [String: Any] = ["64": entry(true, char: 115, code: kVK_ANSI_S, flags: 0x40000 | 0x80000)]
        let s = SystemShortcuts.build(live: [live(kVK_ANSI_S, 4096 | 2048), live(49, 256)], plist: plist)
        #expect(s.collision(ctrlOptS) == "Show Spotlight search")
        #expect(s.collision(cmdSpace) == SystemShortcuts.unnamed)
        // An app-defined or unknown entry on: on, unnamed.
        let other = HotKeyCombo(keyCode: UInt32(kVK_ANSI_K), modifiers: HotKeyCombo.control | HotKeyCombo.command)
        #expect(SystemShortcuts.build(live: [live(kVK_ANSI_K, 4352)], plist: nil).collision(other) == SystemShortcuts.unnamed)
    }

    @Test func withoutTheLiveListTheFileAndDefaultsDecide() {
        let plist: [String: Any] = ["64": entry(false, char: 32, code: 49, flags: 1_048_576)]
        let s = SystemShortcuts.build(live: nil, plist: plist)
        #expect(s.collision(cmdSpace) == nil)                  // turned off in the file
        #expect(s.collision(ctrlUp) == "Mission Control")      // a default the file doesn't mention
        #expect(s.collision(shiftCmd3) == "Save picture of screen as a file")
        // Zoom's shortcuts ship off.
        let zoomIn = HotKeyCombo(keyCode: UInt32(kVK_ANSI_Equal), modifiers: HotKeyCombo.option | HotKeyCombo.command)
        #expect(s.collision(zoomIn) == nil)
    }

    @Test func refusesMacOSShortcutsUnlessInsisted() {
        let d = scratch()
        let system = SystemShortcuts.build(live: [live(49, 256), live(126, 135_168)], plist: nil)
        let refusal = SanduhrHotKeys.setCombo(cmdSpace, for: .settings, in: d, system: system)
        #expect(refusal == .macOS(name: "Show Spotlight search", combo: cmdSpace))
        #expect(refusal?.note == "macOS uses ⌘Space for “Show Spotlight search”, so it would get there first.")
        #expect(refusal?.canInsist == true && refusal?.kind == "macos")
        #expect(d.object(forKey: "hotKeySettingsKeyCode") == nil)
        #expect(SanduhrHotKeys.check(ctrlUp, for: .join, in: d, system: system) == .macOS(name: "Mission Control", combo: ctrlUp))
        // Use Anyway saves it.
        #expect(SanduhrHotKeys.setCombo(cmdSpace, for: .settings, in: d, system: system, insist: true) == nil)
        #expect(SanduhrHotKeys.combo(.settings, in: d) == cmdSpace)
        // Insisting never gets past the hard rules.
        let shiftS = HotKeyCombo(keyCode: UInt32(kVK_ANSI_S), modifiers: HotKeyCombo.shift)
        #expect(SanduhrHotKeys.check(shiftS, for: .join, in: d, system: system, insist: true) == .needsModifier)
        #expect(SanduhrHotKeys.check(cmdSpace, for: .join, in: d, system: system, insist: true) == .taken(by: .settings))
        #expect(SanduhrHotKeys.Refusal.needsModifier.canInsist == false)
        #expect(SanduhrHotKeys.Refusal.taken(by: .join).kind == "taken")
    }

    @Test func notesNameTheShortcut() {
        #expect(SanduhrHotKeys.Refusal.macOS(name: SystemShortcuts.unnamed, combo: ctrlUp).note
                == "macOS uses ⌃↑ for another of its shortcuts, so it would get there first.")
        #expect(SanduhrHotKeys.clashNote(cmdSpace, name: "Show Spotlight search")
                == "macOS also uses ⌘Space for “Show Spotlight search”, so it may not reach Sanduhr.")
    }

    @Test func theDefaultsAreNamedAndKnown() {
        #expect(SystemShortcuts.defaults[64]?.combo == cmdSpace)
        #expect(SystemShortcuts.defaults[32]?.combo == ctrlUp)
        #expect(SystemShortcuts.defaults[28]?.combo == shiftCmd3)
        #expect(SystemShortcuts.defaults.values.allSatisfy { $0.name != SystemShortcuts.unnamed && $0.combo.hasRequiredModifier })
        #expect(SystemShortcuts.idNames[118] == "Switch to Desktop 1" && SystemShortcuts.idNames[133] == "Switch to Desktop 16")
        // Sanduhr's own defaults are none of them.
        for s in SanduhrHotKeys.Shortcut.allCases {
            #expect(!SystemShortcuts.defaults.values.contains { $0.combo == s.defaultCombo })
        }
    }

    @Test func probeWaitsArrivesOrMisses() {
        var p = HotKeyProbe()
        let idle = p.arrive(.settings)
        #expect(!idle)                               // not testing: the action runs
        p.start(.settings)
        #expect(p.results[.settings] == .waiting)
        let other = p.arrive(.join)
        #expect(!other)                              // only the shortcut under test
        let heard = p.arrive(.settings)
        #expect(heard)                               // taken by the test
        #expect(p.results[.settings] == .arrived)
        let again = p.arrive(.settings)
        #expect(!again)                              // the next press runs the action again
        p.expire(.settings)
        #expect(p.results[.settings] == .arrived)    // a late timer changes nothing
        p.start(.join)
        p.expire(.join)
        #expect(p.results[.join] == .missed)
        p.clear(.join)
        #expect(p.results[.join] == nil)
        #expect(HotKeyProbe.note(.waiting, combo: ctrlOptS) == "Press ⌃⌥S now…")
        #expect(HotKeyProbe.note(.arrived, combo: ctrlOptS) == "⌃⌥S reached Sanduhr.")
        #expect(HotKeyProbe.note(.missed, combo: ctrlOptS).hasPrefix("⌃⌥S didn't reach Sanduhr in 10 seconds"))
    }
}

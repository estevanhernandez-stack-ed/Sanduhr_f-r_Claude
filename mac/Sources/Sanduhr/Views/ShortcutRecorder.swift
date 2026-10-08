import AppKit
import Carbon.HIToolbox
import SwiftUI

/// General, Shortcuts' recorders (2026-10-08): click a shortcut's keys, press a new combination,
/// it saves; Escape cancels. A combination needs ⌘, ⌃ or ⌥ and can't be the other shortcut's
/// (SanduhrHotKeys.check). While one listens the shortcuts are unregistered, so pressing the
/// current keys records them instead of opening Settings.
@MainActor
final class ShortcutRecorderModel: ObservableObject {
    static let shared = ShortcutRecorderModel()

    typealias Shortcut = SanduhrHotKeys.Shortcut

    /// The shortcut listening for keys, nil when none is.
    @Published private(set) var recording: Shortcut?
    /// The keys saved for each shortcut (its default when none are).
    @Published private(set) var combos: [Shortcut: HotKeyCombo] = [:]
    /// Why the last combination pressed for a shortcut was refused.
    @Published private(set) var refusals: [Shortcut: SanduhrHotKeys.Refusal] = [:]
    /// Shortcuts whose keys another app holds (DeskController.takenShortcuts).
    @Published private(set) var taken: Set<Shortcut> = []

    private var monitor: Any?

    init() {
        reload()
        NotificationCenter.default.addObserver(forName: .sanduhrHotKeysDidRegister, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { ShortcutRecorderModel.shared.reload() }
        }
    }

    func combo(_ s: Shortcut) -> HotKeyCombo { combos[s] ?? s.defaultCombo }

    /// The line under a shortcut: a refused combination, else keys another app holds.
    func note(_ s: Shortcut) -> String? {
        if let refusal = refusals[s] { return refusal.note }
        return taken.contains(s) ? SanduhrHotKeys.takenNote(combo(s)) : nil
    }

    func reload() {
        combos = Dictionary(uniqueKeysWithValues: Shortcut.allCases.map { ($0, SanduhrHotKeys.combo($0)) })
        taken = DeskController.shared.takenShortcuts
    }

    /// Saves `combo` for `s` and registers it; returns the refusal, nil when saved. The smoke's
    /// `hot-key` action goes through here too.
    @discardableResult
    func save(_ combo: HotKeyCombo, for s: Shortcut) -> SanduhrHotKeys.Refusal? {
        let refusal = SanduhrHotKeys.setCombo(combo, for: s)
        refusals[s] = refusal
        if refusal == nil {
            DeskController.shared.applyHotKeys()
            reload()
        }
        return refusal
    }

    func reset(_ s: Shortcut) { save(s.defaultCombo, for: s) }

    func toggleRecording(_ s: Shortcut) {
        recording == s ? stop() : start(s)
    }

    func start(_ s: Shortcut) {
        stop()
        recording = s
        refusals[s] = nil
        DeskController.shared.hotKeysSuspended = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            MainActor.assumeIsolated { ShortcutRecorderModel.shared.handle(event) } ? nil : event
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        guard recording != nil else { return }
        recording = nil
        DeskController.shared.hotKeysSuspended = false
    }

    /// A key pressed while listening: Escape cancels, anything else is tried. Returns whether the
    /// event was used (and so goes no further).
    private func handle(_ event: NSEvent) -> Bool {
        guard let s = recording else { return false }
        if Int(event.keyCode) == kVK_Escape {
            stop()
            return true
        }
        let combo = HotKeyCombo(keyCode: UInt32(event.keyCode), modifiers: Self.carbon(event.modifierFlags))
        if save(combo, for: s) == nil { stop() }
        return true
    }

    /// AppKit's modifier flags as Carbon's mask.
    nonisolated static func carbon(_ flags: NSEvent.ModifierFlags) -> UInt32 {
        var m: UInt32 = 0
        if flags.contains(.command) { m |= HotKeyCombo.command }
        if flags.contains(.shift) { m |= HotKeyCombo.shift }
        if flags.contains(.option) { m |= HotKeyCombo.option }
        if flags.contains(.control) { m |= HotKeyCombo.control }
        return m
    }
}

/// One shortcut on General: its label with the live keys, the recorder, Reset when changed, the
/// switch, and a note when the keys were refused or another app holds them.
struct ShortcutRow: View {
    let shortcut: SanduhrHotKeys.Shortcut
    @Binding var isOn: Bool
    @ObservedObject var model: ShortcutRecorderModel

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(shortcut.title(model.combo(shortcut)))
                Spacer(minLength: 8)
                ShortcutKeysButton(shortcut: shortcut, model: model)
                Toggle(shortcut.title(model.combo(shortcut)), isOn: $isOn)
                    .labelsHidden()
                    .toggleStyle(.switch)
            }
            if let note = model.note(shortcut) {
                Text(note).font(.caption).foregroundStyle(.orange)
            }
        }
    }
}

/// The keys as a button: click to record, Escape to cancel; Reset beside it once changed.
private struct ShortcutKeysButton: View {
    let shortcut: SanduhrHotKeys.Shortcut
    @ObservedObject var model: ShortcutRecorderModel

    private var listening: Bool { model.recording == shortcut }
    private var changed: Bool { model.combo(shortcut) != shortcut.defaultCombo }

    var body: some View {
        HStack(spacing: 6) {
            if changed {
                Button("Reset to \(shortcut.defaultCombo.display)") { model.reset(shortcut) }
                    .buttonStyle(.link)
                    .controlSize(.small)
            }
            Button(listening ? "Type keys…" : model.combo(shortcut).display) { model.toggleRecording(shortcut) }
                .frame(minWidth: 72)
                .help(listening ? "Press the new keys, or Escape to cancel." : "Click, then press new keys.")
                .accessibilityLabel(listening ? "Recording keys, \(shortcut.name)" : "Change keys, \(shortcut.name)")
        }
        .onDisappear { model.stop() }
    }
}

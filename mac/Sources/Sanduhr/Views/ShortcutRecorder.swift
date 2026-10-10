import AppKit
import Carbon.HIToolbox
import SwiftUI

/// General, Shortcuts' recorders (2026-10-08): click a shortcut's keys, press a new combination,
/// it saves; Escape cancels. A combination needs ⌘, ⌃ or ⌥ and can't be the other shortcut's
/// (SanduhrHotKeys.check). While one listens the shortcuts are unregistered, so pressing the
/// current keys records them instead of opening Settings. With "Also check other apps' menus" on
/// and Accessibility allowed, other apps' menu shortcuts are checked too (AppMenuReader, item 73 b).
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
    /// macOS's own shortcuts, read again on reload and whenever Sanduhr comes to the front.
    @Published private(set) var system: SystemShortcuts = .none
    /// Test It's state per shortcut (item 73).
    @Published private(set) var probe = HotKeyProbe()
    /// Other apps' menu shortcuts, last read while the check is on and allowed; empty otherwise.
    @Published private(set) var appMenus: AppMenuShortcuts = .none
    /// Whether macOS trusts Sanduhr with Accessibility, asked on reload (never with a prompt).
    @Published private(set) var appMenusTrusted = false

    private var monitor: Any?

    init() {
        reload()
        NotificationCenter.default.addObserver(forName: .sanduhrHotKeysDidRegister, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { ShortcutRecorderModel.shared.reload() }
        }
        // macOS's shortcuts may have changed in System Settings while Sanduhr was in the back.
        NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { ShortcutRecorderModel.shared.reload() }
        }
    }

    func combo(_ s: Shortcut) -> HotKeyCombo { combos[s] ?? s.defaultCombo }

    /// A line under a shortcut and how loudly it reads.
    struct Note: Equatable {
        enum Tone { case warning, quiet, good }
        let text: String
        let tone: Tone
    }

    /// The line under a shortcut: a refused combination, keys another app holds, Test It's
    /// answer, else a quiet note when it is on while macOS, or else another app's menu, uses the
    /// same keys.
    func note(_ s: Shortcut) -> Note? {
        if let refusal = refusals[s] { return Note(text: refusal.note, tone: .warning) }
        if taken.contains(s) { return Note(text: SanduhrHotKeys.takenNote(combo(s)), tone: .warning) }
        if let result = probe.results[s] {
            let tone: Note.Tone = switch result {
            case .waiting: .quiet
            case .arrived: .good
            case .missed: .warning
            }
            return Note(text: HotKeyProbe.note(result, combo: combo(s)), tone: tone)
        }
        if let name = systemClash(s) { return Note(text: SanduhrHotKeys.clashNote(combo(s), name: name), tone: .quiet) }
        if let hit = appClash(s) {
            return Note(text: SanduhrHotKeys.appClashNote(combo(s), app: hit.app, item: hit.item), tone: .quiet)
        }
        return nil
    }

    /// What else uses the keys of `s` while `s` is on: the macOS shortcut's name first, else the
    /// app menu item ("Finder: New Folder"); nil when nothing does or `s` is off.
    func clash(_ s: Shortcut) -> String? {
        systemClash(s) ?? appClash(s)?.name
    }

    /// The macOS shortcut on the same keys as `s` while `s` is on.
    private func systemClash(_ s: Shortcut) -> String? {
        SanduhrHotKeys.isOn(s, in: UserDefaults.desk) ? system.collision(combo(s)) : nil
    }

    /// The app menu item on the same keys as `s` while `s` is on and the check is active.
    private func appClash(_ s: Shortcut) -> AppMenuShortcuts.Collision? {
        SanduhrHotKeys.isOn(s, in: UserDefaults.desk) ? activeAppMenus.collision(combo(s)) : nil
    }

    /// The app menus to check against: none unless the switch is on and Sanduhr is allowed.
    private var activeAppMenus: AppMenuShortcuts {
        AppMenuReader.isOn() && appMenusTrusted ? appMenus : .none
    }

    /// Whether Test It is offered: the shortcut is on and registered, and nothing is recording.
    func canTest(_ s: Shortcut) -> Bool {
        recording == nil && SanduhrHotKeys.isOn(s, in: UserDefaults.desk) && !taken.contains(s)
    }

    func reload() {
        combos = Dictionary(uniqueKeysWithValues: Shortcut.allCases.map { ($0, SanduhrHotKeys.combo($0)) })
        taken = DeskController.shared.takenShortcuts
        system = SystemShortcuts.current()
        refreshAppMenus()
    }

    /// Asks again whether Sanduhr is trusted (no prompt) and, when the check is on and allowed,
    /// reads the menus if the last read is stale. Off, it forgets them.
    func refreshAppMenus(force: Bool = false) {
        appMenusTrusted = AppMenuReader.isTrusted
        guard AppMenuReader.isOn(), appMenusTrusted else {
            appMenus = .none
            AppMenuReader.shared.refresh()
            return
        }
        appMenus = AppMenuReader.shared.current
        AppMenuReader.shared.refresh(force: force) { menus in ShortcutRecorderModel.shared.appMenus = menus }
    }

    /// "Allow in System Settings…": the one place Sanduhr asks for Accessibility.
    func requestAppMenuAccess() {
        AppMenuReader.requestAccess()
        refreshAppMenus()
    }

    /// Saves `combo` for `s` and registers it; returns the refusal, nil when saved. `insist`
    /// saves keys macOS also uses (Use Anyway). The smoke's `hot-key` action goes through here too.
    @discardableResult
    func save(_ combo: HotKeyCombo, for s: Shortcut, insist: Bool = false) -> SanduhrHotKeys.Refusal? {
        system = SystemShortcuts.current()
        let refusal = SanduhrHotKeys.setCombo(combo, for: s, system: system, appMenus: activeAppMenus, insist: insist)
        refusals[s] = refusal
        if refusal == nil {
            probe.clear(s)
            DeskController.shared.applyHotKeys()
            reload()
        }
        return refusal
    }

    /// Back to the default; macOS's shortcuts and other apps' menus never refuse it.
    func reset(_ s: Shortcut) { save(s.defaultCombo, for: s, insist: true) }

    /// Saves the keys last refused because macOS or another app's menu uses them, and stops
    /// listening.
    func useAnyway(_ s: Shortcut) {
        guard let combo = refusals[s]?.insistCombo else { return }
        if save(combo, for: s, insist: true) == nil { stop() }
    }

    /// Test It: listens for the shortcut's own keys for HotKeyProbe.timeout seconds.
    func test(_ s: Shortcut) {
        guard canTest(s) else { return }
        refusals[s] = nil
        probe.start(s)
        let token = UUID()
        probeTokens[s] = token
        DispatchQueue.main.asyncAfter(deadline: .now() + HotKeyProbe.timeout) {
            MainActor.assumeIsolated {
                let model = ShortcutRecorderModel.shared
                // A newer test of the same shortcut has its own timer.
                if model.probeTokens[s] == token { model.probe.expire(s) }
            }
        }
    }

    private var probeTokens: [Shortcut: UUID] = [:]

    /// The registered keys of `s` were pressed (DeskController). True when Test It took them, so
    /// the shortcut's action is skipped.
    func arrived(_ s: Shortcut) -> Bool { probe.arrive(s) }

    func toggleRecording(_ s: Shortcut) {
        recording == s ? stop() : start(s)
    }

    func start(_ s: Shortcut) {
        stop()
        recording = s
        refusals[s] = nil
        probe.clear(s)
        // A stale read of the menus is renewed while the keys are being chosen.
        refreshAppMenus()
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
        // Refused for macOS's sake, it keeps listening: press other keys, or Use Anyway.
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
/// switch, and a note: refused keys (Use Anyway when only macOS or an app's menu objects), keys
/// another app holds, Test It's answer, or a quiet line when macOS or an app's menu uses the same
/// keys.
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
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                if let note = model.note(shortcut) {
                    Text(note.text)
                        .font(.caption)
                        .foregroundStyle(color(note.tone))
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
                if model.refusals[shortcut]?.canInsist == true {
                    Button("Use Anyway") { model.useAnyway(shortcut) }
                        .buttonStyle(.link)
                        .controlSize(.small)
                        .accessibilityLabel("Use \(model.combo(shortcut).display) anyway, \(shortcut.name)")
                } else if model.canTest(shortcut), model.probe.results[shortcut] != .waiting {
                    Button("Test It") { model.test(shortcut) }
                        .buttonStyle(.link)
                        .controlSize(.small)
                        .help("Press the keys once after clicking, and Sanduhr says whether they arrived.")
                        .accessibilityLabel("Test the keys, \(shortcut.name)")
                }
            }
        }
    }

    private func color(_ tone: ShortcutRecorderModel.Note.Tone) -> Color {
        switch tone {
        case .warning: .orange
        case .quiet: .secondary
        case .good: .green
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

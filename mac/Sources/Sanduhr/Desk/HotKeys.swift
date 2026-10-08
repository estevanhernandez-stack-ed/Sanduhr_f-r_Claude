import Carbon.HIToolbox

/// Sanduhr's global shortcuts: one joins the next meeting, one opens Settings (⌥J and ⌥S unless
/// changed on General; HotKeyCombo). Registered with Carbon's RegisterEventHotKey, which needs no
/// Accessibility permission, while Sanduhr runs and each one's switch is on (Settings, General,
/// Shortcuts; SanduhrHotKeys). A registered hotkey takes the keystroke from every app, so while
/// one is on its keys no longer type (⌥J ∆, ⌥S ß); switching it off gives that back.
final class DeskHotKeys {
    struct Binding {
        let keyCode: UInt32
        let modifiers: UInt32
        let action: () -> Void
    }

    private var refs: [EventHotKeyRef] = []
    private var actions: [UInt32: () -> Void] = [:]
    private var handler: EventHandlerRef?
    /// "Desk" as a four-char code, so our hotkey events are told apart from anyone else's.
    private static let signature: OSType = 0x4465_736B

    var isRegistered: Bool { !refs.isEmpty }
    /// How many are registered now (state.yaml `hot_keys.registered`).
    var count: Int { refs.count }

    /// Registers each binding; returns the indexes of those another app already holds.
    @discardableResult
    func register(_ bindings: [Binding]) -> [Int] {
        var failed: [Int] = []
        unregister()
        installHandlerIfNeeded()
        for (index, binding) in bindings.enumerated() {
            let id = EventHotKeyID(signature: Self.signature, id: UInt32(index + 1))
            var ref: EventHotKeyRef?
            let status = RegisterEventHotKey(binding.keyCode, binding.modifiers, id,
                                             GetApplicationEventTarget(), 0, &ref)
            if status == noErr, let ref {
                refs.append(ref)
                actions[id.id] = binding.action
            } else {
                // Another app holds the combination (eventHotKeyExistsErr); it keeps it.
                NSLog("Desk hotkey \(binding.keyCode) not registered: \(status)")
                failed.append(index)
            }
        }
        return failed
    }

    func unregister() {
        refs.forEach { UnregisterEventHotKey($0) }
        refs = []
        actions = [:]
    }

    private func installHandlerIfNeeded() {
        guard handler == nil else { return }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let me = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { _, event, userData in
            guard let event, let userData else { return OSStatus(eventNotHandledErr) }
            var id = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                                           EventParamType(typeEventHotKeyID), nil,
                                           MemoryLayout<EventHotKeyID>.size, nil, &id)
            guard status == noErr, id.signature == DeskHotKeys.signature else { return OSStatus(eventNotHandledErr) }
            let hotKeys = Unmanaged<DeskHotKeys>.fromOpaque(userData).takeUnretainedValue()
            guard let action = hotKeys.actions[id.id] else { return OSStatus(eventNotHandledErr) }
            DispatchQueue.main.async(execute: action)
            return noErr
        }, 1, &spec, me, &handler)
    }
}

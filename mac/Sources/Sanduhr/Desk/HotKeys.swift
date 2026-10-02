import Carbon.HIToolbox

/// Desk's global shortcuts: Option+J joins the next meeting, Option+S opens Desk settings.
/// Registered with Carbon's RegisterEventHotKey, which needs no Accessibility permission, and
/// only while Desk runs with the shortcuts switch on (Desk settings, General). A registered
/// hotkey takes the keystroke from every app, so while they are on, Option+J and Option+S no
/// longer type ∆ and ß; switching them off gives those back.
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

    func register(_ bindings: [Binding]) {
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
            }
        }
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

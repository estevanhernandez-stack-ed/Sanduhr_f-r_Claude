import Foundation
import CoreAudio
import os

/// Which device the mic monitor listens to: the default input device, followed as it changes
/// (a Bluetooth headset connecting, a pick in Sound settings). Pure, so device changes test
/// without hardware.
struct MicWatch: Equatable {
    /// The device whose running flag is watched now, nil for none.
    private(set) var device: AudioObjectID?

    struct Change: Equatable {
        /// Stop listening to this one.
        var unwatch: AudioObjectID?
        /// Start listening to this one.
        var watch: AudioObjectID?
    }

    /// The default input is now `next` (nil for none): what to stop and start listening to.
    /// The same device again changes nothing.
    mutating func follow(_ next: AudioObjectID?) -> Change {
        let next = next == AudioObjectID(kAudioObjectUnknown) ? nil : next
        guard next != device else { return Change() }
        let change = Change(unwatch: device, watch: next)
        device = next
        return change
    }

    /// Stops watching: the device to stop listening to.
    mutating func reset() -> AudioObjectID? {
        defer { device = nil }
        return device
    }

    /// The activity event for the watched device's running flag.
    func event(running: Bool) -> CameraActivity.Event {
        .devices(device.map { [$0: running] } ?? [:])
    }
}

/// Watches whether any app uses the microphone: `kAudioDevicePropertyDeviceIsRunningSomewhere` on
/// the default input device, with a listener on it and one on the system's default input device
/// (`kAudioHardwarePropertyDefaultInputDevice`), so it follows a headset as it connects. No
/// polling. Reading those properties opens no input stream, reads no audio and needs no
/// microphone permission, so macOS never asks. `onChange` runs on the main queue whenever `inUse`
/// flips; the decision (on at once, off after a short settle, as for cameras) is CameraActivity's.
///
/// The listeners are C functions with this monitor's target as client data, as in CameraMonitor:
/// a remove must name the very listener the add registered, which a function pointer and a
/// client-data pointer guarantee and a bridged Swift block does not. Core Audio calls them on its
/// own thread; they only hop to the main queue.
final class MicMonitor {
    private(set) var inUse = false
    private(set) var isRunning = false
    var onChange: ((Bool) -> Void)?

    private var activity = CameraActivity()
    private var watch = MicWatch()
    private var settleWork: DispatchWorkItem?
    /// Failed listener calls already logged (once per call and selector), so a device that keeps
    /// refusing doesn't fill the log. Never anything about the device or an app.
    private var loggedFailures: Set<String> = []
    private static let log = Logger(subsystem: "com.626labs.sanduhr", category: "mic")

    private final class Target {
        weak var monitor: MicMonitor?
    }
    private let target = Target()
    private var clientData: UnsafeMutableRawPointer { Unmanaged.passUnretained(target).toOpaque() }

    private static let defaultListener: AudioObjectPropertyListenerProc = { _, _, _, data in
        guard let data else { return noErr }
        let target = Unmanaged<Target>.fromOpaque(data).takeUnretainedValue()
        DispatchQueue.main.async { target.monitor?.followDefault() }
        return noErr
    }
    private static let runningListener: AudioObjectPropertyListenerProc = { _, _, _, data in
        guard let data else { return noErr }
        let target = Unmanaged<Target>.fromOpaque(data).takeUnretainedValue()
        DispatchQueue.main.async { target.monitor?.reloadRunning() }
        return noErr
    }

    init() {
        target.monitor = self
        _ = Unmanaged.passRetained(target)
    }

    deinit {
        if isRunning {
            var addr = Self.defaultAddress
            _ = AudioObjectRemovePropertyListener(Self.system, &addr, Self.defaultListener, clientData)
            if let device = watch.reset() {
                var running = Self.runningAddress
                _ = AudioObjectRemovePropertyListener(device, &running, Self.runningListener, clientData)
            }
        }
        Unmanaged.passUnretained(target).release()
    }

    private static var system: AudioObjectID { AudioObjectID(kAudioObjectSystemObject) }

    private static var defaultAddress: AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultInputDevice,
                                   mScope: kAudioObjectPropertyScopeGlobal,
                                   mElement: kAudioObjectPropertyElementMain)
    }

    private static var runningAddress: AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyDeviceIsRunningSomewhere,
                                   mScope: kAudioObjectPropertyScopeGlobal,
                                   mElement: kAudioObjectPropertyElementMain)
    }

    func start() {
        guard !isRunning else { return }
        isRunning = true
        var addr = Self.defaultAddress
        check("add default", AudioObjectAddPropertyListener(Self.system, &addr, Self.defaultListener, clientData))
        followDefault()
    }

    func stop() {
        guard isRunning else { return }
        isRunning = false
        var addr = Self.defaultAddress
        check("remove default", AudioObjectRemovePropertyListener(Self.system, &addr, Self.defaultListener, clientData))
        if let device = watch.reset() { unlisten(device) }
        settleWork?.cancel(); settleWork = nil
        activity = CameraActivity()
        publish()
    }

    // MARK: Core Audio

    /// The default input device changed (or at start): move the running listener to it and read it.
    private func followDefault() {
        guard isRunning else { return }
        let change = watch.follow(Self.defaultInput())
        if let old = change.unwatch { unlisten(old) }
        if let new = change.watch {
            var addr = Self.runningAddress
            check("add running", AudioObjectAddPropertyListener(new, &addr, Self.runningListener, clientData))
        }
        reloadRunning()
    }

    private func reloadRunning() {
        guard isRunning else { return }
        apply(watch.event(running: watch.device.map(Self.isRunningSomewhere) ?? false))
    }

    private func unlisten(_ device: AudioObjectID) {
        var addr = Self.runningAddress
        // A headset disconnected a moment ago can refuse with a bad-object status; logged once.
        check("remove running", AudioObjectRemovePropertyListener(device, &addr, Self.runningListener, clientData))
    }

    private func check(_ call: String, _ status: OSStatus) {
        guard status != noErr, loggedFailures.insert(call).inserted else { return }
        Self.log.error("Core Audio \(call, privacy: .public) listener failed, OSStatus \(status)")
    }

    /// The default input device, nil for none.
    private static func defaultInput() -> AudioObjectID? {
        var addr = defaultAddress
        var id = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &addr, 0, nil, &size, &id) == noErr,
              id != AudioObjectID(kAudioObjectUnknown) else { return nil }
        return id
    }

    /// Whether any process has the device running. A property read: no stream, no audio.
    private static func isRunningSomewhere(_ id: AudioObjectID) -> Bool {
        var addr = runningAddress
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &value) == noErr else { return false }
        return value != 0
    }

    // MARK: Decision

    private func apply(_ event: CameraActivity.Event) {
        settleWork?.cancel(); settleWork = nil
        if let wait = activity.reduce(event, now: Date()) {
            let work = DispatchWorkItem { [weak self] in self?.apply(.settle) }
            settleWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + wait, execute: work)
        }
        publish()
    }

    private func publish() {
        guard activity.inUse != inUse else { return }
        inUse = activity.inUse
        onChange?(inUse)
    }
}

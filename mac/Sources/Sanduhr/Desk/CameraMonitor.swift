import Foundation
import CoreMediaIO

/// Whether any camera is in use, decided from each device's running flag. Pure, so it tests
/// without hardware: the monitor feeds it events and schedules a `.settle` when asked to.
/// A camera turning on counts at once; all of them off counts only after `offDelay` without
/// one coming back, so an app that restarts its capture session does not flicker the light.
struct CameraActivity: Equatable {
    static let offDelay: TimeInterval = 0.5

    enum Event: Equatable {
        /// One device's running flag changed.
        case device(UInt32, running: Bool)
        /// The device list changed (a camera plugged in or out): every device's flag, read again.
        case devices([UInt32: Bool])
        /// The time asked for by the last reduce has passed.
        case settle
    }

    private(set) var running: [UInt32: Bool] = [:]
    private(set) var inUse = false
    /// When the last running camera stopped, while `inUse` still holds.
    private(set) var stoppedAt: Date?

    var anyRunning: Bool { running.values.contains(true) }

    /// Applies the event; returns how long to wait before sending `.settle`, nil when nothing is pending.
    @discardableResult
    mutating func reduce(_ event: Event, now: Date) -> TimeInterval? {
        switch event {
        case .device(let id, let on): running[id] = on
        case .devices(let all): running = all
        case .settle: break
        }
        if anyRunning {
            inUse = true
            stoppedAt = nil
            return nil
        }
        guard inUse else { return nil }
        let since = stoppedAt ?? now
        stoppedAt = since
        let left = Self.offDelay - now.timeIntervalSince(since)
        if left <= 0 {
            inUse = false
            stoppedAt = nil
            return nil
        }
        return left
    }
}

/// Watches CoreMediaIO for any app using a camera: `kCMIODevicePropertyDeviceIsRunningSomewhere`
/// on every video device, with a listener on each and one on the device list for hot-plugged
/// cameras. Reading that flag needs no camera permission and never turns a camera on.
/// `onChange` runs on the main queue whenever `inUse` flips.
final class CameraMonitor {
    private(set) var inUse = false
    private(set) var isRunning = false
    var onChange: ((Bool) -> Void)?

    private var activity = CameraActivity()
    private var watched: [CMIOObjectID] = []
    private var settleWork: DispatchWorkItem?
    private lazy var deviceBlock: CMIOObjectPropertyListenerBlock = { [weak self] _, _ in
        self?.reloadDevices()
    }
    private lazy var runningBlock: CMIOObjectPropertyListenerBlock = { [weak self] _, _ in
        self?.reloadRunning()
    }

    private static func address(_ selector: Int, scope: Int = kCMIOObjectPropertyScopeGlobal) -> CMIOObjectPropertyAddress {
        CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(selector),
                                  mScope: CMIOObjectPropertyScope(scope),
                                  mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
    }

    private static var system: CMIOObjectID { CMIOObjectID(kCMIOObjectSystemObject) }

    func start() {
        guard !isRunning else { return }
        isRunning = true
        var addr = Self.address(kCMIOHardwarePropertyDevices)
        CMIOObjectAddPropertyListenerBlock(Self.system, &addr, .main, deviceBlock)
        reloadDevices()
    }

    func stop() {
        guard isRunning else { return }
        isRunning = false
        var addr = Self.address(kCMIOHardwarePropertyDevices)
        CMIOObjectRemovePropertyListenerBlock(Self.system, &addr, .main, deviceBlock)
        unwatchAll()
        settleWork?.cancel(); settleWork = nil
        activity = CameraActivity()
        publish()
    }

    // MARK: CoreMediaIO

    private func reloadDevices() {
        guard isRunning else { return }
        unwatchAll()
        watched = Self.videoDevices()
        var addr = Self.address(kCMIODevicePropertyDeviceIsRunningSomewhere)
        for id in watched {
            CMIOObjectAddPropertyListenerBlock(id, &addr, .main, runningBlock)
        }
        apply(.devices(readAll()))
    }

    /// The listener does not say which device changed in a way worth trusting across macOS
    /// versions, so every watched device is read again; there are only a few.
    private func reloadRunning() {
        guard isRunning else { return }
        apply(.devices(readAll()))
    }

    private func unwatchAll() {
        var addr = Self.address(kCMIODevicePropertyDeviceIsRunningSomewhere)
        for id in watched {
            CMIOObjectRemovePropertyListenerBlock(id, &addr, .main, runningBlock)
        }
        watched = []
    }

    private func readAll() -> [UInt32: Bool] {
        var out: [UInt32: Bool] = [:]
        for id in watched { out[id] = Self.isRunningSomewhere(id) }
        return out
    }

    /// Devices with an input stream that report whether they run (CoreMediaIO devices are video;
    /// microphones live in Core Audio).
    private static func videoDevices() -> [CMIOObjectID] {
        var addr = address(kCMIOHardwarePropertyDevices)
        var size: UInt32 = 0
        guard CMIOObjectGetPropertyDataSize(system, &addr, 0, nil, &size) == noErr, size > 0 else { return [] }
        let count = Int(size) / MemoryLayout<CMIOObjectID>.size
        var ids = [CMIOObjectID](repeating: 0, count: count)
        var used: UInt32 = 0
        guard CMIOObjectGetPropertyData(system, &addr, 0, nil, size, &used, &ids) == noErr else { return [] }
        return ids.prefix(Int(used) / MemoryLayout<CMIOObjectID>.size).filter { id in
            var running = address(kCMIODevicePropertyDeviceIsRunningSomewhere)
            guard CMIOObjectHasProperty(id, &running) else { return false }
            var streams = address(kCMIODevicePropertyStreams, scope: kCMIODevicePropertyScopeInput)
            var streamSize: UInt32 = 0
            return CMIOObjectGetPropertyDataSize(id, &streams, 0, nil, &streamSize) == noErr && streamSize > 0
        }
    }

    private static func isRunningSomewhere(_ id: CMIOObjectID) -> Bool {
        var addr = address(kCMIODevicePropertyDeviceIsRunningSomewhere)
        var value: UInt32 = 0
        var used: UInt32 = 0
        let size = UInt32(MemoryLayout<UInt32>.size)
        guard CMIOObjectGetPropertyData(id, &addr, 0, nil, size, &used, &value) == noErr else { return false }
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

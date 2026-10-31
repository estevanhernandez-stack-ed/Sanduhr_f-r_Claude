import Foundation
import CoreAudio
import os

// The one file that talks to Core Audio for the live now playing bars (spec 14). Public API only:
// a process tap on ONE app's output (macOS 14.2+), held by a private aggregate device, read by an
// IOProc that keeps a single peak. No buffer is copied, kept, written or sent. Creating the tap
// device is what makes macOS ask for System Audio Recording, so this is built only from
// AudioLevelTapFactory, which the audio bars driver calls only after the Settings switch is on.
// There are no private TCC calls here (no preflight, no request): a denial shows up as silence and
// AudioDeniedInference reads it.

/// The only place a real tap is constructed.
enum AudioLevelTapFactory {
    static var isSupported: Bool {
        if #available(macOS 14.2, *) { return true }
        return false
    }

    static func make() -> AudioLevelTap {
        if #available(macOS 14.2, *) { return CoreAudioProcessTap() }
        return UnavailableAudioTap()
    }
}

/// macOS before 14.2: no process taps. Never starts.
final class UnavailableAudioTap: AudioLevelTap {
    func start(pid: Int) -> Bool { false }
    func stop() {}
    func takePeak() -> Float { 0 }
    func playerIsOutputting(pid: Int) -> Bool { false }
    func armWake(_ handler: @escaping () -> Void) {}
}

@available(macOS 14.2, *)
final class CoreAudioProcessTap: AudioLevelTap {
    private static let log = Logger(subsystem: "com.626labs.sanduhr", category: "audiotap")

    /// What the audio thread shares with the main thread: the largest sample since the last read, and
    /// the one-shot wake handler. A lock, because the IOProc is not on the main thread.
    private struct Shared {
        var peak: Float = 0
        var wake: (() -> Void)?
    }
    private let shared = OSAllocatedUnfairLock(initialState: Shared())

    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var aggregateID = AudioObjectID(kAudioObjectUnknown)
    private var procID: AudioDeviceIOProcID?
    private let queue = DispatchQueue(label: "com.626labs.sanduhr.audiotap", qos: .userInteractive)

    // MARK: AudioLevelTap

    func start(pid: Int) -> Bool {
        stop()
        let processes = Self.playerObjects(for: pid)
        guard !processes.isEmpty else {
            Self.log.info("no audio process for the player")
            return false
        }
        let description = CATapDescription(stereoMixdownOfProcesses: processes)
        description.muteBehavior = .unmuted
        description.isPrivate = true
        description.name = "Sanduhr level"

        var newTap = AudioObjectID(kAudioObjectUnknown)
        guard AudioHardwareCreateProcessTap(description, &newTap) == noErr else {
            Self.log.error("could not create the tap")
            return false
        }
        tapID = newTap

        let aggregate: [String: Any] = [
            kAudioAggregateDeviceNameKey: "Sanduhr level",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceTapListKey: [[
                kAudioSubTapUIDKey: description.uuid.uuidString,
                kAudioSubTapDriftCompensationKey: true,
            ]],
        ]
        var newAggregate = AudioObjectID(kAudioObjectUnknown)
        guard AudioHardwareCreateAggregateDevice(aggregate as CFDictionary, &newAggregate) == noErr else {
            Self.log.error("could not create the aggregate device")
            stop()
            return false
        }
        aggregateID = newAggregate

        var newProc: AudioDeviceIOProcID?
        let status = AudioDeviceCreateIOProcIDWithBlock(&newProc, aggregateID, queue) { [shared] _, input, _, _, _ in
            let peak = Self.peak(of: input)
            // One lock per callback; the wake handler fires at most once per arm.
            let wake: (() -> Void)? = shared.withLock { state in
                if peak > state.peak { state.peak = peak }
                guard peak >= AudioBars.idleThreshold, let handler = state.wake else { return nil }
                state.wake = nil
                return handler
            }
            if let wake { DispatchQueue.main.async(execute: wake) }
        }
        guard status == noErr, let newProc else {
            Self.log.error("could not create the IO proc")
            stop()
            return false
        }
        procID = newProc
        guard AudioDeviceStart(aggregateID, newProc) == noErr else {
            Self.log.error("could not start the device")
            stop()
            return false
        }
        Self.log.info("level tap on")
        return true
    }

    func stop() {
        if let proc = procID, aggregateID != kAudioObjectUnknown {
            AudioDeviceStop(aggregateID, proc)
            AudioDeviceDestroyIOProcID(aggregateID, proc)
        }
        procID = nil
        if aggregateID != kAudioObjectUnknown {
            AudioHardwareDestroyAggregateDevice(aggregateID)
            aggregateID = AudioObjectID(kAudioObjectUnknown)
        }
        if tapID != kAudioObjectUnknown {
            AudioHardwareDestroyProcessTap(tapID)
            tapID = AudioObjectID(kAudioObjectUnknown)
        }
        shared.withLock { $0 = Shared() }
    }

    func takePeak() -> Float {
        shared.withLock { state in
            let peak = state.peak
            state.peak = 0
            return peak
        }
    }

    func playerIsOutputting(pid: Int) -> Bool {
        Self.playerObjects(for: pid).contains { process in
            var address = AudioObjectPropertyAddress(
                mSelector: kAudioProcessPropertyIsRunningOutput,
                mScope: kAudioObjectPropertyScopeGlobal,
                mElement: kAudioObjectPropertyElementMain)
            var value: UInt32 = 0
            var size = UInt32(MemoryLayout<UInt32>.size)
            guard AudioObjectGetPropertyData(process, &address, 0, nil, &size, &value) == noErr else { return false }
            return value != 0
        }
    }

    func armWake(_ handler: @escaping () -> Void) {
        shared.withLock { $0.wake = handler }
    }

    deinit { stop() }

    // MARK: Core Audio reads

    /// The audio process objects of the player and its helpers (AudioPlayerProcesses decides which).
    private static func playerObjects(for pid: Int) -> [AudioObjectID] {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyProcessObjectList,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        let system = AudioObjectID(kAudioObjectSystemObject)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &size) == noErr, size > 0 else {
            return processObject(for: pid).map { [$0] } ?? []
        }
        var objects = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &objects) == noErr else {
            return processObject(for: pid).map { [$0] } ?? []
        }
        let pids = objects.compactMap { o in processPID(o).map { (pid: $0, object: o) } }
        let byPid = Dictionary(pids.map { ($0.pid, $0.object) }, uniquingKeysWith: { a, _ in a })
        let candidates = pids.map { AudioPlayerProcesses.Candidate(pid: $0.pid, bundleID: processBundleID($0.object)) }
        let playerBundle = candidates.first { $0.pid == pid }?.bundleID
        return AudioPlayerProcesses.members(player: pid, playerBundleID: playerBundle,
                                            candidates: candidates, parent: parentPID)
            .compactMap { byPid[$0] }
    }

    private static func processPID(_ object: AudioObjectID) -> Int? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioProcessPropertyPID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var value = pid_t(0)
        var size = UInt32(MemoryLayout<pid_t>.size)
        guard AudioObjectGetPropertyData(object, &address, 0, nil, &size, &value) == noErr, value > 0 else { return nil }
        return Int(value)
    }

    private static func processBundleID(_ object: AudioObjectID) -> String? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioProcessPropertyBundleID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var value: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        guard AudioObjectGetPropertyData(object, &address, 0, nil, &size, &value) == noErr,
              let id = value?.takeRetainedValue() as String?, !id.isEmpty else { return nil }
        return id
    }

    /// A pid's parent, from the kernel's process table. Nil when the process is gone.
    private static func parentPID(_ pid: Int) -> Int? {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, Int32(pid)]
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0, size > 0 else { return nil }
        return Int(info.kp_eproc.e_ppid)
    }

    /// The audio process object for a pid, nil when that pid has none (it has not touched audio).
    private static func processObject(for pid: Int) -> AudioObjectID? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyTranslatePIDToProcessObject,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var qualifier = pid_t(pid)
        var object = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        let status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject), &address,
            UInt32(MemoryLayout<pid_t>.size), &qualifier, &size, &object)
        guard status == noErr, object != kAudioObjectUnknown else { return nil }
        return object
    }

    /// The largest absolute sample in the callback's input, 0 to 1. Reads in place; copies nothing.
    private static func peak(of input: UnsafePointer<AudioBufferList>) -> Float {
        var peak: Float = 0
        let buffers = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: input))
        for buffer in buffers {
            guard let data = buffer.mData else { continue }
            let count = Int(buffer.mDataByteSize) / MemoryLayout<Float>.size
            let samples = data.assumingMemoryBound(to: Float.self)
            for i in 0..<count {
                let v = abs(samples[i])
                if v > peak { peak = v }
            }
        }
        return min(peak, 1)
    }
}

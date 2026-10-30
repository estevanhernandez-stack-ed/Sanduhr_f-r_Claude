import Foundation
import Darwin

/// Reads the three counters the system piece shows. Local only: nothing here is sent or stored.
enum TelemetryReader {
    /// The summed CPU tick counters of every core (`host_processor_info`, PROCESSOR_CPU_LOAD_INFO), mapped
    /// onto Windows' shape: idle = idle, kernel = system + idle, user = user + nice. Nil when the call fails.
    static func cpuTimes() -> CpuTimes? {
        var cpuCount: natural_t = 0
        var info: processor_info_array_t?
        var infoCount: mach_msg_type_number_t = 0
        let status = host_processor_info(mach_host_self(), PROCESSOR_CPU_LOAD_INFO, &cpuCount, &info, &infoCount)
        guard status == KERN_SUCCESS, let info else { return nil }
        defer {
            vm_deallocate(mach_task_self_, vm_address_t(UInt(bitPattern: info)),
                          vm_size_t(infoCount) * vm_size_t(MemoryLayout<integer_t>.stride))
        }
        var idle: UInt64 = 0, system: UInt64 = 0, user: UInt64 = 0, nice: UInt64 = 0
        let states = Int(CPU_STATE_MAX)
        for core in 0..<Int(cpuCount) {
            let base = core * states
            user += UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_USER)]))
            system += UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_SYSTEM)]))
            idle += UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_IDLE)]))
            nice += UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_NICE)]))
        }
        return CpuTimes(idle: idle, kernel: system + idle, user: user + nice)
    }

    /// Memory in use and physical memory, in bytes (`host_statistics64`, HOST_VM_INFO64).
    /// Which "used": active + wired + compressed pages. Windows' `dwMemoryLoad` counts everything that is
    /// not available, so the closest Mac reading leaves out free, inactive and speculative pages (cache the
    /// system gives back on demand) and counts what it cannot: active, wired and the compressor's pages.
    /// That runs a little above Activity Monitor's "Memory Used", which also subtracts purgeable pages.
    static func memory() -> (used: UInt64, physical: UInt64)? {
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.stride / MemoryLayout<integer_t>.stride)
        let status = withUnsafeMutablePointer(to: &stats) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count)
            }
        }
        guard status == KERN_SUCCESS else { return nil }
        let pages = UInt64(stats.active_count) + UInt64(stats.wire_count) + UInt64(stats.compressor_page_count)
        return (pages * UInt64(vm_kernel_page_size), ProcessInfo.processInfo.physicalMemory)
    }

    /// The boot volume's capacity and what is free for important use, which counts purgeable space as
    /// free (what Finder shows as available) rather than the raw free blocks.
    static func disk() -> (total: UInt64, free: UInt64)? {
        let keys: Set<URLResourceKey> = [.volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey]
        guard let values = try? URL(fileURLWithPath: "/").resourceValues(forKeys: keys),
              let total = values.volumeTotalCapacity,
              let free = values.volumeAvailableCapacityForImportantUsage else { return nil }
        return (UInt64(max(0, total)), UInt64(max(0, free)))
    }
}

/// Samples CPU, memory and disk every `Telemetry.interval` while it is started, and not at all
/// otherwise: no timer exists until `start()` and none after `stop()`. The Desk starts it only while
/// it shows the system piece. The first CPU reading needs two tick readings, so the sample taken at
/// `start()` carries 0 for CPU and the first tick the real figure.
final class TelemetrySampler {
    /// What the sampler reads, replaceable so tests need no kernel.
    struct Source {
        var cpu: () -> CpuTimes?
        var memory: () -> (used: UInt64, physical: UInt64)?
        var disk: () -> (total: UInt64, free: UInt64)?
        static let live = Source(cpu: TelemetryReader.cpuTimes, memory: TelemetryReader.memory, disk: TelemetryReader.disk)
    }

    /// Starts a repeating call and returns what cancels it.
    typealias Schedule = (_ interval: TimeInterval, _ fire: @escaping () -> Void) -> () -> Void

    /// A repeating main-run-loop timer.
    static let liveSchedule: Schedule = { interval, fire in
        let timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { _ in fire() }
        return { timer.invalidate() }
    }

    private let source: Source
    private let schedule: Schedule
    private let now: () -> Date
    private var cancel: (() -> Void)?
    private var previous: CpuTimes?

    /// The newest reading, nil before the first and after `stop()`.
    private(set) var latest: TelemetrySample?
    /// Called on the main thread with each new reading.
    var onSample: ((TelemetrySample?) -> Void)?

    var isRunning: Bool { cancel != nil }

    init(source: Source = .live, schedule: @escaping Schedule = TelemetrySampler.liveSchedule,
         now: @escaping () -> Date = Date.init) {
        self.source = source
        self.schedule = schedule
        self.now = now
    }

    func start() {
        guard cancel == nil else { return }
        previous = source.cpu()
        publish(sample(cpu: 0))
        cancel = schedule(Telemetry.interval) { [weak self] in self?.tick() }
    }

    /// Stops the timer and forgets the reading: a Desk shown again starts from a fresh baseline.
    func stop() {
        cancel?()
        cancel = nil
        previous = nil
        if latest != nil { publish(nil) }
    }

    private func tick() {
        var cpu = latest?.cpu ?? 0
        if let next = source.cpu() {
            if let previous { cpu = Telemetry.cpuPercent(previous, next) }
            previous = next
        }
        publish(sample(cpu: cpu))
    }

    private func sample(cpu: Double) -> TelemetrySample {
        let ram = source.memory().map { Telemetry.memoryPercent(used: $0.used, physical: $0.physical) } ?? 0
        let disk = source.disk().map { Telemetry.diskUsedPercent(total: $0.total, free: $0.free) } ?? 0
        return TelemetrySample(cpu: cpu, ram: ram, disk: disk, at: now())
    }

    private func publish(_ sample: TelemetrySample?) {
        latest = sample
        onSample?(sample)
    }
}

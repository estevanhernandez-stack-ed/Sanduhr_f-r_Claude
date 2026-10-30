import Foundation

/// One reading of the CPU tick counters. Windows' `GetSystemTimes` shape, kept so the math and its
/// tests port as they are: kernel time INCLUDES idle time. The Mac sampler maps `host_processor_info`
/// onto it: idle = idle ticks, kernel = system + idle, user = user + nice.
struct CpuTimes: Equatable {
    var idle: UInt64
    var kernel: UInt64
    var user: UInt64
}

/// One sample of the three system numbers, each 0 to 100.
struct TelemetrySample: Equatable {
    var cpu: Double
    var ram: Double
    var disk: Double
    var at: Date
}

/// One drawn row of the system piece: key, label, whole percent and bar fill 0 to 1.
struct TelemetryRow: Equatable, Identifiable {
    var key: String
    var label: String
    var percent: Int
    var fill: Double
    var id: String { key }
}

/// The pure math behind the Desk system piece (layout word `telemetry`, shown as "System"), ported
/// from Windows' `Telemetry`: CPU from two tick readings, memory used against physical, disk as the
/// used share of the boot volume, and the rows the piece draws. `TelemetrySampler` reads the
/// counters; nothing here touches the OS.
enum Telemetry {
    /// How often the Desk samples: five seconds, and only while the Desk shows the piece.
    static let interval: TimeInterval = 5

    /// CPU busy share between two readings: busy = (dKernel - dIdle) + dUser over
    /// total = dKernel + dUser. Zero for a zero or negative total (a counter that did not move or went
    /// backwards); clamped to 0...100.
    static func cpuPercent(_ prev: CpuTimes, _ next: CpuTimes) -> Double {
        let dIdle = Double(next.idle) - Double(prev.idle)
        let dKernel = Double(next.kernel) - Double(prev.kernel)
        let dUser = Double(next.user) - Double(prev.user)
        let total = dKernel + dUser
        guard total > 0 else { return 0 }
        let busy = (dKernel - dIdle) + dUser
        return min(100, max(0, busy / total * 100))
    }

    /// Memory used as a share of physical memory, clamped to 0...100; 0 for no physical memory.
    static func memoryPercent(used: UInt64, physical: UInt64) -> Double {
        guard physical > 0 else { return 0 }
        return min(100, max(0, Double(used) / Double(physical) * 100))
    }

    /// Windows' reading: `dwMemoryLoad`, a percent, clamped to 0...100.
    static func memoryPercent(_ load: UInt32) -> Double { min(100, max(0, Double(load))) }

    /// Disk used share from total and free bytes; 0 when the total is 0, and 0 for a free count
    /// above the total.
    static func diskUsedPercent(total: UInt64, free: UInt64) -> Double {
        guard total > 0 else { return 0 }
        let used = free >= total ? 0 : Double(total - free)
        return min(100, max(0, used / Double(total) * 100))
    }

    /// The rows in draw order: CPU, RAM, Disk. Percent rounds half away from zero; fill is percent / 100.
    static func rows(_ sample: TelemetrySample) -> [TelemetryRow] {
        [row("CPU", "CPU", sample.cpu), row("RAM", "RAM", sample.ram), row("Disk", "Disk", sample.disk)]
    }

    private static func row(_ key: String, _ label: String, _ value: Double) -> TelemetryRow {
        let safe = value.isNaN ? 0 : min(100, max(0, value))
        let percent = Int(safe.rounded(.toNearestOrAwayFromZero))
        return TelemetryRow(key: key, label: label, percent: percent, fill: Double(percent) / 100)
    }

    /// The made-up reading demo mode and Settings' previews show.
    static func demoSample(now: Date = Date()) -> TelemetrySample {
        TelemetrySample(cpu: 23, ram: 61, disk: 48, at: now)
    }
}

/// The meter-style gauges of the system rows: the same `Gauge` the Claude limits use, with no pace
/// mark and no warning (a busy machine is not a limit). The tier is a stand-in id only: the system
/// piece reports no limit-menu frames.
extension DeskMeterStyleModel {
    static func telemetryGauges(_ sample: TelemetrySample) -> [Gauge] {
        Telemetry.rows(sample).map { r in
            Gauge(tier: .fiveHour, label: r.label, short: r.label, percent: r.percent, fill: r.fill,
                  pace: nil, warning: false, key: r.key)
        }
    }
}

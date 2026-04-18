import Foundation

/// Pure port of `time_until`, `reset_datetime_str`, `pace_frac`, `pace_info`,
/// and `burn_projection` from sanduhr.py:157-228.

private let isoFormatter: ISO8601DateFormatter = {
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    return f
}()

private let isoFallback: ISO8601DateFormatter = {
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime]
    return f
}()

func parseISO(_ s: String?) -> Date? {
    guard let s = s, !s.isEmpty else { return nil }
    // Normalize "Z" suffix and strip trailing whitespace.
    let trimmed = s.trimmingCharacters(in: .whitespacesAndNewlines)
    if let d = isoFormatter.date(from: trimmed) { return d }
    if let d = isoFallback.date(from: trimmed) { return d }
    return nil
}

/// Human-friendly countdown ("2d 3h 15m"). sanduhr.py:157-168.
func timeUntil(_ iso: String?, now: Date = Date()) -> String {
    guard let rd = parseISO(iso) else { return "—" }
    let s = max(0, Int(rd.timeIntervalSince(now)))
    if s <= 0 { return "now" }
    let d = s / 86400
    let h = (s % 86400) / 3600
    let m = (s % 3600) / 60
    var parts: [String] = []
    if d > 0 { parts.append("\(d)d") }
    if h > 0 { parts.append("\(h)h") }
    if m > 0 || parts.isEmpty { parts.append("\(m)m") }
    return parts.joined(separator: " ")
}

/// "Today 5:00 PM" / "Tomorrow 3:30 PM" / "Thu 9:00 AM" / "Sat Apr 18 9:00 AM".
/// sanduhr.py:170-181.
func resetDateTimeStr(_ iso: String?, now: Date = Date()) -> String {
    guard let rd = parseISO(iso) else { return "" }
    let cal = Calendar.current
    let startResets = cal.startOfDay(for: rd)
    let startNow = cal.startOfDay(for: now)
    let days = cal.dateComponents([.day], from: startNow, to: startResets).day ?? 0

    let timeF = DateFormatter()
    timeF.dateFormat = "h:mm a"
    let t = timeF.string(from: rd)

    if days <= 0 { return "Today \(t)" }
    if days == 1 { return "Tomorrow \(t)" }
    if days < 7 {
        let wd = DateFormatter(); wd.dateFormat = "EEE"
        return "\(wd.string(from: rd)) \(t)"
    }
    let full = DateFormatter(); full.dateFormat = "EEE MMM d"
    return "\(full.string(from: rd)) \(t)"
}

/// Fraction of the current period already elapsed (0…1). sanduhr.py:183-189.
func paceFrac(_ iso: String?, tier: Tier, now: Date = Date()) -> Double? {
    guard let rd = parseISO(iso) else { return nil }
    let rem = max(0, rd.timeIntervalSince(now))
    let tot = tier.totalSeconds
    guard tot > 0 else { return nil }
    return min(1.0, max(0.0, (tot - rem) / tot))
}

struct PaceInfo {
    let text: String
    let colorHex: String
}

/// "On pace" / "X% ahead" / "X% under" with color. sanduhr.py:191-197.
func paceInfo(util: Double?, iso: String?, tier: Tier, now: Date = Date()) -> PaceInfo? {
    guard let u = util, let f = paceFrac(iso, tier: tier, now: now) else { return nil }
    let diff = u - f * 100
    if abs(diff) < 5      { return PaceInfo(text: "On pace",                    colorHex: "4ade80") }
    if diff > 0           { return PaceInfo(text: "\(Int(abs(diff)))% ahead",   colorHex: "fb923c") }
    return                          PaceInfo(text: "\(Int(abs(diff)))% under",   colorHex: "60a5fa")
}

/// Returns formatted wait time (e.g. "45m") if ahead of pace, else nil.
func calculateCooldown(util: Double?, iso: String?, tier: Tier, now: Date = Date()) -> String? {
    guard let u = util, let f = paceFrac(iso, tier: tier, now: now) else { return nil }
    let waitFrac = (u / 100.0) - f
    if waitFrac <= 0 { return nil }
    let s = Int(waitFrac * tier.totalSeconds)
    let d = s / 86400, h = (s % 86400) / 3600, m = (s % 3600) / 60
    var parts: [String] = []
    if d > 0 { parts.append("\(d)d") }
    if h > 0 { parts.append("\(h)h") }
    if m > 0 || parts.isEmpty { parts.append("\(m)m") }
    return parts.joined(separator: " ")
}

/// Returns integer surplus quota percentage if under pace, else nil.
func calculateSurplus(util: Double?, iso: String?, tier: Tier, now: Date = Date()) -> Int? {
    guard let u = util, let f = paceFrac(iso, tier: tier, now: now) else { return nil }
    let surplus = (f * 100.0) - u
    if surplus <= 0 { return nil }
    return Int(surplus)
}

struct BurnInfo {
    let text: String
    let colorHex: String
}

/// Burn-rate projection — if current rate sustained, when do we hit 100%?
/// Returns nil if the period will reset before that. sanduhr.py:199-228.
func burnProjection(util: Double?, iso: String?, tier: Tier, now: Date = Date()) -> BurnInfo? {
    guard let u = util, u > 0,
          let rd = parseISO(iso),
          let f = paceFrac(iso, tier: tier, now: now), f > 0
    else { return nil }

    let ratePerFrac = u / f                       // projected % over full period
    if ratePerFrac <= 100 { return nil }          // won't hit 100% this cycle

    let secsUntilReset = max(0, rd.timeIntervalSince(now))
    let tot = tier.totalSeconds
    let fracAt100 = 100.0 / ratePerFrac
    let secsUntil100 = max(0, (fracAt100 - f) * tot)

    if secsUntil100 <= 0 {
        return BurnInfo(text: "Limit reached", colorHex: "f87171")
    }
    if secsUntil100 >= secsUntilReset {
        return nil                                // resets before limit — no warning
    }

    let s = Int(secsUntil100)
    let d = s / 86400, h = (s % 86400) / 3600, m = (s % 3600) / 60
    var parts: [String] = []
    if d > 0 { parts.append("\(d)d") }
    if h > 0 { parts.append("\(h)h") }
    if m > 0 || parts.isEmpty { parts.append("\(m)m") }
    return BurnInfo(text: "At current pace, expires in \(parts.joined(separator: " "))",
                    colorHex: "f87171")
}

import XCTest
@testable import Sanduhr

/// Tests for the pure-function helpers in UsageMath.swift.
///
/// These functions are ported straight from sanduhr.py:157-228 and drive
/// every pacing decision the widget surfaces. Locking them down here
/// gives a Swift-side parity guarantee against the Windows pacing.py
/// implementation (which already has its own pytest coverage).
final class UsageMathTests: XCTestCase {

    // Fixed "now" so tests are deterministic regardless of wall-clock.
    private let now = Date(timeIntervalSince1970: 1_000_000)

    // MARK: - parseISO

    func testParseISOHandlesFractionalSeconds() {
        XCTAssertNotNil(parseISO("2026-04-19T14:00:00.000Z"))
    }

    func testParseISOHandlesNoFractionalSeconds() {
        XCTAssertNotNil(parseISO("2026-04-19T14:00:00Z"))
    }

    func testParseISORejectsGarbage() {
        XCTAssertNil(parseISO(nil))
        XCTAssertNil(parseISO(""))
        XCTAssertNil(parseISO("not a date"))
    }

    // MARK: - timeUntil

    func testTimeUntilShowsDaysHoursMinutes() {
        let oneHourTwentyMinLater = now.addingTimeInterval(3600 + 20 * 60)
        let iso = ISO8601DateFormatter().string(from: oneHourTwentyMinLater)
        let result = timeUntil(iso, now: now)
        XCTAssertTrue(result.contains("1h"), "expected '1h' in \(result)")
        XCTAssertTrue(result.contains("20m"), "expected '20m' in \(result)")
    }

    func testTimeUntilClampsAtZero() {
        let past = now.addingTimeInterval(-3600)
        let iso = ISO8601DateFormatter().string(from: past)
        // Past dates should render as "now" or "0m" — never negative.
        let result = timeUntil(iso, now: now)
        XCTAssertFalse(result.contains("-"), "got negative duration \(result)")
    }

    func testTimeUntilHandlesNilAndEmpty() {
        XCTAssertEqual(timeUntil(nil, now: now), "—")
        XCTAssertEqual(timeUntil("", now: now), "—")
    }

    // MARK: - paceFrac

    func testPaceFracHalfwayThroughSession() {
        // 2.5 hours into a 5-hour tier → pace fraction should be 0.5
        let reset = now.addingTimeInterval(2.5 * 3600)  // 2.5h remaining
        let iso = ISO8601DateFormatter().string(from: reset)
        let frac = paceFrac(iso, tier: .fiveHour, now: now)
        XCTAssertNotNil(frac)
        XCTAssertEqual(frac!, 0.5, accuracy: 0.01)
    }

    func testPaceFracClampsToOneWhenReset() {
        let reset = now.addingTimeInterval(-3600)  // already past
        let iso = ISO8601DateFormatter().string(from: reset)
        let frac = paceFrac(iso, tier: .fiveHour, now: now)
        XCTAssertNotNil(frac)
        XCTAssertEqual(frac!, 1.0, accuracy: 0.01)
    }

    func testPaceFracReturnsNilOnBadInput() {
        XCTAssertNil(paceFrac(nil, tier: .fiveHour, now: now))
        XCTAssertNil(paceFrac("garbage", tier: .fiveHour, now: now))
    }

    // MARK: - paceInfo

    func testPaceInfoOnPace() {
        // 50% through time, 50% usage → "on pace"
        let reset = now.addingTimeInterval(2.5 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let info = paceInfo(util: 50, iso: iso, tier: .fiveHour, now: now)
        XCTAssertNotNil(info)
        XCTAssertEqual(info?.label.lowercased(), "on pace")
    }

    func testPaceInfoAheadWhenUsageOutpacesTime() {
        // 25% through time, 80% usage → way ahead
        let reset = now.addingTimeInterval(3.75 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let info = paceInfo(util: 80, iso: iso, tier: .fiveHour, now: now)
        XCTAssertNotNil(info)
        XCTAssertTrue(
            info!.label.lowercased().contains("ahead"),
            "expected 'ahead' in \(info!.label)"
        )
    }

    func testPaceInfoUnderWhenTimeOutpacesUsage() {
        // 75% through time, 20% usage → way under
        let reset = now.addingTimeInterval(1.25 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let info = paceInfo(util: 20, iso: iso, tier: .fiveHour, now: now)
        XCTAssertNotNil(info)
        XCTAssertTrue(
            info!.label.lowercased().contains("under"),
            "expected 'under' in \(info!.label)"
        )
    }
}

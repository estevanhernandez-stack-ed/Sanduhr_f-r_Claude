import Foundation
import Testing
@testable import Sanduhr

/// Tests for the pure-function helpers in `UsageMath.swift`.
///
/// Ported straight from `sanduhr.py:157-228`, these drive every pacing
/// decision the widget surfaces. Locking them down here gives a Swift-side
/// parity guarantee against the Windows `pacing.py` implementation (which
/// has its own pytest coverage).
///
/// Uses swift-testing (built into Swift 6.0+) instead of XCTest so the
/// suite runs locally on Command Line Tools without a full Xcode install.
@Suite("Usage math")
struct UsageMathTests {

    /// Fixed "now" so tests are deterministic regardless of wall-clock.
    let now = Date(timeIntervalSince1970: 1_000_000)

    // MARK: - parseISO

    @Test func parseISOHandlesFractionalSeconds() {
        #expect(parseISO("2026-04-19T14:00:00.000Z") != nil)
    }

    @Test func parseISOHandlesNoFractionalSeconds() {
        #expect(parseISO("2026-04-19T14:00:00Z") != nil)
    }

    @Test func parseISORejectsGarbage() {
        #expect(parseISO(nil) == nil)
        #expect(parseISO("") == nil)
        #expect(parseISO("not a date") == nil)
    }

    // MARK: - timeUntil

    @Test func timeUntilShowsDaysHoursMinutes() {
        let future = now.addingTimeInterval(3600 + 20 * 60)
        let iso = ISO8601DateFormatter().string(from: future)
        let result = timeUntil(iso, now: now)
        #expect(result.contains("1h"), "expected '1h' in \(result)")
        #expect(result.contains("20m"), "expected '20m' in \(result)")
    }

    @Test func timeUntilClampsAtZero() {
        let past = now.addingTimeInterval(-3600)
        let iso = ISO8601DateFormatter().string(from: past)
        let result = timeUntil(iso, now: now)
        #expect(!result.contains("-"), "got negative duration \(result)")
    }

    @Test func timeUntilHandlesNilAndEmpty() {
        #expect(timeUntil(nil, now: now) == "—")
        #expect(timeUntil("", now: now) == "—")
    }

    // MARK: - paceFrac

    @Test func paceFracHalfwayThroughSession() throws {
        // 2.5h remaining on a 5h window → 50% elapsed.
        let reset = now.addingTimeInterval(2.5 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let f = try #require(paceFrac(iso, tier: .fiveHour, now: now))
        #expect(abs(f - 0.5) < 0.01)
    }

    @Test func paceFracClampsToOneWhenReset() throws {
        let reset = now.addingTimeInterval(-3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let f = try #require(paceFrac(iso, tier: .fiveHour, now: now))
        #expect(abs(f - 1.0) < 0.01)
    }

    @Test func paceFracReturnsNilOnBadInput() {
        #expect(paceFrac(nil, tier: .fiveHour, now: now) == nil)
        #expect(paceFrac("garbage", tier: .fiveHour, now: now) == nil)
    }

    // MARK: - paceInfo
    //
    // The Windows-authored originals referenced `.label`, but `PaceInfo` on
    // Mac exposes `.text`. Fixed in this migration — same semantics.

    @Test func paceInfoOnPace() throws {
        // 50% through time, 50% usage → "on pace"
        let reset = now.addingTimeInterval(2.5 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let info = try #require(paceInfo(util: 50, iso: iso, tier: .fiveHour, now: now))
        #expect(info.text.lowercased() == "on pace")
    }

    @Test func paceInfoAheadWhenUsageOutpacesTime() throws {
        // 25% through time, 80% usage → "ahead".
        let reset = now.addingTimeInterval(3.75 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let info = try #require(paceInfo(util: 80, iso: iso, tier: .fiveHour, now: now))
        #expect(info.text.lowercased().contains("ahead"),
                "expected 'ahead' in \(info.text)")
    }

    @Test func paceInfoUnderWhenTimeOutpacesUsage() throws {
        // 75% through time, 20% usage → "under".
        let reset = now.addingTimeInterval(1.25 * 3600)
        let iso = ISO8601DateFormatter().string(from: reset)
        let info = try #require(paceInfo(util: 20, iso: iso, tier: .fiveHour, now: now))
        #expect(info.text.lowercased().contains("under"),
                "expected 'under' in \(info.text)")
    }
}

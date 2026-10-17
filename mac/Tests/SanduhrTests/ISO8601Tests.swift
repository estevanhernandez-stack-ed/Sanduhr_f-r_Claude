import Foundation
import Testing
@testable import Sanduhr

/// The one ISO 8601 stamp and parser behind the handoff files, snapshot.json and the meter
/// history: milliseconds or whole seconds out, either form (and a numeric offset) back in.
@Suite("ISO 8601 stamps")
struct ISO8601Tests {
    /// 2026-10-04T17:03:45Z.
    static let fixed = Date(timeIntervalSince1970: 1_791_133_425)

    @Test func stampsMillisecondsByDefault() {
        #expect(ISO8601.stamp(Self.fixed) == "2026-10-04T17:03:45.000Z")
        #expect(ISO8601.stamp(Self.fixed.addingTimeInterval(0.25)) == "2026-10-04T17:03:45.250Z")
    }

    @Test func stampsWholeSecondsOnRequest() {
        #expect(ISO8601.stamp(Self.fixed, fractional: false) == "2026-10-04T17:03:45Z")
        // Cut, not rounded: a whole-second stamp never runs ahead of its reading.
        #expect(ISO8601.stamp(Self.fixed.addingTimeInterval(0.75), fractional: false) == "2026-10-04T17:03:45Z")
    }

    @Test func bothFormsParseBackToTheInstant() {
        #expect(ISO8601.parse("2026-10-04T17:03:45.000Z") == Self.fixed)
        #expect(ISO8601.parse("2026-10-04T17:03:45Z") == Self.fixed)
        #expect(ISO8601.parse(ISO8601.stamp(Self.fixed)) == Self.fixed)
        #expect(ISO8601.parse(ISO8601.stamp(Self.fixed, fractional: false)) == Self.fixed)
        #expect(ISO8601.parse("2026-10-04T17:03:45.250Z") == Self.fixed.addingTimeInterval(0.25))
    }

    @Test func numericOffsetsParse() {
        #expect(ISO8601.parse("2026-10-04T19:03:45+02:00") == Self.fixed)
        #expect(ISO8601.parse("2026-10-04T19:03:45.250+02:00") == Self.fixed.addingTimeInterval(0.25))
        #expect(ISO8601.parse("2026-10-04T17:03:45.000+00:00") == Self.fixed)
    }

    @Test func longFractionsAndWhitespaceParse() {
        // .NET writes seven fraction digits; the history's Windows points carry six.
        #expect(ISO8601.parse("2026-10-04T17:03:45.2500000+00:00") == Self.fixed.addingTimeInterval(0.25))
        #expect(ISO8601.parse("2026-10-04T17:03:45.250000Z") == Self.fixed.addingTimeInterval(0.25))
        #expect(ISO8601.parse(" 2026-10-04T17:03:45Z\n") == Self.fixed)
    }

    @Test func garbageIsNil() {
        for s in ["", "   ", "yesterday", "2026-10-04", "2026-10-04 17:03:45", "1791133425"] {
            #expect(ISO8601.parse(s) == nil, "\(s)")
        }
    }
}

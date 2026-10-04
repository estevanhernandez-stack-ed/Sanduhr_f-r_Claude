import Foundation
import Testing
@testable import Sanduhr

/// Desk message effects (item 54): the markup, strict for Claude's proposals and lenient on the
/// Desk, and what each effect means on screen (Reduce Motion, out of sight). Made-up lines only.
// MARK: - Effects markup

@Suite("Message effects markup")
struct MessageMarkupTests {
    @Test func tagsInAnyOrder() throws {
        let p = try MessageMarkup.parseStrict("{ink:#ff2a6d,#05d9e8} {glow}{size:1.5}  {write} {shimmer} hello there").get()
        #expect(p.text == "hello there")
        #expect(p.effects.ink == ["ff2a6d", "05d9e8"])
        #expect(p.effects.glow == true)
        #expect(p.effects.size == 1.5)
        #expect(p.effects.write && p.effects.shimmer)
        let q = try MessageMarkup.parseStrict("{SHIMMER} {noglow} {ink:fff} x").get()
        #expect(q.effects.glow == false)
        #expect(q.effects.shimmer)
        #expect(q.effects.ink == ["fff"])
    }

    @Test func plainLinesAndMidLineBraces() {
        #expect(MessageMarkup.parse("keep building.") == .init(text: "keep building."))
        #expect(MessageMarkup.parse("hello {glow} there").text == "hello {glow} there")
        #expect(MessageMarkup.parse("hello {glow} there").effects == MessageEffects())
    }

    @Test func strictFailuresAreNamed() {
        let cases: [(String, MessageMarkup.Failure)] = [
            ("{blink} hi", .unknown("blink")),
            ("{glow hi", .unclosed),
            ("{glow:yes} hi", .takesNoValue("glow")),
            ("{ink:} hi", .inkMissing),
            ("{ink} hi", .inkMissing),
            ("{ink:#1,#2} hi", .inkNotHex),
            ("{ink:#111,#222,#333,#444,#555} hi", .inkTooMany),
            ("{size:3} hi", .size),
            ("{size:0.4} hi", .size),
            ("{size:big} hi", .size),
            ("{size} hi", .size),
            ("{glow} {write}", .noText),
        ]
        for (line, want) in cases {
            #expect(MessageMarkup.parseStrict(line) == .failure(want), "\(line)")
        }
        #expect(MessageMarkup.parseStrict("{size:0.5} a").map(\.effects.size) == .success(0.5))
        #expect(MessageMarkup.parseStrict("{size:2} a").map(\.effects.size) == .success(2))
    }

    /// The Desk never hides a line and never drops text: a bad tag and the rest draw as written.
    @Test func lenientDrawsBadTagsAsText() {
        let p = MessageMarkup.parse("{glow} {blink} hello")
        #expect(p.effects.glow == true)
        #expect(p.text == "{blink} hello")
        #expect(MessageMarkup.parse("{ink:nothex} hi").text == "{ink:nothex} hi")
        #expect(MessageMarkup.parse("{glow} {write}").text == "{glow} {write}")
        #expect(MessageMarkup.parse("{glow} {write}").effects == MessageEffects())
        #expect(MessageMarkup.parse("{unclosed hello").text == "{unclosed hello")
        #expect(MessageMarkup.parse("").text == "")
    }
}

@Suite("Message effects on screen")
struct MessageMotionTests {
    @Test func inkGlowAndSize() {
        var e = MessageEffects()
        #expect(MessageMotion.inkSpec(e, global: "9ad7ff") == "9ad7ff")
        #expect(MessageMotion.glows(e, global: true))
        #expect(!MessageMotion.glows(e, global: false))
        #expect(MessageMotion.size(e, base: 84) == 84)
        e.ink = ["ff2a6d", "05d9e8"]
        e.glow = false
        e.size = 1.5
        #expect(MessageMotion.inkSpec(e, global: "9ad7ff") == "ff2a6d,05d9e8")
        #expect(!MessageMotion.glows(e, global: true))
        #expect(MessageMotion.size(e, base: 84) == 126)
        e.glow = true
        #expect(MessageMotion.glows(e, global: false))
    }

    @Test func reduceMotionAndPause() {
        var e = MessageEffects()
        #expect(!MessageMotion.animatesWrite(e, reduceMotion: false))
        #expect(!MessageMotion.shimmers(e, reduceMotion: false, paused: false))
        e.write = true
        e.shimmer = true
        #expect(MessageMotion.animatesWrite(e, reduceMotion: false))
        #expect(!MessageMotion.animatesWrite(e, reduceMotion: true))
        #expect(MessageMotion.shimmers(e, reduceMotion: false, paused: false))
        #expect(!MessageMotion.shimmers(e, reduceMotion: true, paused: false))
        #expect(!MessageMotion.shimmers(e, reduceMotion: false, paused: true))
    }

    @Test func outOfSight() {
        #expect(!MessageMotion.paused(deskVisible: true, screensAsleep: false, screenSaver: false, sessionAway: false))
        #expect(MessageMotion.paused(deskVisible: false, screensAsleep: false, screenSaver: false, sessionAway: false))
        #expect(MessageMotion.paused(deskVisible: true, screensAsleep: true, screenSaver: false, sessionAway: false))
        #expect(MessageMotion.paused(deskVisible: true, screensAsleep: false, screenSaver: true, sessionAway: false))
        #expect(MessageMotion.paused(deskVisible: true, screensAsleep: false, screenSaver: false, sessionAway: true))
    }

    @Test func timings() {
        #expect(MessageMotion.writeDuration == 1.5)
        #expect(MessageMotion.shimmerPeriod == 8)
        #expect(MessageMotion.shimmerSweep < MessageMotion.shimmerPeriod)
    }

    /// The Desk's pick keeps the tags; the view reads them.
    @Test func pickKeepsTagsAfterThePrefix() {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let sunday = Date(timeIntervalSince1970: 1_785_067_200)   // 2026-07-26 12:00 UTC, a Sunday
        let text = "{ink:#fff} plain\nSun: {glow} rest.\n"
        #expect(MessageEngine.pick(from: text, now: sunday, hourly: false, calendar: cal) == "{glow} rest.")
        #expect(MessageEngine.splitPrefix("{ink:#fff} plain").kind == .plain)
        #expect(MessageEngine.splitPrefix("10-31: boo").kind == .date)
        #expect(MessageEngine.splitPrefix("Note: plain").body == "Note: plain")
    }
}

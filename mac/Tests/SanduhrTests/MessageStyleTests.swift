import Foundation
import Testing
@testable import Sanduhr

/// Item 65 (a) and (b): `{font:…}` and `{sweep}` on the Desk message: the tags, the Unicode letter
/// styles with their Letterlike holes, how each style draws in the line's own font, and when the
/// sweep's light moves. Made-up lines only.

@Suite("Message font and sweep tags")
struct MessageStyleTagTests {
    @Test func fontTagsTakeTheStatuslineNames() throws {
        for style in LetterStyle.allCases {
            let p = try MessageMarkup.parseStrict("{font:\(style.rawValue)} hi").get()
            #expect(p.effects.font == style)
            #expect(p.text == "hi")
        }
        // Case, spaces, hyphens and underscores don't matter.
        #expect(LetterStyle(tag: "smallcaps") == .smallCaps)
        #expect(LetterStyle(tag: "Small Caps") == .smallCaps)
        #expect(LetterStyle(tag: "bold_italic") == .boldItalic)
        #expect(LetterStyle(tag: "DOUBLESTRUCK") == .doubleStruck)
        #expect(LetterStyle(tag: "outline") == nil)
        #expect(LetterStyle.tagNames == "bold, italic, bold-italic, sans, mono, double-struck, script, fraktur, small-caps")
    }

    @Test func sweepOnceOrOnAPeriod() throws {
        let once = try MessageMarkup.parseStrict("{sweep} {font:smallcaps} {ink:#ff2a6d} hi.").get()
        #expect(once.effects.sweep && once.effects.sweepPeriod == nil)
        #expect(once.effects.font == .smallCaps)
        #expect(once.text == "hi.")
        let every = try MessageMarkup.parseStrict("{SWEEP:20} hi").get()
        #expect(every.effects.sweep && every.effects.sweepPeriod == 20)
        #expect(MessageMarkup.parseStrict("{sweep:2} a").map(\.effects.sweepPeriod) == .success(2))
        #expect(MessageMarkup.parseStrict("{sweep:3600} a").map(\.effects.sweepPeriod) == .success(3600))
    }

    @Test func badFontAndSweepTagsAreNamed() {
        let cases: [(String, MessageMarkup.Failure)] = [
            ("{font} hi", .font),
            ("{font:} hi", .font),
            ("{font:outline} hi", .font),
            ("{font:comic} hi", .font),
            ("{sweep:} hi", .sweep),
            ("{sweep:1} hi", .sweep),
            ("{sweep:3601} hi", .sweep),
            ("{sweep:fast} hi", .sweep),
            ("{sweep:-5} hi", .sweep),
        ]
        for (line, want) in cases {
            #expect(MessageMarkup.parseStrict(line) == .failure(want), "\(line)")
        }
        #expect(MessageMarkup.Failure.font.reason.contains("small-caps"))
        #expect(MessageMarkup.Failure.sweep.reason.contains("2 to 3600"))
        #expect(MessageMarkup.Failure.unknown("x").reason.hasSuffix("shimmer, sweep, font"))
    }

    /// Unknown or malformed tags draw as written, as before.
    @Test func lenientDrawsBadStyleTagsAsText() {
        let p = MessageMarkup.parse("{sweep} {font:comic} hi")
        #expect(p.effects.sweep)
        #expect(p.effects.font == nil)
        #expect(p.text == "{font:comic} hi")
        #expect(MessageMarkup.parse("{sweep:0.5} hi").text == "{sweep:0.5} hi")
        #expect(MessageMarkup.parse("{font:bold}").text == "{font:bold}")
    }
}

@Suite("Unicode letter styles")
struct LetterMapTests {
    @Test func lettersAndDigits() {
        #expect(LetterMap.map("Ab 09", .bold) == "𝐀𝐛 𝟎𝟗")
        #expect(LetterMap.map("Ab 09", .sans) == "𝖠𝖻 𝟢𝟫")
        #expect(LetterMap.map("Ab 09", .mono) == "𝙰𝚋 𝟶𝟿")
        #expect(LetterMap.map("Ab 09", .doubleStruck) == "𝔸𝕓 𝟘𝟡")
        // No digits in the block for these: they stay.
        #expect(LetterMap.map("Az 09", .italic) == "𝐴𝑧 09")
        #expect(LetterMap.map("Az 09", .boldItalic) == "𝑨𝒛 09")
        #expect(LetterMap.map("Az 09", .script) == "𝒜𝓏 09")
        #expect(LetterMap.map("Az 09", .fraktur) == "𝔄𝔷 09")
    }

    /// The letters Mathematical Alphanumerics leaves out come from Letterlike Symbols.
    @Test func letterlikeHolesAreFilled() {
        #expect(LetterMap.map("h", .italic) == "ℎ")
        #expect(LetterMap.map("BEFHILMR ego", .script) == "ℬℰℱℋℐℒℳℛ ℯℊℴ")
        #expect(LetterMap.map("CHIRZ", .fraktur) == "ℭℌℑℜℨ")
        #expect(LetterMap.map("CHNPQRZ", .doubleStruck) == "ℂℍℕℙℚℝℤ")
        // Every mapped character is one assigned scalar: no reserved code point in a hole.
        for style in LetterStyle.allCases {
            for ch in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" {
                let out = LetterMap.letter(ch, style)
                #expect(out.unicodeScalars.count == 1)
                #expect(out.unicodeScalars.first?.properties.generalCategory != .unassigned, "\(style) \(ch)")
            }
        }
    }

    @Test func smallCapsAndWhatStays() {
        #expect(LetterMap.map("Hello, x 42", .smallCaps) == "Hᴇʟʟᴏ, x 42")
        #expect(LetterMap.map("ünïcode ✨ – é", .bold) == "ü𝐧ï𝐜𝐨𝐝𝐞 ✨ – é")
        #expect(LetterMap.map("as written", nil) == "as written")
    }
}

@Suite("Message typography")
struct MessageTypographyTests {
    @Test func boldUsesTheFamilysBoldFace() {
        let este = MessageTypography.plan("hi", style: .bold, family: BundledFonts.family)
        #expect(este.face == .custom(BundledFonts.boldFace))
        #expect(!este.bold)
        #expect(este.slant == 0)
        let other = MessageTypography.plan("hi", style: .bold, family: "Avenir Next")
        #expect(other.face == .custom("Avenir Next"))
        #expect(other.bold)
    }

    @Test func italicIsASlantAndBoldItalicBoth() {
        let italic = MessageTypography.plan("hi", style: .italic, family: BundledFonts.family)
        #expect(italic.face == .custom(BundledFonts.family))
        #expect(italic.slant == 12)
        #expect(italic.text == "hi")
        let both = MessageTypography.plan("hi", style: .boldItalic, family: BundledFonts.family)
        #expect(both.face == .custom(BundledFonts.boldFace))
        #expect(both.slant == 12)
    }

    @Test func smallCapsAreCapitalsAtXHeight() {
        let plan = MessageTypography.plan("Hi there.", style: .smallCaps, family: BundledFonts.family)
        #expect(plan.smallCaps && plan.text == "Hi there.")
        #expect(plan.face == .custom(BundledFonts.family))
        let runs = MessageTypography.smallCapsRuns("Hi there.").map { "\($0.small ? "s" : "C"):\($0.text)" }
        #expect(runs == ["C:H", "s:I", "C: ", "s:THERE", "C:."])
        #expect(MessageTypography.smallCapsScale(xHeight: 50, capHeight: 70) == CGFloat(50) / CGFloat(70))
        #expect(MessageTypography.smallCapsScale(xHeight: 10, capHeight: 70) == 0.6)
        #expect(MessageTypography.smallCapsScale(xHeight: 70, capHeight: 70) == 0.85)
        #expect(MessageTypography.smallCapsScale(xHeight: 0, capHeight: 70) == MessageTypography.smallCapsFallback)
    }

    @Test func unicodeStylesDrawInTheSystemFont() {
        for style in [LetterStyle.sans, .mono, .doubleStruck, .script, .fraktur] {
            let plan = MessageTypography.plan("Ab", style: style, family: BundledFonts.family)
            #expect(plan.face == .system)
            #expect(plan.text == LetterMap.map("Ab", style))
            #expect(plan.slant == 0 && !plan.bold && !plan.smallCaps)
        }
        #expect(MessageTypography.plan("Ab", style: nil, family: "Avenir") == .init(text: "Ab", face: .custom("Avenir")))
    }

    /// The notch reads the line without its tags, Unicode styles applied; typographic ones are
    /// drawn by the view, so their characters stay as written.
    @Test func notchShowsTheLineWithoutTags() {
        let now = Date(timeIntervalSince1970: 1_785_067_200)
        func notch(_ raw: String) -> String? {
            NotchContent.message.text(at: .strip, meetings: [], meters: nil, message: raw, now: now)
        }
        #expect(notch("{sweep} {glow} ship it.") == "ship it.")
        #expect(notch("{font:fraktur} Hi") == "ℌ𝔦")
        #expect(notch("{font:smallcaps} hi") == "hi")
        #expect(notch("{blink} hi") == "{blink} hi")
        #expect(notch("  ") == nil)
    }
}

@Suite("Message sweep")
struct MessageSweepTests {
    @Test func timingFollowsTheNowPlayingLight() {
        #expect(MessageMotion.sweepRun == 1.1)
        #expect(MessageMotion.sweepWidth == 3)
        #expect(MessageMotion.sweepFrame < 0.1)
        // Enters before the first character and leaves past the last.
        #expect(MessageMotion.sweepAt(fraction: 0, length: 10) == -3)
        #expect(MessageMotion.sweepAt(fraction: 0.5, length: 10) == 5)
        #expect(MessageMotion.sweepAt(fraction: 1, length: 10) == nil)
        #expect(MessageMotion.sweepAt(fraction: -0.1, length: 10) == nil)
        #expect(MessageMotion.sweepAt(fraction: 0.5, length: 0) == nil)
    }

    @Test func threeCharactersBrightenTowardWhite() {
        #expect(MessageMotion.sweepLight(index: 5, at: 5) == MessageMotion.sweepStrength)
        #expect(MessageMotion.sweepLight(index: 3, at: 5) > 0)
        #expect(MessageMotion.sweepLight(index: 7, at: 5) > 0)
        #expect(MessageMotion.sweepLight(index: 8, at: 5) == 0)
        #expect(MessageMotion.sweepLight(index: 2, at: 5) == 0)
        #expect(MessageMotion.sweepLight(index: 4, at: 5) > MessageMotion.sweepLight(index: 3, at: 5))
        #expect(MessageMotion.sweepLight(index: 0, at: nil) == 0)
    }

    @Test func onceOnAppearOrOnAPeriodAndRestsUnseen() throws {
        var e = MessageEffects()
        #expect(MessageMotion.sweepPlan(e, reduceMotion: false, paused: false, sweptOnce: false, replay: false) == nil)
        e.sweep = true
        let once = try #require(MessageMotion.sweepPlan(e, reduceMotion: false, paused: false, sweptOnce: false, replay: false))
        #expect(once.first == MessageMotion.sweepAppearDelay && once.every == nil)
        // Once means once per line: back in sight after it ran, nothing moves.
        #expect(MessageMotion.sweepPlan(e, reduceMotion: false, paused: false, sweptOnce: true, replay: false) == nil)
        #expect(MessageMotion.sweepPlan(e, reduceMotion: true, paused: false, sweptOnce: false, replay: false) == nil)
        #expect(MessageMotion.sweepPlan(e, reduceMotion: false, paused: true, sweptOnce: false, replay: false) == nil)
        e.sweepPeriod = 20
        let every = try #require(MessageMotion.sweepPlan(e, reduceMotion: false, paused: false, sweptOnce: true, replay: false))
        #expect(every.every == 20)
        #expect(MessageMotion.sweepPlan(e, reduceMotion: false, paused: true, sweptOnce: false, replay: false) == nil)
        // Settings' Replay sweeps sooner.
        let replay = try #require(MessageMotion.sweepPlan(e, reduceMotion: false, paused: false, sweptOnce: false, replay: true))
        #expect(replay.first == MessageMotion.sweepReplayDelay)
        #expect(MessageMotion.sweepReplayDelay < MessageMotion.sweepAppearDelay)
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// Item 68: the shared terminal frame for what Sanduhr draws inside Claude Code.
@Suite("Terminal preview frame")
struct TerminalPreviewFrameTests {
    @Test func textIsTrimmedAndEmptyFallsBack() {
        #expect(TerminalPreviewFrame.shown("line\n") == "line")
        #expect(TerminalPreviewFrame.shown("\u{1B}[32mok\u{1B}[0m\n\n") == "\u{1B}[32mok\u{1B}[0m")
        #expect(TerminalPreviewFrame.shown("  \n") == nil)
        #expect(TerminalPreviewFrame.shown(nil) == nil)
    }

    @Test func clockStillsUnderReduceMotion() {
        #expect(TerminalPreviewFrame.period(clock: 0.5, reduceMotion: false) == 0.5)
        #expect(TerminalPreviewFrame.period(clock: 0.5, reduceMotion: true) == nil)
        #expect(TerminalPreviewFrame.period(clock: nil, reduceMotion: false) == nil)
        #expect(TerminalPreviewFrame.period(clock: 0.01, reduceMotion: false) == 0.1)
    }

    @Test func colorsAndPowerlineGlyphsAreDrawn() {
        // A green segment, a powerline arrow, then plain text: the arrow is cut out to be drawn.
        let pieces = ANSIText.pieces("\u{1B}[32mok\u{1B}[0m\u{E0B0} rest")
        #expect(pieces.count == 3)
        if case .text(let s, let style) = pieces[0] {
            #expect(s == "ok")
            #expect(style.fg == ANSIText.basic[2])
        } else {
            Issue.record("first piece is not text")
        }
        if case .glyph(let shape, _) = pieces[1] { #expect(shape == .arrow) } else { Issue.record("no glyph") }
    }
}

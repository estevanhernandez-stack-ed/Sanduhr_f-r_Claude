import SwiftUI
import Testing
@testable import Sanduhr

/// The hex fields' validator (Desk Look's Advanced, Notch text color): one six-digit color or a
/// comma gradient of two to four, and what a commit does to the saved value.
@Suite("Desk hex fields")
struct DeskHexTests {
    @Test(arguments: [
        "9ad7ff", "#9ad7ff", "9AD7FF", "#9AD7FF", "  9ad7ff  ",
        "5b8cff,a86bff", "5b8cff, a86bff", " #5B8CFF ,a86bff ",
        "8f5bd6,3a63e0,33fdff", "531b93,012089,00fdff,ffffff",
    ])
    func acceptsOneColorOrAGradientOfTwoToFour(_ text: String) {
        #expect(DeskHex.problem(text) == nil)
    }

    @Test(arguments: ["blue", "9ad7f", "#12345g", "", "   ", "9ad7ff7", "fff"])
    func rejectsWhatIsNotSixHexDigits(_ text: String) {
        #expect(DeskHex.problem(text) == DeskHex.hint)
    }

    @Test(arguments: [
        "5b8cff,blue", "5b8cff,", ",5b8cff", "5b8cff,a86bff,x",
        "111111,222222,333333,444444,555555",
    ])
    func aBadPartOrFivePartsSaysGradient(_ text: String) {
        #expect(DeskHex.problem(text) == DeskHex.gradientHint)
    }

    @Test func aBadCommitKeepsTheSavedValueAndSaysWhy() {
        let kept = DeskHex.commit("blue", saved: "9ad7ff")
        #expect(kept.value == "9ad7ff")
        #expect(kept.problem == DeskHex.hint)
        let gradient = DeskHex.commit("5b8cff,blue", saved: "8f5bd6,3a63e0,33fdff")
        #expect(gradient.value == "8f5bd6,3a63e0,33fdff")
        #expect(gradient.problem == DeskHex.gradientHint)
    }

    @Test func aGoodCommitSavesTheTrimmedText() {
        #expect(DeskHex.commit("  #9AD7FF ", saved: "ffffff") == ("#9AD7FF", nil))
        #expect(DeskHex.commit("5b8cff, a86bff", saved: "ffffff") == ("5b8cff, a86bff", nil))
    }

    @Test func whatACommitSavesStillRenders() {
        // The saved forms go through the same parsers the desktop and the notch use.
        #expect(Color.hex("#9AD7FF") == Color.hex("9ad7ff"))
        #expect(DeskThemeMapping.inkHexes("5b8cff, a86bff") == ["5b8cff", "a86bff"])
    }
}

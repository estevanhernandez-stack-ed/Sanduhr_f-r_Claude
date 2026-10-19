import SwiftUI
import Testing
@testable import Sanduhr

/// The hex fields' validator (Desk Look's Advanced, Notch text color): one color of six digits or
/// three as shorthand, or a comma gradient of two to four, and what a commit does to the saved value.
@Suite("Desk hex fields")
struct DeskHexTests {
    @Test(arguments: [
        "9ad7ff", "#9ad7ff", "9AD7FF", "#9AD7FF", "  9ad7ff  ",
        "fff", "#fff", "9AD", " 9ad ",
        "5b8cff,a86bff", "5b8cff, a86bff", " #5B8CFF ,a86bff ", "5b8cff, a86",
        "8f5bd6,3a63e0,33fdff", "531b93,012089,00fdff,ffffff",
    ])
    func acceptsOneColorOrAGradientOfTwoToFour(_ text: String) {
        #expect(DeskHex.problem(text) == nil)
    }

    @Test(arguments: ["blue", "9ad7f", "9ad7", "#12345g", "ggg", "", "   ", "#", "9ad7ff7"])
    func rejectsWhatIsNotThreeOrSixHexDigits(_ text: String) {
        #expect(DeskHex.problem(text) == DeskHex.hint)
    }

    @Test(arguments: [
        "5b8cff,blue", "5b8cff,", ",5b8cff", "5b8cff,a86bff,x", "5b8cff,a86b", "5b8cff,a86bf",
        "111111,222222,333333,444444,555555",
    ])
    func aBadPartOrFivePartsSaysGradient(_ text: String) {
        #expect(DeskHex.problem(text) == DeskHex.gradientHint)
    }

    @Test func theHintsSayThreeOrSix() {
        #expect(DeskHex.hint.hasPrefix("Three or six hex digits"))
        #expect(DeskHex.gradientHint.hasPrefix("Three or six hex digits"))
        #expect(DeskHex.gradientHint.contains("comma list for a gradient"))
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

    @Test func aShorthandCommitSavesSixDigits() {
        #expect(DeskHex.commit("fff", saved: "000000") == ("ffffff", nil))
        #expect(DeskHex.commit("9ad", saved: "000000") == ("99aadd", nil))
        #expect(DeskHex.commit(" #9ad ", saved: "000000") == ("#99aadd", nil))
    }

    @Test func aMixedCaseShorthandExpandsAsTyped() {
        #expect(DeskHex.commit("9AD", saved: "000000") == ("99AADD", nil))
        #expect(DeskHex.commit("#FfF", saved: "000000") == ("#FFffFF", nil))
    }

    @Test func aGradientExpandsOnlyItsShorthandEntries() {
        #expect(DeskHex.commit("5b8cff, a86", saved: "000000") == ("5b8cff, aa8866", nil))
        #expect(DeskHex.commit("fff ,#9AD7FF, 123", saved: "000000") == ("ffffff ,#9AD7FF, 112233", nil))
    }

    @Test(arguments: ["9ad7", "9ad7f", "5b8cff,a86b"])
    func fourAndFiveDigitsAreStillRefusedOnCommit(_ text: String) {
        let kept = DeskHex.commit(text, saved: "9ad7ff")
        #expect(kept.value == "9ad7ff")
        #expect(kept.problem != nil)
    }

    @Test func whatACommitSavesStillRenders() {
        // The saved forms go through the same parsers the desktop and the notch use.
        #expect(Color.hex("#9AD7FF") == Color.hex("9ad7ff"))
        #expect(DeskThemeMapping.inkHexes("5b8cff, a86bff") == ["5b8cff", "a86bff"])
        #expect(Color.hex(DeskHex.normalized("9ad")) == Color.hex("99aadd"))
        // A shorthand saved before expansion arrived keeps rendering as it did.
        #expect(Color.hex("fff") == Color.hex("ffffff"))
        #expect(DeskThemeMapping.inkHexes("fff, 9ad") == ["ffffff", "99aadd"])
    }
}

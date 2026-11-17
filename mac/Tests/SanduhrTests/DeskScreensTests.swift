import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

private let idA = "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6"
private let idB = "7A1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F7"
private let idC = "8B1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F8"

private func scr(_ id: String?, _ x: Double, _ y: Double, _ w: Double, _ h: Double, primary: Bool = false) -> DeskScreenInfo {
    DeskScreenInfo(id: id, frame: CGRect(x: x, y: y, width: w, height: h), primary: primary)
}

private func names(_ s: [DeskScreenInfo]) -> [String] { DeskScreens.named(s).map(\.name) }

@Suite("Desk screens, 2.44")
struct DeskScreensTests {
    @Test func mainIsNamedBySize() {
        #expect(names([scr(idA, 0, 0, 1512, 982, primary: true)]) == ["1512×982, main"])
    }

    @Test func rightLeftAboveBelow() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        #expect(names([main, scr(idB, 1920, 0, 1280, 720)])[1] == "1280×720, right of main")
        #expect(names([main, scr(idB, -1280, 0, 1280, 720)])[1] == "1280×720, left of main")
        #expect(names([main, scr(idB, 0, 1080, 1920, 1080)])[1] == "1920×1080, above main")
        #expect(names([main, scr(idB, 0, -1080, 1920, 1080)])[1] == "1920×1080, below main")
    }

    @Test func overlapIsNamedByTopLeftPosition() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        // Left edge 100 from main's left; top edge 1080-(100+500)=480 from main's top.
        let n = names([main, scr(idB, 100, 100, 800, 500)])
        #expect(n[1] == "800×500, at 100,480")
        // Mirrored: same frame as main.
        #expect(names([main, scr(idB, 0, 0, 1920, 1080)])[1] == "1920×1080, at 0,0")
    }

    @Test func twinsGetNumbersLeftToRightThenTopToBottom() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        let r1 = scr(idB, 1920, 0, 1280, 720)
        let r2 = scr(idC, 3200, 0, 1280, 720)
        // Input order has the farther one first; names follow screen order, result keeps input order.
        let n = names([main, r2, r1])
        #expect(n[1] == "1280×720, right of main (2)")
        #expect(n[2] == "1280×720, right of main")
        // Same left edge: the higher one (larger maxY) is first.
        let top = scr(idB, 1920, 500, 1280, 720)
        let low = scr(idC, 1920, -400, 1280, 720)
        let m = names([main, low, top])
        #expect(m[2] == "1280×720, right of main")
        #expect(m[1] == "1280×720, right of main (2)")
    }

    @Test func mainIsFlaggedPrimaryThenOriginThenFirst() {
        let a = scr(idA, 0, 0, 1920, 1080)
        let b = scr(idB, 1920, 0, 1280, 720, primary: true)
        #expect(DeskScreens.main([a, b])?.id == idB)
        #expect(DeskScreens.main([scr(idA, 1920, 0, 1, 1), a.with(id: idC)])?.id == idC)
        #expect(DeskScreens.main([scr(idA, 10, 10, 5, 5), scr(idB, 30, 0, 5, 5)])?.id == idA)
        #expect(DeskScreens.main([]) == nil)
        #expect(names([a, b])[1] == "1280×720, main")
        #expect(names([a, b])[0] == "1920×1080, left of main")
    }

    @Test func missingDisplayFallsBackToMain() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        let other = scr(idB, 1920, 0, 1280, 720)
        let connected = [main, other]
        let gone = DeskDisplay.id(idC)
        #expect(DeskScreens.showsOn(gone, screen: main, connected: connected))
        #expect(!DeskScreens.showsOn(gone, screen: other, connected: connected))
        // The display returns: it draws there and no longer on main.
        let back = [main, other, scr(idC, -800, 0, 800, 600)]
        #expect(!DeskScreens.showsOn(gone, screen: main, connected: back))
        #expect(DeskScreens.showsOn(gone, screen: back[2], connected: back))
    }

    @Test func idsMatchWithoutCase() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        let other = scr(idB, 1920, 0, 1280, 720)
        #expect(DeskScreens.showsOn(.id(idB.lowercased()), screen: other, connected: [main, other]))
        #expect(!DeskScreens.showsOn(.id(idB.lowercased()), screen: main, connected: [main, other]))
    }

    @Test func mainAndAll() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        let other = scr(idB, 1920, 0, 1280, 720)
        let c = [main, other]
        #expect(DeskScreens.showsOn(.main, screen: main, connected: c))
        #expect(!DeskScreens.showsOn(.main, screen: other, connected: c))
        #expect(DeskScreens.showsOn(.all, screen: main, connected: c))
        #expect(DeskScreens.showsOn(.all, screen: other, connected: c))
    }

    @Test func screensWithPieces() {
        let main = scr(idA, 0, 0, 1920, 1080, primary: true)
        let b = scr(idB, 1920, 0, 1280, 720)
        let c = scr(idC, -800, 0, 800, 600)
        func ids(_ a: DeskArrangement, _ s: [DeskScreenInfo]) -> [String?] { DeskScreens.screensWithPieces(a, s).map(\.id) }

        #expect(ids(DeskArrangement("clock:tl"), [main]) == [idA])
        #expect(ids(DeskArrangement("clock:tl meters:tr"), [main, b]) == [idA])
        #expect(ids(DeskArrangement("clock:tl@all"), [main, b]) == [idA, idB])
        #expect(ids(DeskArrangement("clock:tl meters:tr@\(idC)"), [main, b, c]) == [idA, idC])
        #expect(ids(DeskArrangement("clock:tl meters:tr@\(UUID().uuidString)"), [main, b]) == [idA])
        #expect(ids(DeskArrangement(""), [b, main]) == [idA])
    }
}

private extension DeskScreenInfo {
    func with(id: String?) -> DeskScreenInfo { var s = self; s.id = id; return s }
}

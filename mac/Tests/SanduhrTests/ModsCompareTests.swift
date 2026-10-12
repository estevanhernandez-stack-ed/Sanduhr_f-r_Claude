import Foundation
import Testing
@testable import Sanduhr

/// Item 71, slice 1d: two seats side by side, from the Catalog's cards. Made-up seats only.
@Suite("Mods & Config: Compare")
struct ModsCompareTests {
    static func card(_ kind: CatalogKind, _ name: String, _ chips: [(String, SeatMark, CatalogChipState)]) -> CatalogCard {
        var c = CatalogCard(kind: kind, name: name)
        c.chips = chips.map { CatalogChip(seat: $0.0, label: $0.0, mark: $0.1, state: $0.2) }
        return c
    }

    @Test func sortsEveryCardIntoOneBucket() {
        let cards = [
            Self.card(.plugin, "both-on", [("/a", .work, .on), ("/b", .personal, .on)]),
            Self.card(.plugin, "on-off", [("/a", .work, .on), ("/b", .personal, .off)]),
            Self.card(.plugin, "projects", [("/a", .work, .projects(2)), ("/b", .personal, .projects(1))]),
            Self.card(.skill, "a-only", [("/a", .work, .on)]),
            Self.card(.agent, "b-only", [("/b", .personal, .on)]),
            Self.card(.mcpServer, "elsewhere", [("/c", .personal, .on)]),
            Self.card(.plugin, "available", []),
        ]
        let cmp = ConfigCatalog.compare(cards, a: "/a", b: "/b")
        #expect(cmp.onlyA.map(\.card.name) == ["a-only"])
        #expect(cmp.onlyB.map(\.card.name) == ["b-only"])
        #expect(cmp.differ.map(\.card.name) == ["on-off", "projects"])
        #expect(cmp.same.map(\.card.name) == ["both-on"])
        #expect(cmp.differ[0].a == .on && cmp.differ[0].b == .off)
        #expect(cmp.counts == [1, 1, 2, 1])
        // Swapping the seats swaps the buckets.
        let back = ConfigCatalog.compare(cards, a: "/b", b: "/a")
        #expect(back.onlyA.map(\.card.name) == ["b-only"] && back.onlyB.map(\.card.name) == ["a-only"])
        #expect(!SeatComparison.Bucket.same.startsOpen && SeatComparison.Bucket.onlyA.startsOpen)
    }

    @Test func demoModeComparesWithoutAWorkSeatsCards() {
        let cards = [
            Self.card(.plugin, "work-only", [("/a", .work, .on)]),
            Self.card(.plugin, "shared", [("/a", .work, .on), ("/b", .personal, .on), ("/c", .personal, .off)]),
        ]
        let shown = cards.compactMap { $0.demoView() }
        let cmp = ConfigCatalog.compare(shown, a: "/b", b: "/c")
        #expect(cmp.differ.map(\.card.name) == ["shared"])
        #expect(cmp.counts == [0, 0, 1, 0])
        #expect(!shown.flatMap(\.chips).contains { $0.mark == .work })
    }

    static func seat(_ path: String, _ mark: SeatMark) -> Seat {
        let store = MemoryDefaults()
        SeatPreferences.setMark(mark, for: path, in: store)
        return Seats.seat(path, origin: .found, home: "/nowhere", store: store)
    }

    @Test func thePairFallsBackAndNeverPicksOneSeatTwice() {
        let w = Self.seat("/w", .work), p = Self.seat("/p", .personal), q = Self.seat("/q", .personal)
        // No picks: the first two.
        #expect(ModsPageModel.pair([w, p, q], a: nil, b: nil).map { [$0.a.path, $0.b.path] } == ["/w", "/p"])
        // The same seat picked twice: the second falls back to another.
        #expect(ModsPageModel.pair([w, p, q], a: "/q", b: "/q").map { [$0.a.path, $0.b.path] } == ["/q", "/w"])
        // A pick that's gone, or hidden: the first shown seat that isn't the other one.
        #expect(ModsPageModel.pair([p, q], a: "/w", b: "/p").map { [$0.a.path, $0.b.path] } == ["/p", "/q"])
        // Fewer than two seats: nothing to compare.
        #expect(ModsPageModel.pair([p], a: nil, b: nil) == nil)
    }

    @Test func pickingTheOtherSidesSeatSwaps() {
        #expect(ModsPageModel.picked("/b", first: true, a: "/a", b: "/b") == ("/b", "/a"))
        #expect(ModsPageModel.picked("/a", first: false, a: "/a", b: "/b") == ("/b", "/a"))
        #expect(ModsPageModel.picked("/c", first: true, a: "/a", b: "/b") == ("/c", "/b"))
        #expect(ModsPageModel.picked("/c", first: false, a: "/a", b: "/b") == ("/a", "/c"))
    }

    @Test func demoModeDropsAWorkSeatsChipFromASharedCard() {
        // Held by the work seat and the personal one: in demo mode, compared against a second
        // personal seat, it is only in the personal one.
        let card = Self.card(.plugin, "shared", [("/a", .work, .on), ("/b", .personal, .on)])
        let shown = [card].compactMap { $0.demoView() }
        #expect(shown.first?.chips.map(\.seat) == ["/b"])
        #expect(ConfigCatalog.compare(shown, a: "/b", b: "/c").onlyA.map(\.card.name) == ["shared"])
    }

    @Test func theSmokeHookOpensCompare() {
        #expect((try? DebugLink.action("mods-config", arg: "compare").get()) == .modsConfig(.compare, seat: nil))
        #expect(ModsTab.compare.debugName == "compare" && ModsTab.allCases.count == 3)
    }
}

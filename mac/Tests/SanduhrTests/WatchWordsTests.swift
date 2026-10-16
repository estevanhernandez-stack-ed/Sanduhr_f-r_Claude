import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice G: a work seat's watch words, on made-up names only.
@Suite("Packs, watch words")
struct WatchWordsTests {
    private func agent(_ name: String, _ text: String) -> ConfigPack.Item {
        var item = ConfigPack.Item(kind: .agent, name: name)
        item.files = [.init(path: "files/agents/\(name).md", text: text)]
        return item
    }

    @Test func aListIsTypedOnceEach() {
        #expect(WatchWords.parse("Acme, Rocketsled\nacme , x,, Wile E") == ["Acme", "Rocketsled", "Wile E"])
    }

    @Test func aRowHoldingAWordIsFlaggedAndUnchecked() {
        var plugin = ConfigPack.Item(kind: .plugin, name: "acme-tools", detail: "from acme-market")
        plugin.onByDefault = true
        let items = WatchWords.flag([
            agent("reviewer", "Reviews diffs for the ACME platform team."),
            agent("writer", "Writes notes. Acmeville is a town, not the company."),
            plugin,
        ], words: ["Acme"])
        #expect(items[0].watched == ["Acme"] && !items[0].onByDefault)
        // Whole words only: Acmeville isn't Acme.
        #expect(items[1].watched.isEmpty && items[1].onByDefault)
        // Hyphens split words, so a plugin named for it is caught.
        #expect(items[2].watched == ["Acme"] && !items[2].onByDefault)
        #expect(WatchWords.flag(items, words: []).allSatisfy { $0.watched.isEmpty })
    }

    @Test func theListStaysPerSeatInItsOwnFile() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let store = WatchWords(url: URL(fileURLWithPath: h.at("watch-words.json")))
        store.set(["Acme", "acme", "Rocketsled"], for: h.at(".claude"))
        #expect(store.words(for: h.at(".claude")) == ["Acme", "Rocketsled"])
        #expect(store.words(for: h.at(".claude-personal")).isEmpty)
        let attrs = try FileManager.default.attributesOfItem(atPath: h.at("watch-words.json"))
        #expect((attrs[.posixPermissions] as? Int) == 0o600)
        store.set([], for: h.at(".claude"))
        #expect(store.words(for: h.at(".claude")).isEmpty)
    }

    @Test func namesInTwoRowsAreSuggestedAndWordsAreNot() {
        let items = [
            agent("a", "Deploy Rocketsled to Zorblax. Run the Tests. Ask Claude."),
            agent("b", "Rocketsled builds on Zorblax nightly. Tests again."),
            agent("c", "Only once: Quuxify."),
        ]
        let words: Set<String> = ["deploy", "run", "the", "tests", "ask", "builds", "on", "nightly", "again", "only", "once"]
        #expect(WatchWords.suggest(items, known: [], dictionary: words) == ["Rocketsled", "Zorblax"])
        #expect(WatchWords.suggest(items, known: ["zorblax"], dictionary: words) == ["Rocketsled"])
    }

    @Test func aFlaggedRowStaysOutOfThePackUnlessChecked() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let items = WatchWords.flag([agent("reviewer", "For Acme."), agent("writer", "Plain.")], words: ["Acme"])
        let chosen = items.filter { $0.canTravel && $0.onByDefault }
        let url = URL(fileURLWithPath: h.at("w.sanduhrpack"))
        try ConfigPack.write(chosen, label: "Work", to: url, version: "2.22.0", fromWork: true, anyAgent: true)
        let pack = try ModPackReader.read(url)
        #expect(pack.files.keys.contains("files/agents/writer.md") && !pack.files.keys.contains("files/agents/reviewer.md"))
        let all = pack.files.values.map { String(decoding: $0, as: UTF8.self) }.joined() + String(decoding: try JSONSerialization.data(withJSONObject: pack.manifest), as: UTF8.self)
        #expect(!all.contains("Acme"))
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// The one JSON object file reader: strict JSON under a size cap, nil (with a reason from
/// `read(at:)`) for anything else.
@Suite("JSON object files")
struct JSONFilesTests {
    static func put(_ h: TempHome, _ relative: String, _ text: String) {
        ModsPageTests.put(h, relative, text)
    }

    @Test func aGoodObjectReads() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "settings.json", "{\n  \"enabledPlugins\": {\"a@b\": true},\n  \"env\": {\"X\": \"1\"}\n}\n")
        let o = try #require(JSONFiles.object(at: h.at("settings.json")))
        #expect((o["enabledPlugins"] as? [String: Any])?["a@b"] as? Bool == true)
        #expect((o["env"] as? [String: String]) == ["X": "1"])
        #expect(JSONFiles.read(at: h.at("settings.json")).object?.keys.sorted() == ["enabledPlugins", "env"])
    }

    @Test func aTopLevelArrayIsNil() {
        let h = TempHome()
        defer { h.cleanUp() }
        Self.put(h, "list.json", "[{\"a\": 1}]")
        #expect(JSONFiles.object(at: h.at("list.json")) == nil)
        if case .unreadable(let why) = JSONFiles.read(at: h.at("list.json")) {
            #expect(why == "has no JSON object at its top level")
        } else { Issue.record("an array read as an object") }
    }

    @Test func aFileOverTheCapIsNil() {
        let h = TempHome()
        defer { h.cleanUp() }
        let text = "{\"a\": \"" + String(repeating: "x", count: 2048) + "\"}"
        Self.put(h, "big.json", text)
        #expect(JSONFiles.object(at: h.at("big.json"), maxBytes: 1024) == nil)
        if case .unreadable(let why) = JSONFiles.read(at: h.at("big.json"), maxBytes: 1024) {
            #expect(why.contains("larger than"))
        } else { Issue.record("an oversized file read") }
        // The cap is inclusive: a file exactly at it reads.
        #expect(JSONFiles.object(at: h.at("big.json"), maxBytes: text.utf8.count) != nil)
    }

    @Test func aMissingFileIsNil() {
        let h = TempHome()
        defer { h.cleanUp() }
        #expect(JSONFiles.object(at: h.at("nothing.json")) == nil)
        if case .missing = JSONFiles.read(at: h.at("nothing.json")) {} else { Issue.record("a missing file wasn't .missing") }
        // A folder is unreadable, not missing.
        h.dir("folder.json")
        #expect(JSONFiles.object(at: h.at("folder.json")) == nil)
        if case .unreadable(let why) = JSONFiles.read(at: h.at("folder.json")) {
            #expect(why == "isn't a regular file")
        } else { Issue.record("a folder read") }
    }

    @Test func aTrailingCommaOrCommentIsNil() {
        let h = TempHome()
        defer { h.cleanUp() }
        // Foundation's parser lets the comma through; Claude Code's doesn't, and neither does this.
        Self.put(h, "comma.json", "{\n  \"a\": 1,\n}")
        #expect(JSONFiles.object(at: h.at("comma.json")) == nil)
        if case .unreadable(let why) = JSONFiles.read(at: h.at("comma.json")) {
            #expect(why == "isn't valid JSON (a trailing comma around line 2, column 9)")
        } else { Issue.record("a trailing comma read") }
        Self.put(h, "comment.json", "{\"a\": 1 /* x */}")
        #expect(JSONFiles.object(at: h.at("comment.json")) == nil)
        Self.put(h, "broken.json", "{\"a\": ")
        if case .unreadable(let why) = JSONFiles.read(at: h.at("broken.json")) {
            #expect(why.hasPrefix("isn't valid JSON"))
            #expect(!why.contains("\"a\""))
        } else { Issue.record("a cut-off file read") }
    }

    /// The sites that used to parse with `JSONSerialization` alone (config packs, the mod
    /// inventory) read the seat's well-formed files exactly as before: every value the same.
    @Test func wellFormedSeatFilesReadAsFoundationReadsThem() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let files = [
            "settings.json": "{\"enabledPlugins\": {\"a@b\": true, \"c@d\": false}, \"env\": {\"CLAUDE_CODE_PLUGIN_DIRS\": \"/x:/y\"}, \"model\": \"opus\"}",
            ".claude.json": TempHome.config(org: "org-1").replacingOccurrences(of: "\"numStartups\": 12,",
                                                                               with: "\"numStartups\": 12, \"mcpServers\": {\"m\": {\"command\": \"m\", \"args\": [\"-a\", 1, 2.5, null]}},"),
            "known_marketplaces.json": "{\"mk\": {\"source\": {\"source\": \"github\", \"repo\": \"o/r\"}, \"installLocation\": \"/p\"}}",
        ]
        for (name, text) in files {
            Self.put(h, name, text)
            let foundation = try #require((try JSONSerialization.jsonObject(with: Data(text.utf8))) as? NSDictionary)
            let shared = try #require(JSONFiles.object(at: h.at(name), maxBytes: ConfigPack.readCap) as NSDictionary?)
            #expect(shared.isEqual(foundation), "\(name)")
        }
    }
}

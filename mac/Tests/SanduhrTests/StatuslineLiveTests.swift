import Foundation
import Testing
@testable import Sanduhr

/// Item 63b's second pass: Join with, the split choice reaching the preview, refilling chips
/// after a live test, the folder's mods that draw status entries, and the live input. Temp
/// folders only.
@Suite("Statusline join, mods and live data")
struct StatuslineLiveTests {
    private static func b64(_ s: String) -> String { Data(s.utf8).base64EncodedString() }

    /// A file under the temp home, its folders made.
    private static func put(_ home: TempHome, _ relative: String, _ text: String) {
        home.dir((relative as NSString).deletingLastPathComponent)
        home.file(relative, text)
    }

    private static func inspection(_ lines: [(String, [String: [(String, String)]])]) -> StatuslineInspection {
        let json: [String: Any] = [
            "theirs": lines.map(\.0).joined(separator: "\n") + "\n",
            "lines": lines.map { text, splits in
                ["text": text, "auto": splits.keys.sorted().first ?? "none",
                 "splits": splits.mapValues { segs in
                     ["doubt": false, "segments": segs.map { ["matcher": $0.0, "text": $0.1] }] as [String: Any]
                 }] as [String: Any]
            },
            "mine": SanduhrSegment.allCases.map { ["name": $0.rawValue, "text": ""] },
            "notice": "",
        ]
        let data = try! JSONSerialization.data(withJSONObject: json)
        return StatuslineInspection.decode(String(decoding: data, as: UTF8.self))!
    }

    // MARK: Join with

    @Test func joinWithRoundTripsAndIsLeftOutByDefault() throws {
        let picks = StatuslinePicks(keep: ["a"], joinWith: .powerline)
        #expect(picks.json == #"{"keep":["a"],"with":"powerline"}"#)
        #expect(StatuslinePicks(base64: picks.base64) == picks)
        #expect(StatuslinePicks(drop: ["a"]).json == #"{"drop":["a"]}"#)
        let command = IntegrationInstaller.statuslineCommand(python: "python3", script: "/x/sanduhr_statusline.py", chain: "my.sh",
                                                             selection: StatuslineSelection(theirs: picks))
        #expect(IntegrationInstaller.parseStatusline(command)?.selection.theirs?.joinWith == .powerline)
        for bad in [#"{"with":"same"}"#, #"{"with":"slash"}"#, #"{"with":1}"#, #"{"with":null}"#] {
            #expect(StatuslinePicks(base64: Self.b64(bad)) == nil, "\(bad)")
        }
    }

    @Test func aSplitChoiceReachesThePreviewButNotTheCommand() {
        var c = StatuslineChips(Self.inspection([("a · b", ["dot": [("a", "a"), ("b", "b")], "none": [("a", "a · b")]])]))
        let before = c.previewSelection
        c.setSeparator(.none, line: 0)
        #expect(c.selection == StatuslineSelection())               // the command stays as it was
        #expect(c.previewSelection != before)                       // the preview composes again
        #expect(c.previewSelection.theirs?.separators == [StatuslineSeparator.none])
        #expect(c.chips(line: 0).map(\.label) == ["a · b"])          // and the chips re-split
        c.joinWith = .pipe
        #expect(c.selection.theirs == StatuslinePicks(keep: ["a"], separators: [StatuslineSeparator.none], joinWith: .pipe))
    }

    @Test func aLiveRunRefillsTheChipsAndKeepsThePicks() {
        var c = StatuslineChips(Self.inspection([("a | b", ["pipe": [("a", "a"), ("b", "b")]])]))
        c.toggle(c.chips(line: 0)[1])
        c.setSeparator(.pipe, line: 0)
        c.joinWith = .bar
        let next = Self.inspection([("a | ⎇ x | b", ["pipe": [("a", "a"), ("⎇", "⎇ x"), ("b", "b")]]),
                                    ("two", ["none": [("two", "two")]])])
        let r = c.refilled(next)
        #expect(r.chips(line: 0).map(\.kept) == [true, true, false])  // the new one shows, kept
        #expect(r.chips(line: 1).map(\.label) == ["two"])
        #expect(r.separator(line: 0) == .pipe && r.joinWith == .bar)
        #expect(r.theirs?.drop == ["b"])
    }

    // MARK: Mods

    @Test func modsThatDrawStatusEntriesAreFound() {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        let home = r.home
        home.dir(".claude/projects")
        // An @inline mod with a status entry and a setting about it.
        Self.put(home, "mods/watch/.claude-plugin/plugin.json", #"{"name":"watch","userConfig":{"show":{"title":"Status line entry","type":"boolean"}}}"#)
        Self.put(home, "mods/watch/hooks/hooks.json", #"{"modules":["./register.tsx"]}"#)
        Self.put(home, "mods/watch/hooks/register.tsx", "export default ($) => { $.ui.status('ok') }\n")
        // A mod without one, a plugin that isn't a mod, and an @inline mod turned off.
        Self.put(home, "mods/band/hooks/hooks.json", #"{"modules":["./r.tsx"]}"#)
        Self.put(home, "mods/band/hooks/r.tsx", "export default ($) => { $.on('ui.render', () => null) }\n")
        Self.put(home, "mods/plain/.claude-plugin/plugin.json", #"{"name":"plain"}"#)
        Self.put(home, "mods/off/.claude-plugin/plugin.json", #"{"name":"off"}"#)
        Self.put(home, "mods/off/hooks/hooks.json", #"{"modules":["./r.js"]}"#)
        Self.put(home, "mods/off/hooks/r.js", "$.ui.status('x')\n")
        // An installed plugin that is a mod with a status entry, in node_modules-free source.
        Self.put(home, "cache/beep/hooks/hooks.json", #"{"modules":["./m.ts"]}"#)
        Self.put(home, "cache/beep/hooks/m.ts", "$.ui.status(`beep`)\n")
        Self.put(home, "cache/beep/node_modules/x/index.js", "")
        Self.put(home, ".claude/plugins/installed_plugins.json",
                  "{\"version\":2,\"plugins\":{\"beep@market\":[{\"installPath\":\"\(home.at("cache/beep"))\"}],\"gone@market\":[{\"installPath\":\"/nowhere\"}]}}")
        Self.put(home, ".claude/settings.json", """
        {"env": {"CLAUDE_CODE_PLUGIN_DIRS": "~/mods/watch:\(home.at("mods/band")):~/mods/plain:~/mods/off"},
         "enabledPlugins": {"off@inline": false, "beep@market": true, "gone@market": true, "quiet@market": false}}
        """)
        let found = ModStatusEntries.scan(folder: home.at(".claude"), home: home.path)
        #expect(found.map(\.name) == ["watch", "beep"])
        #expect(found.map(\.origin) == [.pluginDirs, .enabledPlugins])
        #expect(found.first?.statusSetting == "Status line entry")
        #expect(found.last?.statusSetting == nil)
        // A folder without settings, or without mods.
        #expect(ModStatusEntries.scan(folder: home.at("nothing"), home: home.path).isEmpty)
    }

    // MARK: Live input

    @Test func liveInputUsesTheSnapshotAndTheLatestSession() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        let home = r.home
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        let reset = ISO8601DateFormatter().string(from: now.addingTimeInterval(7200))
        Self.put(home, "snap.json", """
        {"schema_version":1,"tiers":[{"key":"five_hour","utilization":61,"resets_at":"\(reset)"},
         {"key":"seven_day","utilization":12,"resets_at":null},{"key":"seven_day_opus","utilization":90}]}
        """)
        home.dir("work")
        let older = home.at(".claude/projects/p/old.jsonl")
        Self.put(home, ".claude/projects/p/old.jsonl", #"{"type":"assistant","message":{"model":"claude-haiku-4-5","usage":{"input_tokens":1}}}"#)
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSince1970: 1_000_000_000)], ofItemAtPath: older)
        Self.put(home, ".claude/projects/q/new.jsonl", """
        {"type":"assistant","cwd":"\(home.at("work"))","message":{"model":"claude-opus-5-5","usage":{"input_tokens":1000,"cache_read_input_tokens":75000,"cache_creation_input_tokens":0},"content":[{"type":"text","text":"never read"}]}}
        {"type":"user","message":{"content":"hi"}}

        """)
        let result = StatuslineLiveInput.build(folder: home.at(".claude"), snapshot: URL(fileURLWithPath: home.at("snap.json")), now: now)
        #expect(result.liveLimits && result.liveSession)
        let o = try #require(try JSONSerialization.jsonObject(with: result.data) as? [String: Any])
        let limits = try #require(o["rate_limits"] as? [String: Any])
        #expect((limits["five_hour"] as? [String: Any])?["used_percentage"] as? Double == 61)
        #expect((limits["five_hour"] as? [String: Any])?["resets_at"] as? Int == 1_800_007_200)
        #expect((limits["seven_day"] as? [String: Any])?["resets_at"] == nil)
        #expect(limits["seven_day_opus"] == nil)
        #expect((o["model"] as? [String: Any])?["display_name"] as? String == "Opus 5.5")
        #expect((o["context_window"] as? [String: Any])?["used_percentage"] as? Int == 38)
        #expect((o["workspace"] as? [String: Any])?["current_dir"] as? String == home.at("work"))
        #expect(!String(decoding: result.data, as: UTF8.self).contains("never read"))

        // No snapshot and no session: the sample, said so.
        let none = StatuslineLiveInput.build(folder: home.at("nothing"), snapshot: URL(fileURLWithPath: home.at("none.json")), now: now)
        #expect(!none.liveLimits && !none.liveSession)
        #expect(none.data == StatuslinePreview.sampleJSON(now: now))
    }

    @Test func modelIdsReadAsNames() {
        #expect(StatuslineLiveInput.displayName("claude-opus-5-5") == "Opus 5.5")
        #expect(StatuslineLiveInput.displayName("claude-sonnet-4-5-20250929") == "Sonnet 4.5")
        #expect(StatuslineLiveInput.displayName("claude-haiku-4") == "Haiku 4")
        #expect(StatuslineLiveInput.displayName("claude-opus-5-5[1m]") == "Opus 5.5")
        #expect(StatuslineLiveInput.displayName("gpt-x") == "gpt-x")
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// Combine (item 63): the flag grammar, unwrapping, the carried members, the receipt's mode and
/// the byte-for-byte round trip. Temp folders only.
@Suite("Statusline combine")
struct StatuslineCombineTests {
    private static let script = "/x/Sanduhr/integrations/current/sanduhr_statusline.py"

    private static func b64(_ s: String) -> String { Data(s.utf8).base64EncodedString() }

    private static func ours(_ command: String) -> Bool { IntegrationInstaller.isOurStatusline(["command": command]) }

    @Test func theCombinedGrammarIsExact() {
        let mine = "~/bin/line.sh | tee /tmp/x; echo \"$(date)\""
        let built = IntegrationInstaller.statuslineCommand(python: "/usr/bin/python3", script: Self.script,
                                                           chain: mine, join: .same, padding: 2)
        #expect(built == "/usr/bin/python3 \(Self.script) --chain-b64 \(Self.b64(mine)) --join same --padding 2")
        let parsed = IntegrationInstaller.parseStatusline(built)
        #expect(parsed == StatuslineCommand(base: "/usr/bin/python3 \(Self.script)", chain: mine, join: .same, padding: 2))
        #expect(IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a", join: .line, padding: 0)
                == "python3 \(Self.script) --chain-b64 YQ== --join line")
        // A quoted script with a space, as Sanduhr writes it.
        let quoted = IntegrationInstaller.statuslineCommand(python: "/usr/bin/python3",
                                                            script: "/Users/a/Library/Application Support/Sanduhr/integrations/current/sanduhr_statusline.py",
                                                            chain: "my.sh", join: .line)
        #expect(IntegrationInstaller.parseStatusline(quoted)?.chain == "my.sh")
        #expect(Self.ours("python3 \(Self.script)"))

        let base = "python3 \(Self.script)"
        let good = Self.b64("my.sh")
        for bad in [
            "\(base) --join line --chain-b64 \(good)",              // order
            "\(base) --chain-b64 \(good)",                          // no join
            "\(base) --chain-b64 \(good) --join both",
            "\(base) --chain-b64 \(good) --join line --padding",
            "\(base) --chain-b64 \(good) --join line --padding 1000",
            "\(base) --chain-b64 \(good) --join line --padding -1",
            "\(base) --chain-b64 \(good) --join line --extra 1",
            "\(base) --chain-b64 \(good) --join line extra",
            "\(base) --chain-b64  \(good) --join line",              // two spaces
            "\(base) --chain-b64 bXkuc2g --join line",               // unpadded
            "\(base) --chain-b64 bX=uc2g= --join line",
            "\(base) --chain-b64 \(good)! --join line",
            "\(base) --chain-b64 \(Data([0xFF, 0xFE]).base64EncodedString()) --join line",   // not UTF-8
            "\(base) --chain-b64 \(Self.b64("   ")) --join line",
            "\(base) --chain-b64=\(good) --join line",
            "echo hi; \(base) --chain-b64 \(good) --join line",
            "\(base) --chain-b64 \(good) --join line | cat",
            "my.sh --chain-b64 \(good) --join line",                // not Sanduhr's script
            "\(base) --padding 2",                                  // padding without a chain
        ] {
            #expect(!Self.ours(bad), "\(bad)")
        }
    }

    @Test func unwrapFindsTheInnermostForeignCommand() {
        let plain = "python3 \(Self.script)"
        let once = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "my.sh")
        let twice = IntegrationInstaller.statuslineCommand(python: "/opt/py", script: "/other/seat/sanduhr_statusline.py",
                                                           chain: once, join: .same)
        let around = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: plain)
        #expect(IntegrationInstaller.innermostForeign("my.sh") == "my.sh")
        #expect(IntegrationInstaller.innermostForeign(once) == "my.sh")
        #expect(IntegrationInstaller.innermostForeign(twice) == "my.sh")
        #expect(IntegrationInstaller.innermostForeign(plain) == nil)
        #expect(IntegrationInstaller.innermostForeign(around) == nil)
    }

    @Test func siblingsAreCarriedOnlyWhenTheyAreTheRightKind() {
        let carried = IntegrationInstaller.carriedSiblings([
            "type": "command", "command": "x", "padding": 2, "refreshInterval": 5, "hideVimModeIndicator": true, "other": 1,
        ])
        #expect(carried == [JSONEdit.Pair("padding", .int(2)), JSONEdit.Pair("refreshInterval", .int(5)),
                            JSONEdit.Pair("hideVimModeIndicator", .bool(true))])
        #expect(IntegrationInstaller.carriedSiblings(["padding": true, "refreshInterval": 1.5, "hideVimModeIndicator": 1]).isEmpty)
        #expect(IntegrationInstaller.carriedSiblings("x").isEmpty)
    }

    private static let theirs = """
    {
      "model": "opus",
      "statusLine": {
        "type": "command",
        "command": "~/bin/my-line.sh --style powerline",
        "padding": 2,
        "refreshInterval": 5,
        "hideVimModeIndicator": true
      },
      "permissions": {
        "allow": ["Bash(ls:*)"]
      }
    }

    """

    @Test func combineThenRemoveIsByteIdenticalSiblingsIncluded() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.theirs)
        let folder = r.home.at(".claude")
        let before = r.snapshot()

        #expect(r.installer.status(.statusline, folder: folder) == .other)
        #expect(try r.installer.install(.statusline, folder: folder, python: r.python, combine: .same) == .installed)
        let sl = r.json(".claude/settings.json")["statusLine"] as? [String: Any]
        let command = try #require(sl?["command"] as? String)
        let parsed = try #require(IntegrationInstaller.parseStatusline(command))
        #expect(parsed.chain == "~/bin/my-line.sh --style powerline")
        #expect(parsed.join == .same)
        #expect(parsed.padding == 2)
        #expect(sl?["padding"] as? Int == 2)
        #expect(sl?["refreshInterval"] as? Int == 5)
        #expect(sl?["hideVimModeIndicator"] as? Bool == true)
        #expect(r.json(".claude/settings.json")["model"] as? String == "opus")
        #expect(r.installer.status(.statusline, folder: folder) == .installed)
        #expect(r.installer.isCombined(folder: folder))
        #expect(r.installer.loadReceipts().first?.mode == .combine)

        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
        #expect(!r.installer.isCombined(folder: folder))
    }

    @Test func replaceCarriesTheSiblingsTooAndRoundTrips() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.theirs)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.install(.statusline, folder: folder, python: r.python, replaceOther: true)
        let sl = r.json(".claude/settings.json")["statusLine"] as? [String: Any]
        #expect(IntegrationInstaller.parseStatusline(sl?["command"] as? String ?? "")?.chain == nil)
        #expect(sl?["refreshInterval"] as? Int == 5)
        #expect(!r.installer.isCombined(folder: folder))
        #expect(r.installer.loadReceipts().first?.mode == .replace)
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func anUpdateRebuildsTheCombinedCommandAndStillRoundTrips() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude-a/projects")
        r.home.file(".claude-a/settings.json", Self.theirs)
        let folder = r.home.at(".claude-a")
        let before = r.snapshot()
        try r.installer.install(.statusline, folder: folder, python: r.python, combine: .line)
        let installed = r.bytes(".claude-a/settings.json")

        r.ship(version: "2")
        #expect(r.installer.status(.statusline, folder: folder) == .outdated)
        r.scripts.refreshIfInstalled()
        #expect(r.installer.status(.statusline, folder: folder) == .installed)
        // The Update button: same bytes, same receipt.
        try r.installer.install(.statusline, folder: folder, python: r.python)
        #expect(r.bytes(".claude-a/settings.json") == installed)
        #expect(r.installer.loadReceipts().first?.mode == .combine)
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func aNestedSanduhrCommandIsUnwrappedOnUpdate() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let inner = IntegrationInstaller.statuslineCommand(python: "/usr/bin/python3", script: "/old/seat/sanduhr_statusline.py",
                                                           chain: "my.sh", join: .same)
        let outer = IntegrationInstaller.statuslineCommand(python: "/usr/bin/python3", script: Self.script, chain: inner, join: .same)
        let json = "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"\(outer)\"\n  }\n}\n"
        r.home.file(".claude/settings.json", json)
        let folder = r.home.at(".claude")
        #expect(r.installer.status(.statusline, folder: folder) == .outdated)
        try r.installer.install(.statusline, folder: folder, python: r.python)
        let command = r.json(".claude/settings.json")["statusLine"].flatMap { ($0 as? [String: Any])?["command"] as? String }
        let parsed = try #require(command.flatMap(IntegrationInstaller.parseStatusline))
        #expect(parsed.chain == "my.sh")
        #expect(parsed.join == .same)
        #expect(r.installer.status(.statusline, folder: folder) == .installed)
    }

    @Test func combiningOverNothingIsAPlainInstall() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.install(.statusline, folder: folder, python: r.python, combine: .line)
        #expect(!r.installer.isCombined(folder: folder))
        #expect(r.installer.loadReceipts().first?.mode == nil)
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func receiptsWithAndWithoutAModeDecode() throws {
        let old = #"[{"folder":"/h/.claude","file":"/h/.claude/settings.json","kind":"statusline","createdFile":false,"createdParent":false,"previous":"{}"}]"#
        let decoded = try JSONDecoder().decode([IntegrationReceipt].self, from: Data(old.utf8))
        #expect(decoded.first?.mode == nil)
        #expect(decoded.first?.previous == "{}")
        let new = #"[{"folder":"/h/.claude","file":"/h/.claude/settings.json","kind":"statusline","createdFile":false,"createdParent":false,"mode":"combine"}]"#
        #expect(try JSONDecoder().decode([IntegrationReceipt].self, from: Data(new.utf8)).first?.mode == .combine)
    }

    @Test func previewArgumentsUseTheAppsCopyOfTheScript() {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        let args = r.installer.previewArguments(chain: "my.sh", join: .same)
        #expect(args == [r.app.appendingPathComponent("sanduhr_statusline.py").path, "--chain-b64", Self.b64("my.sh"), "--join", "same"])
        let none = IntegrationInstaller(home: r.home.path, scripts: IntegrationScripts(source: nil, dir: r.support))
        #expect(none.previewArguments(chain: "my.sh", join: .line) == nil)
        let sample = try? JSONSerialization.jsonObject(with: StatuslinePreview.sampleJSON()) as? [String: Any]
        #expect((sample?["rate_limits"] as? [String: Any])?["five_hour"] != nil)
        #expect((sample?["model"] as? [String: Any])?["display_name"] as? String == "Opus")
    }
}

@Suite("ANSI text")
struct ANSITextTests {
    @Test func sgrBecomesStyledRuns() {
        let runs = ANSIText.runs("\u{1B}[1;31mred\u{1B}[0m plain \u{1B}[38;5;196mx\u{1B}[38;2;1;2;3my\u{1B}[22;39m")
        #expect(runs.map(\.text) == ["red", " plain ", "x", "y"])
        #expect(runs[0].style.bold && runs[0].style.fg == ANSIText.basic[1])
        #expect(runs[1].style == ANSIText.Style())
        #expect(runs[2].style.fg == ANSIText.RGB(255, 0, 0))
        #expect(runs[3].style.fg == ANSIText.RGB(1, 2, 3))
    }

    @Test func oscLinksKeepTheirTextAndOtherEscapesGo() {
        let runs = ANSIText.runs("\u{1B}]8;;https://x.test\u{1B}\\link\u{1B}]8;;\u{1B}\\ \u{1B}[2K\u{1B}]0;title\u{07}ok\u{07}")
        #expect(runs.map(\.text).joined() == "link ok")
    }

    @Test func brightBackgroundsAndResets() {
        var s = ANSIText.Style()
        ANSIText.apply("97;104;4", to: &s)
        #expect(s.fg == ANSIText.basic[15] && s.bg == ANSIText.basic[12] && s.underline)
        ANSIText.apply("24;49", to: &s)
        #expect(!s.underline && s.bg == nil && s.fg == ANSIText.basic[15])
        ANSIText.apply("", to: &s)
        #expect(s == ANSIText.Style())
        #expect(ANSIText.palette(232) == ANSIText.RGB(8, 8, 8))
        #expect(ANSIText.palette(16) == ANSIText.RGB(0, 0, 0))
        #expect(String(ANSIText.attributed("\u{1B}[1mab\u{1B}[0mc").characters) == "abc")
    }
}

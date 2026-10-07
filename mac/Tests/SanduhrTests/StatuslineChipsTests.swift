import Foundation
import Testing
@testable import Sanduhr

/// Picking segments when combining statuslines (item 63b): the picks' grammar inside the exact
/// command, the chip model, Update keeping the picks and Remove still byte-identical. Temp
/// folders only.
@Suite("Statusline segments")
struct StatuslineChipsTests {
    private static let script = "/x/Sanduhr/integrations/current/sanduhr_statusline.py"
    private static let base = "python3 \(script)"

    private static func b64(_ s: String) -> String { Data(s.utf8).base64EncodedString() }

    private static func ours(_ command: String) -> Bool { IntegrationInstaller.isOurStatusline(["command": command]) }

    // MARK: The grammar

    @Test func picksRoundTripThroughTheCommand() throws {
        let picks = StatuslinePicks(keep: ["~", "+"], drop: ["⎇", "日本"], keepNew: false, separators: [nil, .powerline])
        let selection = StatuslineSelection(theirs: picks, mine: [.session, .context, .model])
        let built = IntegrationInstaller.statuslineCommand(python: "/usr/bin/python3", script: Self.script, chain: "my.sh",
                                                           join: .same, padding: 2, selection: selection)
        #expect(built == "/usr/bin/python3 \(Self.script) --chain-b64 \(Self.b64("my.sh")) --join same --padding 2 --keep-theirs-b64 \(picks.base64) --mine session,context,model")
        #expect(picks.json == #"{"drop":["⎇","日本"],"keep":["~","+"],"new":false,"sep":[null,"powerline"]}"#)
        let parsed = try #require(IntegrationInstaller.parseStatusline(built))
        #expect(parsed == StatuslineCommand(base: "/usr/bin/python3 \(Self.script)", chain: "my.sh", join: .same, padding: 2,
                                            selection: selection))
        // Each flag on its own, and without padding.
        let mineOnly = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a",
                                                              selection: StatuslineSelection(mine: [.weekly]))
        #expect(mineOnly == "\(Self.base) --chain-b64 YQ== --join line --mine weekly")
        #expect(IntegrationInstaller.parseStatusline(mineOnly)?.selection == StatuslineSelection(mine: [.weekly]))
        let theirsOnly = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a",
                                                                selection: StatuslineSelection(theirs: StatuslinePicks(drop: ["x"])))
        #expect(IntegrationInstaller.parseStatusline(theirsOnly)?.selection.theirs == StatuslinePicks(drop: ["x"]))
        // Trailing detected lines are left out; picks with nothing but the defaults still read.
        #expect(StatuslinePicks(drop: ["x"], separators: [.pipe, nil, nil]).json == #"{"drop":["x"],"sep":["pipe"]}"#)
        #expect(StatuslinePicks(base64: Self.b64("{}")) == StatuslinePicks())
    }

    @Test func aCommandWithoutPicksStaysAsItWas() throws {
        let old = "\(Self.base) --chain-b64 \(Self.b64("my.sh")) --join line --padding 3"
        let parsed = try #require(IntegrationInstaller.parseStatusline(old))
        #expect(parsed.selection == StatuslineSelection())
        #expect(IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: parsed.chain,
                                                       join: parsed.join ?? .line, padding: parsed.padding,
                                                       selection: parsed.selection) == old)
    }

    @Test func picksTheRunnerWouldRefuseMakeTheCommandTheirs() {
        let chain = "\(Self.base) --chain-b64 \(Self.b64("my.sh")) --join line"
        let k = StatuslinePicks(drop: ["a"]).base64
        for bad in [
            "\(chain) --mine session --keep-theirs-b64 \(k)",                 // order
            "\(chain) --keep-theirs-b64 \(k) --padding 2",
            "\(chain) --keep-theirs-b64",
            "\(chain) --keep-theirs-b64 \(k) --keep-theirs-b64 \(k)",
            "\(chain) --keep-theirs-b64 !!!!",
            "\(chain) --keep-theirs-b64 \(Self.b64("[]"))",
            "\(chain) --keep-theirs-b64 \(Self.b64("{not json"))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"extra":1}"#))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"drop":"a"}"#))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"drop":[""]}"#))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"drop":[1]}"#))",
            "\(chain) --keep-theirs-b64 \(Self.b64("{\"drop\":[\"\(String(repeating: "x", count: 65))\"]}"))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"new":"yes"}"#))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"new":1}"#))",
            "\(chain) --keep-theirs-b64 \(Self.b64(#"{"sep":["slash"]}"#))",
            "\(chain) --mine",
            "\(chain) --mine weekly,session",                                 // out of order
            "\(chain) --mine session,session",
            "\(chain) --mine session,cost",
            "\(chain) --mine session,",
            "\(Self.base) --mine session",                                    // picks without a chain
            "\(Self.base) --keep-theirs-b64 \(k)",
        ] {
            #expect(!Self.ours(bad), "\(bad)")
        }
    }

    /// The runner itself accepts what Swift builds and filters as the chips said (skipped
    /// without a python3).
    @Test func theRunnerAcceptsWhatSwiftBuilds() throws {
        let python = "/usr/bin/python3"
        guard FileManager.default.isExecutableFile(atPath: python) else { return }
        let script = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations/sanduhr_statusline.py").path
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-chips-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let selection = StatuslineSelection(theirs: StatuslinePicks(keep: ["~"], drop: ["main"]), mine: [.model])
        let p = Process()
        p.executableURL = URL(fileURLWithPath: python)
        p.arguments = [script, "--compose-b64", Self.b64("~/proj | main\n"), "--join", "line"]
            + IntegrationInstaller.pickFlags(selection)
        // A snapshot that isn't there, so only the sample JSON speaks.
        p.environment = ["SANDUHR_SNAPSHOT": dir.appendingPathComponent("none.json").path, "PATH": "/usr/bin:/bin"]
        let input = Pipe(), output = Pipe()
        p.standardInput = input
        p.standardOutput = output
        try p.run()
        input.fileHandleForWriting.write(StatuslinePreview.sampleJSON())
        try input.fileHandleForWriting.close()
        let data = output.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        #expect(String(decoding: data, as: UTF8.self) == "~/proj\nOpus\n")
    }

    // MARK: The chips

    private static let inspection = StatuslineInspection.decode(#"""
    {"theirs": "~/proj | ⎇ main | ⎇ x\nOpus  ctx 8%\n",
     "lines": [
       {"text": "~/proj | ⎇ main | ⎇ x", "auto": "pipe", "splits": {
          "pipe": {"doubt": false, "segments": [{"matcher": "~", "text": "~/proj"}, {"matcher": "⎇", "text": "⎇ main"}, {"matcher": "⎇", "text": "⎇ x"}]},
          "spaces": {"doubt": false, "segments": [{"matcher": "~", "text": "~/proj | ⎇ main | ⎇ x"}]},
          "dot": {"doubt": true, "segments": []},
          "none": {"doubt": false, "segments": [{"matcher": "~", "text": "~/proj | ⎇ main | ⎇ x"}]}}},
       {"text": "\u001b[1mOpus\u001b[0m  ctx 8%", "auto": "spaces", "splits": {
          "spaces": {"doubt": false, "segments": [{"matcher": "Opus", "text": "Opus"}, {"matcher": "ctx", "text": "ctx 8%"}]},
          "pipe": {"doubt": false, "segments": [{"matcher": "Opus", "text": "Opus  ctx 8%"}]}}},
       {"text": "", "auto": "none", "splits": {}}],
     "mine": [{"name": "session", "text": "5h 42%"}, {"name": "weekly", "text": "wk 18%"}, {"name": "resets", "text": "wk resets Thu 3p"},
              {"name": "context", "text": "ctx 8%"}, {"name": "model", "text": "Opus"}],
     "notice": ""}
    """#)!

    @Test func chipsFollowEachLinesSeparator() {
        var c = StatuslineChips(Self.inspection)
        #expect(c.lineIndices == [0, 1])
        #expect(c.chips(line: 0).map(\.label) == ["~/proj", "⎇ main", "⎇ x"])
        #expect(c.chips(line: 0).allSatisfy { $0.kept && $0.enabled && $0.source == .theirs })
        #expect(c.chips(line: 1).map(\.key) == ["Opus", "ctx"])
        #expect(c.separator(line: 1) == .spaces)
        // Untouched: the command carries no picks.
        #expect(c.selection == StatuslineSelection())

        c.setSeparator(.dot, line: 0)
        #expect(c.isWhole(line: 0))
        #expect(c.chips(line: 0).count == 1 && !c.chips(line: 0)[0].enabled)
        #expect(c.seenMatchers == ["Opus", "ctx"])
        c.setSeparator(.pipe, line: 0)
        #expect(c.seenMatchers == ["~", "⎇", "Opus", "ctx"])
    }

    @Test func clickingDropsAndChipsSharingAMatcherGoTogether() {
        var c = StatuslineChips(Self.inspection)
        c.toggle(c.chips(line: 0)[1])
        #expect(c.chips(line: 0).map(\.kept) == [true, false, false])
        #expect(c.theirs == StatuslinePicks(keep: ["~", "Opus", "ctx"], drop: ["⎇"], separators: [nil, nil, nil]))
        c.toggle(c.chips(line: 1)[1])
        #expect(c.theirs?.drop == ["⎇", "ctx"])
        c.toggle(c.chips(line: 0)[2])
        #expect(c.theirs?.drop == ["ctx"])
        // Cut another way, a dropped matcher no longer seen drops nothing: no picks at all.
        c.setSeparator(.pipe, line: 1)
        #expect(c.theirs == nil)
        // A chosen separator travels with the picks; a disabled chip can't be clicked.
        c.toggle(c.chips(line: 0)[1])
        #expect(c.theirs?.separators == [nil, .pipe, nil])
        #expect(c.theirs?.json == #"{"drop":["⎇"],"keep":["~","Opus"],"sep":[null,"pipe"]}"#)
        let before = c
        c.toggle(StatuslineChips.Chip(id: "x", source: .theirs, label: "x", key: "~", kept: true, enabled: false))
        #expect(c == before)
    }

    @Test func keepNewOffKeepsOnlyWhatIsListed() {
        var c = StatuslineChips(Self.inspection)
        c.keepNew = false
        #expect(c.theirs == StatuslinePicks(keep: ["~", "⎇", "Opus", "ctx"], drop: [], keepNew: false, separators: [nil, nil, nil]))
    }

    @Test func sanduhrsChipsAreSwitchesAndOneAlwaysStays() {
        var c = StatuslineChips(Self.inspection)
        #expect(c.mineChips.map(\.label) == ["5h 42%", "wk 18%", "wk resets Thu 3p", "ctx 8%", "Opus"])
        #expect(c.mineChips.map(\.kept) == [true, true, true, false, false])
        #expect(c.mineChips.allSatisfy { $0.source == .sanduhr })
        c.toggle(c.mineChips[4])
        c.toggle(c.mineChips[1])
        c.toggle(c.mineChips[2])
        #expect(c.selection.mine == [.session, .model])
        c.toggle(c.mineChips[0])
        #expect(c.selection.mine == [.model])
        #expect(!c.mineChips[4].enabled)
        c.toggle(c.mineChips[4])
        #expect(c.selection.mine == [.model])
        // Back to the defaults: no --mine.
        for i in [0, 1, 2] { c.toggle(c.mineChips[i]) }
        c.toggle(c.mineChips[4])
        #expect(c.selection.mine == nil)
    }

    // MARK: Install, Update, Remove

    private static let theirs = """
    {
      "statusLine": {
        "type": "command",
        "command": "~/bin/powerline.sh",
        "padding": 1
      },
      "theme": "dark"
    }

    """

    @Test func picksSurviveUpdateAndRemoveIsStillByteIdentical() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.theirs)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        let selection = StatuslineSelection(theirs: StatuslinePicks(keep: ["~"], drop: ["⎇"]), mine: [.weekly, .context])
        #expect(try r.installer.install(.statusline, folder: folder, python: r.python, combine: .line,
                                        selection: selection) == .installed)
        let command = try #require((r.json(".claude/settings.json")["statusLine"] as? [String: Any])?["command"] as? String)
        let parsed = try #require(IntegrationInstaller.parseStatusline(command))
        #expect(parsed.chain == "~/bin/powerline.sh")
        #expect(parsed.padding == 1)
        #expect(parsed.selection == selection)
        let installed = r.bytes(".claude/settings.json")

        r.ship(version: "2")
        r.scripts.refreshIfInstalled()
        try r.installer.install(.statusline, folder: folder, python: r.python)
        #expect(r.bytes(".claude/settings.json") == installed)

        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }
}

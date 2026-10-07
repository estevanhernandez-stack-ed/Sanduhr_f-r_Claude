import Foundation
import Testing
@testable import Sanduhr

/// Item 63b's third pass: segments named by kind, duplicates and their resolutions, styles and
/// their round trip, the popover's edits, drawn powerline glyphs and menu labels. Temp folders only.
@Suite("Statusline kinds, duplicates and styles")
struct StatuslineLookTests {
    private static func b64(_ s: String) -> String { Data(s.utf8).base64EncodedString() }

    /// `⎇ main | $1.42 | Opus | ctx 38%`, as the runner's inspect mode reports it.
    private static let inspection = StatuslineInspection.decode(#"""
    {"theirs": "⎇ main | $1.42 | Opus | ctx 38%\n",
     "lines": [{"text": "⎇ main | $1.42 | Opus | ctx 38%", "auto": "pipe", "splits": {
        "pipe": {"doubt": false, "segments": [
          {"matcher": "⎇", "text": "⎇ main", "kind": "branch"}, {"matcher": "$", "text": "$1.42", "kind": "cost"},
          {"matcher": "Opus", "text": "Opus", "kind": "model"}, {"matcher": "ctx", "text": "ctx 38%", "kind": "context"}]},
        "none": {"doubt": false, "segments": [{"matcher": "⎇", "text": "⎇ main | $1.42 | Opus | ctx 38%"}]}}}],
     "mine": [{"name": "session", "text": "5h 23%"}, {"name": "weekly", "text": "wk 41%"}, {"name": "resets", "text": "wk resets Thu 3p"},
              {"name": "context", "text": "ctx 8%"}, {"name": "model", "text": "Opus"}],
     "notice": ""}
    """#)!

    private static func withContextAndModel() -> StatuslineChips {
        var c = StatuslineChips(inspection)
        c.toggle(c.mineChips[3])
        c.toggle(c.mineChips[4])
        return c
    }

    // MARK: Kinds

    @Test func theirChipsAreNamedByKind() {
        let c = StatuslineChips(Self.inspection)
        #expect(c.chips(line: 0).map(\.name) == ["Branch", "Cost", "Model", "Context"])
        #expect(c.chips(line: 0).map(\.kind) == [.branch, .cost, .model, .context])
        #expect(c.mineChips.map(\.name) == ["Session", "Weekly", "Weekly reset", "Context", "Model"])
        // An older script without kinds, and a kind it doesn't know: "Your segment".
        var other = StatuslineChips(Self.inspection)
        other.setSeparator(.none, line: 0)
        #expect(other.chips(line: 0).first?.name == "Your segment")
        #expect(SegmentKind.model.sanduhr == .model && SegmentKind.branch.sanduhr == nil)
    }

    // MARK: Duplicates

    @Test func duplicatesPairByKindAndResolveEitherWay() {
        var c = Self.withContextAndModel()
        let found = StatuslineDuplicate.find(c, mods: [])
        #expect(found.map(\.what) == ["Model", "Context"])
        let allResolvable = found.allSatisfy(\.resolvable)
        #expect(allResolvable)
        #expect(StatuslineDuplicate.summary(found) == "2 duplicates: Model, Context")
        #expect(StatuslineDuplicate.summary([]) == nil)
        // With Sanduhr's defaults (no context, no model), nothing doubles.
        #expect(StatuslineDuplicate.find(StatuslineChips(Self.inspection), mods: []).isEmpty)

        c.resolve(found[0], keepYours: true)                 // Model: Sanduhr's goes
        #expect(c.selection.mine == [.session, .weekly, .resets, .context])
        c.resolve(found[1], keepYours: false)                // Context: yours goes
        #expect(c.theirs?.drop == ["ctx"])
        #expect(StatuslineDuplicate.find(c, mods: []).isEmpty)
    }

    @Test func modsDoubleByTheirWords() {
        let c = Self.withContextAndModel()
        let usage = ModStatusEntry(name: "quota-watch", path: "/m/q", origin: .pluginDirs, statusSetting: nil,
                                   description: "Shows your rate limits")
        let meters = ModStatusEntry(name: "sanduhr-meters", path: "/m/s", origin: .pluginDirs, statusSetting: nil,
                                    description: "", drawsStatus: false)
        let git = ModStatusEntry(name: "git-status", path: "/m/g", origin: .enabledPlugins, statusSetting: nil,
                                 description: "The branch you're on")
        let quiet = ModStatusEntry(name: "weather", path: "/m/w", origin: .pluginDirs, statusSetting: nil)
        let found = StatuslineDuplicate.find(c, mods: [usage, meters, git, quiet])
        #expect(found.contains(StatuslineDuplicate(what: "Branch", a: .theirs("⎇"), b: .mod("/m/g"))))
        #expect(found.contains(StatuslineDuplicate(what: "Session", a: .mod("/m/q"), b: .sanduhr(.session))))
        #expect(found.contains(StatuslineDuplicate(what: "Weekly", a: .mod("/m/s"), b: .sanduhr(.weekly))))
        #expect(!found.contains { $0.a == .mod("/m/w") || $0.b == .mod("/m/w") })
        #expect(found.filter { !$0.resolvable }.count == found.count - 2)
        let badges = Badges(found, mods: [usage, meters, git, quiet])
        #expect(badges.text(.mod("/m/g")) == "Also shown by yours: Branch")
        #expect(badges.text(.theirs("ctx")) == "Also shown by Sanduhr: Context")
        #expect(badges.text(.sanduhr(.session))?.contains("Also shown by the mod quota-watch: Session") == true)
    }

    @Test func theMetersModIsListedEvenWithoutAStatusEntry() {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        let home = r.home
        for (rel, text) in [("m/meters/.claude-plugin/plugin.json", #"{"name":"sanduhr-meters","description":"Meters above the prompt"}"#),
                            ("m/meters/hooks/hooks.json", #"{"modules":["./r.tsx"]}"#),
                            ("m/meters/hooks/r.tsx", "$.on('ui.render', x)\n"),
                            (".claude/settings.json", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"\(home.at("m/meters"))\"}}")] {
            home.dir((rel as NSString).deletingLastPathComponent)
            home.file(rel, text)
        }
        let found = ModStatusEntries.scan(folder: home.at(".claude"), home: home.path)
        #expect(found.map(\.name) == ["sanduhr-meters"])
        #expect(found.first?.drawsStatus == false)
        #expect(found.first?.description == "Meters above the prompt")
        #expect(found.first?.showsUsage == true)
    }

    // MARK: Styles

    @Test func stylesRoundTripThroughTheCommand() throws {
        let style = SegmentStyle(ink: ["#ff2a6d", "#05d9e8"], font: .script, bold: true)
        let picks = StatuslinePicks(keep: ["⎇"], styles: ["⎇": style], ours: [.session: SegmentStyle(dim: true)])
        #expect(picks.json == ##"{"keep":["⎇"],"ours":{"session":{"dim":true}},"style":{"⎇":{"bold":true,"font":"script","ink":["#ff2a6d","#05d9e8"]}}}"##)
        let command = IntegrationInstaller.statuslineCommand(python: "python3", script: "/x/sanduhr_statusline.py", chain: "my.sh",
                                                             selection: StatuslineSelection(theirs: picks))
        #expect(IntegrationInstaller.parseStatusline(command)?.selection.theirs == picks)
        for bad in [##"{"style":{"a":{"ink":[]}}}"##, ##"{"style":{"a":{"ink":["#f00","#f00","#f00","#f00","#f00"]}}}"##,
                    ##"{"style":{"a":{"ink":["red"]}}}"##, ##"{"style":{"a":{"font":"comic"}}}"##, ##"{"style":{"a":{"bold":1}}}"##,
                    ##"{"style":{"a":{"shimmer":true}}}"##, ##"{"style":{"":{"bold":true}}}"##, ##"{"style":[]}"##,
                    ##"{"ours":{"cost":{"bold":true}}}"##, ##"{"ours":{"session":"bold"}}"##] {
            #expect(StatuslinePicks(base64: Self.b64(bad)) == nil, "\(bad)")
        }
        #expect(StatuslinePicks(base64: Self.b64(##"{"style":{"a":{"ink":["fff"]}}}"##))?.styles["a"]?.ink == ["fff"])
    }

    @Test func thePopoversEditsStayWithinTheRules() {
        var s = SegmentStyle()
        #expect(s.keepsOwnColors && s.isEmpty)
        s.setSolid("#ff2a6d")
        #expect(s.ink == ["#ff2a6d"] && !s.isGradient)
        s.makeGradient()
        #expect(s.ink == ["#ff2a6d", "#05d9e8"])
        s.addStop()
        s.addStop()
        s.addStop()
        #expect(s.ink.count == 4)
        s.removeStop(at: 0)
        s.removeStop(at: 0)
        s.removeStop(at: 0)
        #expect(s.ink.count == 2)
        s.setStop(0, hex: "00ff00")
        s.setStop(1, hex: "nope")
        #expect(s.ink == ["#00ff00", "#05d9e8"])
        s.makeSolid()
        #expect(s.ink == ["#00ff00"])
        s.keepOwnColors()
        #expect(s.isEmpty)
        #expect(SegmentStyle.hex(red: 1, green: 0.5, blue: 0) == "#ff8000")
        #expect(SegmentStyle.components("#f80").map { [$0.0, $0.1, $0.2] } == [1, 136.0 / 255, 0])
    }

    @Test func stylingAChipCarriesItIntoThePicks() {
        var c = StatuslineChips(Self.inspection)
        let branch = c.chips(line: 0)[0]
        c.setStyle(SegmentStyle(font: .fraktur), for: branch)
        c.setStyle(SegmentStyle(underline: true), for: c.mineChips[0])
        #expect(c.chips(line: 0)[0].styled && c.mineChips[0].styled)
        #expect(c.theirs?.styles == ["⎇": SegmentStyle(font: .fraktur)])
        #expect(c.theirs?.ours == [.session: SegmentStyle(underline: true)])
        #expect(c.theirs?.drop == [])
        c.setStyle(SegmentStyle(), for: branch)
        c.setStyle(SegmentStyle(), for: c.mineChips[0])
        #expect(c.selection == StatuslineSelection())
    }

    // MARK: Powerline glyphs

    @Test func menusNeverCarryPrivateUseGlyphs() {
        let titles = StatuslineSeparator.allCases.map(\.title) + StatuslineJoinGlyph.allCases.map(\.title)
        #expect(!titles.contains { $0.unicodeScalars.contains { (0xE000...0xF8FF).contains($0.value) } })
        #expect(StatuslineSeparator.powerline.title == "Powerline arrow (needs a Nerd Font)")
        #expect(StatuslineJoinGlyph.powerlineThin.title == "Powerline thin arrow (needs a Nerd Font)")
        #expect(StatuslineSeparator.powerline.shape == .arrow && StatuslineJoinGlyph.powerlineThin.shape == .thin)
        #expect(StatuslineSeparator.pipe.shape == nil)
        #expect(PowerlineGlyph.menuImage(.arrow) != nil && PowerlineGlyph.menuImage(nil) == nil)
    }

    @Test func previewPiecesDrawPowerlineGlyphsInTheirRunsColors() {
        let pieces = ANSIText.pieces("\u{1B}[44m a \u{1B}[34;42m\u{E0B0}\u{1B}[0m \u{E0A0} b\u{E0B1}")
        #expect(pieces.count == 4)
        if case .glyph(let shape, let style) = pieces[1] {
            #expect(shape == .arrow)
            #expect(style.fg == ANSIText.basic[4] && style.bg == ANSIText.basic[2])
        } else {
            Issue.record("the arrow isn't a glyph piece")
        }
        #expect(pieces[2] == .text(" \u{2387} b", ANSIText.Style()))
        #expect(pieces[3] == .glyph(.thin, ANSIText.Style()))
    }
}

import Foundation
import Testing
@testable import Sanduhr

/// The Codex chip in Combine (2.48, item 3). `codex` and `nocodex` are `--mine` names; a list with
/// neither keeps the 2.46 rule (cx rides with session and weekly), so every command written before
/// the chip prints what it printed. Temp strings only.
@Suite("Codex chip in Combine")
struct StatuslineCodexChipTests {
    private static let script = "/x/Sanduhr/integrations/current/sanduhr_statusline.py"
    private static let base = "python3 \(script)"
    private static let chain = "\(base) --chain-b64 YQ== --join line"

    private static let inspection = StatuslineInspection.decode(#"""
    {"theirs": "~/proj\n",
     "lines": [{"text": "~/proj", "auto": "pipe", "splits": {
        "pipe": {"doubt": false, "segments": [{"matcher": "~", "text": "~/proj"}]}}}],
     "mine": [{"name": "session", "text": "5h 42%"}, {"name": "weekly", "text": "wk 18%"},
              {"name": "resets", "text": "wk resets Thu 3p"}, {"name": "codex", "text": "cx 5h 40% wk 12%"},
              {"name": "context", "text": "ctx 8%"}, {"name": "model", "text": "Opus"}],
     "notice": ""}
    """#)!

    private static func chip(_ c: StatuslineChips, _ s: SanduhrSegment) -> StatuslineChips.Chip {
        c.mineChips.first { $0.key == s.rawValue }!
    }

    // MARK: Older commands

    /// Commands written before the chip, and whether the script shows cx for each (the Python tests
    /// pin the same table against the script's output).
    private static let oldLists: [(String?, Bool)] = [
        (nil, true),
        ("session,weekly,resets", true),
        ("label,session,weekly,resets", true),
        ("session", true),
        ("weekly,model", true),
        ("weekly,context,model", true),
        ("session,context", true),
        ("resets", false),
        ("resets,context,model", false),
        ("context", false),
        ("model", false),
        ("context,model", false),
        ("label,context", false),
    ]

    @Test func everyOlderCommandRewritesToTheSameBytesAndKeepsItsCodexBehavior() throws {
        for (mine, shows) in Self.oldLists {
            let old = Self.chain + (mine.map { " --mine \($0)" } ?? "")
            let parsed = try #require(IntegrationInstaller.parseStatusline(old), "\(mine ?? "none")")
            // Parse and rebuild: not a byte moves.
            #expect(IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: parsed.chain,
                                                           join: parsed.join ?? .line, padding: parsed.padding,
                                                           selection: parsed.selection) == old, "\(mine ?? "none")")
            #expect(parsed.selection.shown.contains(.codex) == shows, "\(mine ?? "none")")
            // Loaded onto the chips and written back untouched: still the same command.
            var chips = StatuslineChips(Self.inspection)
            chips.load(parsed.selection)
            #expect(chips.mineChips.first { $0.key == "codex" }?.kept == shows, "\(mine ?? "none")")
            // The sheet may write the explicit default list as no flag at all (it always did); what shows is the same.
            #expect(chips.selection.shown == parsed.selection.shown, "\(mine ?? "none")")
            if mine != "label,session,weekly,resets" { #expect(chips.selection == parsed.selection, "\(mine ?? "none")") }
        }
    }

    @Test func aFreshSheetCarriesNothingNewAndShowsCodexOn() {
        let c = StatuslineChips(Self.inspection)
        #expect(c.selection == StatuslineSelection())
        #expect(Self.chip(c, .codex).kept)
        #expect(c.mineChips.map(\.key) == ["label", "session", "weekly", "resets", "codex", "context", "model"])
        #expect(IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a",
                                                       selection: c.selection) == Self.chain)
    }

    // MARK: The chip, on and off

    @Test func codexTurnsOffAloneAndTheCommandSaysSo() throws {
        var c = StatuslineChips(Self.inspection)
        c.toggle(Self.chip(c, .codex))
        #expect(!Self.chip(c, .codex).kept)
        let selection = c.selection
        #expect(selection.codexOff && selection.mine == [.label, .session, .weekly, .resets])
        let command = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a",
                                                             selection: selection)
        #expect(command == Self.chain + " --mine label,session,weekly,resets,nocodex")
        #expect(IntegrationInstaller.parseStatusline(command)?.selection == selection)
        var loaded = StatuslineChips(Self.inspection)
        loaded.load(selection)
        #expect(!Self.chip(loaded, .codex).kept && Self.chip(loaded, .session).kept)
        #expect(loaded.selection == selection)
        // And on again: back to no --mine at all.
        loaded.toggle(Self.chip(loaded, .codex))
        #expect(loaded.selection == StatuslineSelection())
    }

    @Test func codexTurnsOnWithoutAnyMeter() throws {
        var d = StatuslineChips(Self.inspection)
        d.load(StatuslineSelection(mine: [.context, .model]))
        #expect(!Self.chip(d, .codex).kept)
        d.toggle(Self.chip(d, .codex))
        #expect(Self.chip(d, .codex).kept)
        let selection = d.selection
        #expect(selection.mine == [.codex, .context, .model] && !selection.codexOff)
        let command = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a",
                                                             selection: selection)
        #expect(command == Self.chain + " --mine codex,context,model")
        #expect(IntegrationInstaller.parseStatusline(command)?.selection == selection)
        // A meter back in: the legacy form again, since it already brings cx.
        d.toggle(Self.chip(d, .session))
        #expect(d.selection.mine == [.session, .context, .model] && !d.selection.codexOff)
    }

    @Test func theGrammarTakesTheNewNamesAndRefusesBadOrder() {
        func parsed(_ list: String) -> StatuslineSelection? {
            IntegrationInstaller.parseStatusline("\(Self.chain) --mine \(list)")?.selection
        }
        #expect(parsed("session,codex,context") == StatuslineSelection(mine: [.session, .codex, .context]))
        #expect(parsed("session,nocodex")?.codexOff == true)
        #expect(parsed("session,nocodex")?.mine == [.session])
        #expect(parsed("codex")?.mine == [.codex])
        for bad in ["codex,nocodex", "nocodex,codex", "codex,resets", "context,codex", "nocodex,weekly", "nocodex,nocodex",
                    "nocodex", "codex,codex", "noCodex"] {
            #expect(parsed(bad) == nil, "\(bad)")
        }
        // Plain mode (Sanduhr's line alone) reads them too.
        #expect(IntegrationInstaller.parseStatusline("\(Self.base) --mine session,nocodex")?.selection.codexOff == true)
    }

    @Test func theLastMeterStaysAndCodexAloneIsNeverOffered() {
        var c = StatuslineChips(Self.inspection)
        // Down to one meter: it can't go, whatever Codex is doing.
        for s in [SanduhrSegment.weekly, .resets, .label] { c.toggle(Self.chip(c, s)) }
        #expect(Self.chip(c, .codex).kept && !Self.chip(c, .session).enabled)
        // Codex stays switchable: a meter remains.
        #expect(Self.chip(c, .codex).enabled)
        c.toggle(Self.chip(c, .codex))
        #expect(c.selection.mine == [.session] && c.selection.codexOff)
        // A hand-written `--mine codex` reads: its chip can't go, the others can be added.
        var only = StatuslineChips(Self.inspection)
        only.load(StatuslineSelection(mine: [.codex]))
        #expect(Self.chip(only, .codex).kept && !Self.chip(only, .codex).enabled)
        #expect(Self.chip(only, .model).enabled)
    }

    @Test func theSummaryNamesCodexWhenItShows() {
        #expect(StatuslineSelection().shown == [.label, .session, .weekly, .resets, .codex])
        #expect(StatuslineSelection(mine: [.weekly, .model]).shown == [.weekly, .codex, .model])
        #expect(StatuslineSelection(mine: [.session], codexOff: true).shown == [.session])
        #expect(StatuslineSelection(mine: [.context]).shown == [.context])
        #expect(SanduhrSegment.codex.title == "Codex")
    }

    // MARK: Style, caption, preview

    @Test func theChipTakesALookLikeSanduhrsOthers() throws {
        var c = StatuslineChips(Self.inspection)
        let codex = Self.chip(c, .codex)
        c.setStyle(SegmentStyle(bold: true), for: codex)
        #expect(Self.chip(c, .codex).styled && c.style(for: codex) == SegmentStyle(bold: true))
        let picks = try #require(c.selection.theirs)
        #expect(picks.ours == [.codex: SegmentStyle(bold: true)])
        #expect(picks.json.contains(#""ours":{"codex":"#))
        let command = IntegrationInstaller.statuslineCommand(python: "python3", script: Self.script, chain: "a",
                                                             selection: c.selection)
        #expect(IntegrationInstaller.parseStatusline(command)?.selection.theirs?.ours == [.codex: SegmentStyle(bold: true)])
        c.setStyle(SegmentStyle(), for: codex)
        #expect(!Self.chip(c, .codex).styled && c.selection == StatuslineSelection())
    }

    @Test func theChipPreviewsTheScriptsTextAndFallsBackToItsName() {
        let c = StatuslineChips(Self.inspection)
        #expect(Self.chip(c, .codex).label == "cx 5h 40% wk 12%")
        // No Codex snapshot: the script returns nothing for it, and the chip reads by name.
        let quiet = StatuslineInspection.decode(#"{"theirs": "", "lines": [], "mine": [{"name": "codex", "text": ""}], "notice": ""}"#)!
        #expect(Self.chip(StatuslineChips(quiet), .codex).label == "Codex")
    }

    @Test func theCaptionSaysWhatItShowsAndWhenInTheHouseVoice() {
        let live = SanduhrSegment.codex.caption(now: "cx 5h 40% wk 12%")
        #expect(live == "Codex: its 5-hour and weekly limits, as in cx 5h 40% wk 12%. It shows only while Codex is on and its numbers are under 30 minutes old.")
        // Without numbers to quote, it quotes a sample.
        #expect(SanduhrSegment.codex.caption(now: "Codex").contains("as in cx 5h 40% wk 12%"))
        #expect(SanduhrSegment.session.caption(now: "5h 42%") == "Session: the 5-hour limit, as in 5h 42%.")
    }
}

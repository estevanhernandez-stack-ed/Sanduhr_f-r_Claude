import Foundation
import Testing
@testable import Sanduhr

@Suite("The AI chat prompt")
struct SetupPromptTests {
    private let prompt = SetupPrompt.build()

    @Test func holdsNoPathFileNameOrUsageNumber() {
        #expect(!prompt.contains("/"))
        #expect(!prompt.contains("\\"))
        #expect(!prompt.contains("~"))
        #expect(!prompt.contains("%"))
        #expect(!prompt.contains("@"))
        #expect(prompt.range(of: #"\d\s*%"#, options: .regularExpression) == nil)
        #expect(prompt.range(of: #"\b[\w-]+\.(png|jpe?g|pdf|txt|docx?|xlsx?|zip|dmg|pkg|md|csv|key|mov|app)\b"#,
                             options: [.regularExpression, .caseInsensitive]) == nil)
        #expect(!prompt.contains("\u{2014}"))
    }

    @Test func namesNoProduct() {
        for bad in ["Claude", "ChatGPT", "Gemini", "Copilot", "OpenAI", "Anthropic", "Windows"] {
            #expect(!prompt.contains(bad), "\(bad)")
        }
    }

    @Test func listsEveryPieceAnchorKindAndLook() {
        for piece in DeskArrangement.known {
            #expect(prompt.contains("  \(piece): "), "\(piece)")
            #expect(!SetupPrompt.note(for: piece).isEmpty, "\(piece) has no note")
        }
        for a in DeskAnchor.allCases { #expect(prompt.contains(a.rawValue)) }
        for k in PastedSetup.kindWords { #expect(prompt.contains(k), "\(k)") }
        for l in DrawerLook.allCases { #expect(prompt.contains(l.rawValue)) }
        for c in PastedSetup.cornerWords { #expect(prompt.contains(c)) }
        #expect(prompt.contains("exactly one JSON block"))
        #expect(prompt.contains("\"desk\""))
        #expect(prompt.contains("\"drawers\""))
        #expect(prompt.contains("never file moves"))
    }

    @Test func teachesNothingTheDeskValidatorRefuses() {
        #expect(!prompt.contains("launcher"))
        #expect(!prompt.contains("monitor"))
        #expect(!prompt.contains("messages"))
        #expect(!prompt.contains("glow"))
        for f in SetupPrompt.deskFields { #expect(DeskDesign.fields.contains(f)) }
    }

    @Test func isTheSameEveryTime() {
        #expect(SetupPrompt.build() == SetupPrompt.build())
    }

    @Test func chatCopyHasNoEmDashOrProduct() {
        for s in SetupCopy.chatAll {
            #expect(!s.contains("\u{2014}"), "\(s)")
            #expect(!s.contains("Claude"), "\(s)")
        }
    }
}

@Suite("The pasted-answer reader")
struct PastedSetupTests {
    private func rule(_ kind: String, _ drawer: String, _ extra: String = "") -> String {
        "{\"kind\":\"\(kind)\",\"drawer\":\"\(drawer)\"\(extra)}"
    }
    private func fields(_ r: PastedSetupRead) -> [String] { r.reasons.map(\.field) }

    // MARK: scan

    @Test func aFencedBlockInsideProseIsRead() {
        let r = PastedSetup.read("Here you go!\n```json\n{\"desk\":{\"name\":\"Calm\"}}\n```\nEnjoy.")
        #expect(r.ok)
        #expect(r.blocks == 1)
        #expect(r.desk?.object?["name"]?.string == "Calm")
        #expect(r.rules == nil)
    }

    @Test func twoBlocksUseTheFirstGoodOneAndCountBoth() {
        let r = PastedSetup.read("{not json} and {\"desk\":{\"name\":\"A\"}} then {\"desk\":{\"name\":\"B\"}}")
        #expect(r.ok)
        #expect(r.desk?.object?["name"]?.string == "A")
        #expect(r.blocks == 2)
    }

    @Test func aBlockWithNeitherPartIsSkippedAndNotCounted() {
        let r = PastedSetup.read("Example: {\"hello\":1}\nAnswer: {\"desk\":{\"name\":\"B\"}}")
        #expect(r.ok)
        #expect(r.blocks == 1)
    }

    @Test func bracesInStringsAndStrayBracesInProseDoNotBreakTheScan() {
        let r = PastedSetup.read("Use { carefully. {\"desk\":{\"note\":\"a } and a { and a \\\" quote\"}} Done }")
        #expect(r.ok)
        #expect(r.desk?.object?["note"]?.string == "a } and a { and a \" quote")
    }

    @Test func trailingCommasAndCommentsAreForgiven() {
        let r = PastedSetup.read("{\"desk\":{\"name\":\"A\", // the name\n \"plate\":true,},}")
        #expect(r.ok)
        let s = PastedSetup.read("{\"desk\":{\"note\":\"a, } // not a comment\"}}")
        #expect(s.ok)
        #expect(s.desk?.object?["note"]?.string == "a, } // not a comment")
    }

    @Test func aBadBlockLetsTheNextGoodBlockThrough() {
        let r = PastedSetup.read("{\"drawers\":{\"look\":\"neon\"}}\nand then\n{\"drawers\":{\"look\":\"outline\"}}")
        #expect(r.ok)
        #expect(r.drawerLook?.look == .outline)
        #expect(r.blocks == 2)
    }

    @Test func nothingToReadSaysSoByField() {
        let r = PastedSetup.read("I could not help with that.")
        #expect(!r.ok)
        #expect(fields(r) == ["design"])
        #expect(r.blocks == 0)
        #expect(!PastedSetup.read("{\"hello\":1}").ok)
        #expect(!PastedSetup.read(nil).ok)
        #expect(!PastedSetup.read("").ok)
    }

    @Test func aNearlyRightBlockSaysItDidNotParse() {
        let r = PastedSetup.read("{\"desk\":{\"name\":\"A\"")
        #expect(!r.ok)
        let r2 = PastedSetup.read("{\"desk\": {\"name\": \"A\" \"plate\": true}}")
        #expect(r2.reasons.first?.message.contains("did not parse") == true)
    }

    @Test func aRepeatedKeyIsNotParsed() {
        #expect(!PastedSetup.read("{\"desk\":{\"name\":\"A\",\"name\":\"B\"}}").ok)
    }

    @Test func sizeLimitIsAtAndOverItsEdge() {
        let head = "{\"desk\":{\"note\":\""
        let tail = "\"}}"
        let at = head + String(repeating: "x", count: PastedSetup.maxPasteChars - head.count - tail.count) + tail
        #expect(at.utf16.count == PastedSetup.maxPasteChars)
        #expect(PastedSetup.read(at).ok)
        let over = at + " "
        let r = PastedSetup.read(over)
        #expect(!r.ok)
        #expect(fields(r) == ["design"])
        #expect(r.blocks == 0)
    }

    @Test func aPasteOfOnlyBracesIsRefusedQuickly() {
        #expect(!PastedSetup.read(String(repeating: "{", count: 150_000)).ok)
    }

    // MARK: rules

    @Test func rulesAreReadWithTheMacsKinds() {
        let r = PastedSetup.read("""
            {"drawers":{"rules":[
              {"kind":"images","drawer":"Photos"},
              {"kind":"name_contains","text":"invoice","drawer":"Receipts"},
              {"kind":"older_than","days":60,"drawer":"Old stuff"},
              {"kind":"shortcuts","drawer":"Links"},
              {"kind":"links","drawer":"More links"}]}}
            """)
        #expect(r.ok)
        let rules = r.rules!
        #expect(rules.count == 5)
        #expect(rules[0] == DrawerRule(kind: .images, drawer: "photos", on: true, text: nil, days: 30, drawerName: "Photos"))
        #expect(rules[1].kind == .nameContains && rules[1].text == "invoice")
        #expect(rules[2].kind == .olderThan && rules[2].days == 60)
        #expect(rules[3].kind == .links && rules[4].kind == .links)
        #expect(r.ruleDrawerNames == ["Photos", "Receipts", "Old stuff", "Links", "More links"])
    }

    @Test func sameNameIsOneDrawerAndAlikeSlugsGetOwnIds() {
        let r = PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("images", "My Photos")),\(rule("documents", "my photos")),\(rule("archives", "My-Photos"))]}}")
        #expect(r.ok)
        let ids = r.rules!.map(\.drawer)
        #expect(ids[0] == ids[1])
        #expect(ids[2] != ids[0])
        #expect(ids.allSatisfy { DrawerIds.isValid($0) })
    }

    @Test func eachBadRuleIsNamedByField() {
        let r = PastedSetup.read("""
            {"drawers":{"rules":[{"kind":"move_files","drawer":"X"},{"kind":"images","drawer":".hidden"},
              {"kind":"name_contains","drawer":"Y"},{"kind":"older_than","days":0,"drawer":"Z"}]}}
            """)
        #expect(!r.ok)
        #expect(r.rules == nil)
        for f in ["drawers.rules[0].kind", "drawers.rules[1].drawer", "drawers.rules[2].text", "drawers.rules[3].days"] {
            #expect(fields(r).contains(f), "\(f)")
        }
    }

    @Test func daysEdges() {
        func one(_ d: String) -> PastedSetupRead { PastedSetup.read("{\"drawers\":{\"rules\":[{\"kind\":\"older_than\",\"drawer\":\"Old\",\"days\":\(d)}]}}") }
        #expect(one("1").ok)
        #expect(one("3650").ok)
        #expect(one("60.0").ok)
        for bad in ["0", "-5", "3651", "60.5", "\"60\"", "null"] { #expect(fields(one(bad)).contains("drawers.rules[0].days"), "\(bad)") }
        #expect(fields(PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("older_than", "Old"))]}}")).contains("drawers.rules[0].days"))
    }

    @Test func textEdgesAndKinds() {
        func named(_ t: String) -> PastedSetupRead { PastedSetup.read("{\"drawers\":{\"rules\":[{\"kind\":\"name_contains\",\"drawer\":\"A\",\"text\":\"\(t)\"}]}}") }
        #expect(named(String(repeating: "t", count: 60)).ok)
        #expect(fields(named(String(repeating: "t", count: 61))).contains("drawers.rules[0].text"))
        #expect(fields(named("   ")).contains("drawers.rules[0].text"))
        #expect(fields(named("a/b")).contains("drawers.rules[0].text"))
        #expect(fields(named("a:b")).contains("drawers.rules[0].text"))
        #expect(fields(named("a\\\\b")).contains("drawers.rules[0].text"))
        #expect(!PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("name_starts_with", "B"))]}}").ok)
        let r = PastedSetup.read("{\"drawers\":{\"rules\":[{\"kind\":\"images\",\"drawer\":\"A\",\"text\":\"x\"},{\"kind\":\"name_starts_with\",\"drawer\":\"B\",\"text\":\" Inv \"}]}}")
        #expect(r.ok)
        #expect(r.rules![0].text == nil)
        #expect(r.rules![1].text == "Inv")
    }

    @Test func drawerNameEdges() {
        func drawer(_ n: String) -> PastedSetupRead { PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("images", n))]}}") }
        #expect(drawer(String(repeating: "d", count: 40)).ok)
        #expect(fields(drawer(String(repeating: "d", count: 41))).contains("drawers.rules[0].drawer"))
        for bad in ["", "  ", ".", "..", "Pictures/Old", "a:b", "~/Pictures", "..\\\\x", "C:\\\\x"] {
            #expect(!drawer(bad).ok, "\(bad)")
        }
        #expect(!PastedSetup.read("{\"drawers\":{\"rules\":[{\"kind\":\"images\",\"drawer\":\"A\",\"path\":\"/x\"}]}}").ok)
        #expect(!PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("images", "A"))],\"files\":[\"a\"]}}").ok)
    }

    @Test func ruleCountEdges() {
        func rules(_ n: Int) -> String { (0..<n).map { rule("images", "D\($0)") }.joined(separator: ",") }
        #expect(PastedSetup.read("{\"drawers\":{\"rules\":[\(rules(20))]}}").ok)
        let many = PastedSetup.read("{\"drawers\":{\"rules\":[\(rules(21))]}}")
        #expect(!many.ok)
        #expect(fields(many).contains("drawers.rules"))
        #expect(fields(PastedSetup.read("{\"drawers\":{\"rules\":[]}}")).contains("drawers.rules"))
        #expect(fields(PastedSetup.read("{\"drawers\":{\"rules\":\"images\"}}")).contains("drawers.rules"))
        #expect(!PastedSetup.read("{\"drawers\":{\"rules\":[\"images\"]}}").ok)
    }

    // MARK: look, opacity, arrange

    @Test func aLookOnlyDrawersPartIsRead() {
        let r = PastedSetup.read("{\"drawers\":{\"look\":\"glass\",\"opacity\":70,\"arrange\":{\"corner\":\"bl\",\"direction\":\"row\"}}}")
        #expect(r.ok)
        #expect(r.rules == nil)
        #expect(r.drawerLook == PastedDrawerLook(look: .glass, opacity: 70, corner: .bottomLeft, flow: .row))
    }

    @Test func anArrangeWithOnePartSaysWhichPartItNamed() {
        let c = PastedSetup.read("{\"drawers\":{\"arrange\":{\"corner\":\"tl\"}}}").drawerLook!
        #expect(c.corner == .topLeft && c.flow == nil && c.hasArrange)
        let f = PastedSetup.read("{\"drawers\":{\"arrange\":{\"direction\":\"row\"}}}").drawerLook!
        #expect(f.corner == nil && f.flow == .row)
        let none = PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("images", "A"))]}}")
        #expect(none.drawerLook == nil)
    }

    @Test(arguments: [
        ("{\"drawers\":{\"look\":\"neon\"}}", "drawers.look"),
        ("{\"drawers\":{\"look\":3}}", "drawers.look"),
        ("{\"drawers\":{\"opacity\":19}}", "drawers.opacity"),
        ("{\"drawers\":{\"opacity\":101}}", "drawers.opacity"),
        ("{\"drawers\":{\"opacity\":70.5}}", "drawers.opacity"),
        ("{\"drawers\":{\"opacity\":\"70\"}}", "drawers.opacity"),
        ("{\"drawers\":{\"arrange\":{\"corner\":\"middle\"}}}", "drawers.arrange.corner"),
        ("{\"drawers\":{\"arrange\":{\"corner\":\"topLeft\"}}}", "drawers.arrange.corner"),
        ("{\"drawers\":{\"arrange\":{\"direction\":\"diagonal\"}}}", "drawers.arrange.direction"),
        ("{\"drawers\":{\"arrange\":{\"speed\":3,\"corner\":\"tl\"}}}", "drawers.arrange.speed"),
        ("{\"drawers\":{\"arrange\":{}}}", "drawers.arrange"),
        ("{\"drawers\":{\"arrange\":\"tl\"}}", "drawers.arrange"),
        ("{\"drawers\":{\"colour\":\"red\"}}", "drawers.colour"),
        ("{\"drawers\":{}}", "drawers"),
    ])
    func badDrawerSettingsAreRefusedByField(json: String, field: String) {
        let r = PastedSetup.read(json)
        #expect(!r.ok)
        #expect(fields(r).contains(field))
        #expect(r.drawerLook == nil)
    }

    @Test func opacityEdgesAndAWholeFloat() {
        for (text, want) in [("20", 20), ("100", 100), ("70.0", 70)] {
            #expect(PastedSetup.read("{\"drawers\":{\"opacity\":\(text)}}").drawerLook?.opacity == want)
        }
    }

    // MARK: whole block

    @Test func unknownTopLevelKeysAreRefused() {
        let r = PastedSetup.read("{\"desk\":{},\"files\":[\"a\"]}")
        #expect(!r.ok)
        #expect(fields(r).contains("files"))
    }

    @Test func misshapenPartsAreRefusedNotSkipped() {
        for bad in ["{\"drawers\":[]}", "{\"desk\":[]}", "{\"desk\":null}", "{\"drawers\":null}"] {
            #expect(!PastedSetup.read(bad).ok, "\(bad)")
        }
        #expect(fields(PastedSetup.read("{\"desk\":[]}")) == ["desk"])
    }

    @Test func oneBadFieldRejectsEverythingAndNamesIt() {
        let r = PastedSetup.read("{\"desk\":{\"name\":\"A\"},\"drawers\":{\"rules\":[\(rule("images", "Photos")),\(rule("nope", "X"))],\"look\":\"glass\",\"opacity\":60}}")
        #expect(!r.ok)
        #expect(r.rules == nil)
        #expect(r.drawerLook == nil)
        #expect(r.ruleDrawerNames.isEmpty)
        #expect(fields(r) == ["drawers.rules[1].kind"])
        #expect(r.findings == [PastedFinding(field: "drawers.rules[1].kind", text: r.reasons[0].message, isError: true)])
    }

    @Test func aBadLookHoldsBackTheGoodRules() {
        let r = PastedSetup.read("{\"drawers\":{\"rules\":[\(rule("images", "Photos"))],\"look\":\"neon\",\"opacity\":70}}")
        #expect(!r.ok)
        #expect(r.rules == nil && r.drawerLook == nil)
        #expect(fields(r).contains("drawers.look"))
    }

    @Test func theDeskPartIsKeptRawForTheValidator() {
        let r = PastedSetup.read("{\"desk\":{\"name\":\"Calm\",\"layout\":{\"clock\":{\"anchor\":\"tr\",\"size\":1.2}},\"unknownField\":1}}")
        #expect(r.ok)   // the Desk validator names what the Mac Desk refuses
        let desk = r.desk?.object
        #expect(desk?.has("unknownField") == true)
        #expect(desk?["layout"]?.object?["clock"]?.object?["anchor"]?.string == "tr")
    }

    @Test func aWindowsShapedAnswerReadsCleanly() {
        let r = PastedSetup.read("""
            Here is your setup. I put screenshots and pictures in their own drawers.

            ```json
            {
              "desk": {
                "name": "Evening calm",
                "note": "Dim, quiet, clock on the right.",
                "layout": { "clock": { "anchor": "tr", "size": 1.2 }, "meters": { "style": "ring" } },
                "ink": ["#ffd9a0", "#ff9e7a"],
                "plate": true
              },
              "drawers": {
                "rules": [
                  { "kind": "screenshots", "drawer": "Screenshots" },
                  { "kind": "images", "drawer": "Pictures" },
                  { "kind": "name_contains", "text": "invoice", "drawer": "Receipts" },
                  { "kind": "shortcuts", "drawer": "Shortcuts" },
                  { "kind": "older_than", "days": 60, "drawer": "Old files" }
                ],
                "look": "glass",
                "opacity": 70,
                "arrange": { "corner": "br", "direction": "column" }
              }
            }
            ```
            """)
        #expect(r.ok, "\(r.reasons)")
        #expect(r.blocks == 1)
        #expect(r.rules?.count == 5)
        #expect(r.drawerLook == PastedDrawerLook(look: .glass, opacity: 70, corner: .bottomRight, flow: .column))
        #expect(r.desk != nil)
        #expect(r.findings.isEmpty)
    }

    @Test func findingsSpeakNeutrallyAndNameNoTool() {
        let f = PastedSetup.findings(errors: [DeskReason("messages", "is not part of a Mac Desk design; send message lines with propose_desk_messages")],
                                     warnings: [DeskReason("font", "is not installed")])
        #expect(f.count == 2 && f[0].isError && !f[1].isError)
        #expect(!f[0].text.contains("propose_"))
        #expect(PastedSetup.neutral("Claude cannot do it") == "an AI chat cannot do it")
    }

    @Test func everyFindingCopyHasNoEmDash() {
        let bad = ["{\"drawers\":{\"opacity\":1,\"look\":1,\"arrange\":{}, \"x\":1}}", "x", "{\"desk\":1}"]
        for b in bad { for f in PastedSetup.read(b).findings { #expect(!f.text.contains("\u{2014}")) } }
    }
}

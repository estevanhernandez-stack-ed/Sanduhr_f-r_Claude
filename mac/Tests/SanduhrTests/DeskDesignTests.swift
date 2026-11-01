import Foundation
import Testing
@testable import Sanduhr

/// get_desk and propose_desk, the app's half (spec 8): the design model and its validator, the request
/// handoff, and desk-state.json. The shared fixtures (`Fixtures/desk-designs/`) are the same files the Python
/// MCP's `design_validate` is held to in `integrations/tests/test_propose_desk.py`.
@Suite("Desk design fixtures")
struct DeskDesignFixtureTests {
    static var root: String {
        Bundle.module.resourceURL!.appendingPathComponent("Fixtures/desk-designs").path
    }

    static func expected() -> [String: Any] {
        let data = FileManager.default.contents(atPath: root + "/expected.json")!
        return try! JSONSerialization.jsonObject(with: data) as! [String: Any]
    }

    static func design(_ file: String) -> PackValue {
        TJ.parse(String(decoding: FileManager.default.contents(atPath: root + "/" + file)!, as: UTF8.self))
    }

    @Test func theCorpusIsAllThere() {
        let names = ((try? FileManager.default.contentsOfDirectory(atPath: Self.root)) ?? []).filter { $0 != "expected.json" }
        #expect(Set(names) == Set((Self.expected()["cases"] as! [String: Any]).keys))
        #expect(names.count >= 12)
    }

    @Test func everyDesignIsJudgedAsExpected() throws {
        let all = Self.expected()
        let installed = all["installed_fonts"] as! [String]
        let cases = all["cases"] as! [String: [String: Any]]
        for (file, want) in cases.sorted(by: { $0.key < $1.key }) {
            let v = DeskDesign.validate(Self.design(file), context: DeskDesignContext(installedFonts: installed))
            let detail = v.reasons.map(\.description).joined(separator: " | ")
            #expect(v.status.word == (want["status"] as! String), "\(file): status \(v.status.word): \(detail)")
            let wantReasons = (want["reasons"] as! [[String: String]]).map { DeskReason($0["field"]!, $0["message"]!) }
            #expect(v.reasons == wantReasons, "\(file): reasons \(detail)")
            let wantFonts = (want["fonts"] as! [[String: Any]]).map { MissingFont(family: $0["family"] as! String, link: $0["link"] as? String) }
            #expect(v.fonts == wantFonts, "\(file): fonts")
            if let wantDesign = want["design"] {
                let json = WidgetJson.compact(.object(try #require(v.design).toJson()))
                let got = try JSONSerialization.jsonObject(with: Data(json.utf8), options: [.fragmentsAllowed])
                #expect((got as AnyObject).isEqual(wantDesign), "\(file): design \(json)")
            } else {
                #expect(v.design == nil, "\(file): a refusal made a design")
            }
        }
    }
}

@Suite("Desk design")
struct DeskDesignTests {
    static let now = Date(timeIntervalSince1970: 1_792_692_000)   // 2026-10-22T18:00:00Z

    private func request(id: String = "abc", at: String = "2026-10-22T18:00:00.000Z", schema: String = "1", design: String = #"{"plate":true}"#) -> Data {
        Data(#"{"schema_version":\#(schema),"id":"\#(id)","requested_at":"\#(at)","design":\#(design)}"#.utf8)
    }

    @Test func theHandoffNamesAndLimitsMatchTheServer() {
        #expect(DeskDesignHandoff.requestFile == "desk-request.json")
        #expect(DeskDesignHandoff.resultFile == "desk-result.json")
        #expect(DeskDesignHandoff.stateFile == "desk-state.json")
        #expect(DeskDesignHandoff.maxAge == 600)
        #expect(DeskDesign.maxJsonBytes == 128 * 1024)
        #expect(DeskAnswer.statuses == ["previewing", "applied", "rejected", "needs_font"])
        #expect(DeskDesign.fields == ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow"])
    }

    @Test func aGoodRequestIsAProposal() {
        let r = DeskDesignHandoff.decode(request(), now: Self.now)
        #expect(r.kind == .proposal)
        #expect(r.id == "abc")
        #expect(r.validation?.design?.plate == true)
    }

    @Test func aRequestOlderThanTenMinutesOrUnreadableIsDropped() {
        let old = request(at: "2026-10-22T17:49:00.000Z")
        #expect(DeskDesignHandoff.decode(old, now: Self.now).kind == .discard)
        #expect(DeskDesignHandoff.decode(Data("nope".utf8), now: Self.now).kind == .discard)
        #expect(DeskDesignHandoff.decode(Data(#"{"requested_at":"2026-10-22T18:00:00Z"}"#.utf8), now: Self.now).kind == .discard)
        #expect(DeskDesignHandoff.decode(Data(count: DeskDesignHandoff.maxRequestBytes + 1), now: Self.now).kind == .discard)
    }

    @Test func aRequestWithABadStampOrSchemaIsRefusedWithItsId() {
        let noDate = DeskDesignHandoff.decode(request(at: "yesterday"), now: Self.now)
        #expect(noDate.kind == .refused)
        #expect(noDate.reasons.first?.field == "requested_at")
        let schema = DeskDesignHandoff.decode(request(schema: "2"), now: Self.now)
        #expect(schema.kind == .refused)
        #expect(schema.id == "abc")
        #expect(schema.reasons.first?.field == "schema_version")
    }

    @Test func aDesignThatBreaksARuleIsRefusedAgainWithItsReasons() {
        let r = DeskDesignHandoff.decode(request(design: #"{"layout":{"clock":{"anchor":"ml","tilt":-10},"claude":{"anchor":"ml","tilt":12}}}"#), now: Self.now)
        #expect(r.kind == .refused)
        #expect(r.reasons.first?.message == "the pieces at ml share one slant; give clock and claude the same tilt")
        let windows = DeskDesignHandoff.decode(request(design: #"{"launcher":{"buttons":[]},"layout":{"system":{"anchor":"tr"}}}"#), now: Self.now)
        #expect(windows.kind == .refused)
        #expect(Set(windows.reasons.map(\.field)) == ["launcher", "layout.system"])
    }

    @Test func aFontTheMacLacksWaitsForIt() {
        let context = DeskDesignContext(installedFonts: ["Caveat"])
        let r = DeskDesignHandoff.decode(request(design: #"{"font":{"family":"Pacifico","link":"https://example.com/p"}}"#), now: Self.now, context: context)
        #expect(r.kind == .proposal)
        #expect(r.validation?.status == .needsFont)
        #expect(r.validation?.fonts == [MissingFont(family: "Pacifico", link: "https://example.com/p")])
        let known = DeskDesignHandoff.decode(request(design: #"{"font":"caveat"}"#), now: Self.now, context: context)
        #expect(known.validation?.status == .ok)
        let unknownList = DeskDesignHandoff.decode(request(design: #"{"font":"Anything"}"#), now: Self.now)
        #expect(unknownList.validation?.status == .ok)   // the list is unknown: the rule is skipped
    }

    @Test func theResultJsonCarriesTheStatusAndItsDetailsAndNothingElse() throws {
        let answer = DeskAnswer("needs_font", reasons: [DeskReason("ink", "no")],
                                fonts: [MissingFont(family: "Caveat", link: nil)], changes: ["Ink: white to #ffaa00"])
        let data = DeskDesignHandoff.resultJson(id: "abc", answer: answer, now: Self.now)
        let root = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(root["id"] as? String == "abc")
        #expect(root["completed_at"] as? String == "2026-10-22T18:00:00.000Z")
        let result = try #require(root["result"] as? [String: Any])
        #expect(result["status"] as? String == "needs_font")
        #expect((result["reasons"] as? [[String: String]])?.first == ["field": "ink", "message": "no"])
        #expect((result["fonts"] as? [[String: Any]])?.first?["family"] as? String == "Caveat")
        #expect(result["changes"] as? [String] == ["Ink: white to #ffaa00"])
        #expect(Set(result.keys) == ["status", "reasons", "fonts", "changes"])
        let notReady = DeskDesignHandoff.resultJson(id: "x", answer: .notReady, now: Self.now)
        let nr = try #require((try JSONSerialization.jsonObject(with: notReady) as? [String: Any])?["result"] as? [String: Any])
        #expect(nr["reason"] as? String == "not_ready")
    }

    @Test func theDesignRoundTripsThroughItsOwnJson() throws {
        let node = TJ.parse(##"{"name":"  Dusk ","ink":["#9AD7FF","fff"],"layout":{"claude":{"anchor":"tl","size":1.23,"tilt":-4,"style":"RING"}}}"##)
        let first = try #require(DeskDesign.validate(node).design)
        let again = try #require(DeskDesign.validate(.object(first.toJson())).design)
        #expect(again == first)
        #expect(first.layout?.first?.size == 1.2)
        #expect(first.layout?.first?.style == .ring)
        #expect(first.ink == ["#9ad7ff", "#ffffff"])
    }

    @Test(arguments: ["https://example.com", "https://example.com:8443/a?b#c", "HTTPS://Example.com/x"])
    func goodLinksPass(link: String) {
        #expect(DeskDesign.linkProblem(link) == nil)
    }

    @Test(arguments: [
        ("", "Enter a link."), ("http://example.com", "A link must start with https://."),
        ("https://exa mple.com", "A link has no spaces."), ("https://example.com/\"", "A link has no quotes or backslashes."),
        ("https://", "That link has no site name."), ("https://u:p@example.com", "A link has no sign-in part."),
        ("https://example.com:x", "That is not a link."),
    ])
    func badLinksAreRefusedWithWindowsWords(link: String, why: String) {
        #expect(DeskDesign.linkProblem(link) == why)
    }
}

@Suite("Desk state export")
struct DeskStateExportTests {
    private func suite() -> UserDefaults {
        let name = "sanduhr.tests.deskstate.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: name)!
        d.removePersistentDomain(forName: name)
        return d
    }

    private func state(_ d: UserDefaults, fonts: [String] = ["Helvetica Neue", "Caveat"]) throws -> [String: Any] {
        let data = DeskStateExport.stateJson(defaults: d, installedFonts: fonts, writerVersion: "2.37.0", fontAvailable: { _ in true })
        return try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    @Test func theStandardDeskIsReadBack() throws {
        let s = try state(suite())
        #expect(s["schema_version"] as? Int == 1)
        #expect(s["writer_version"] as? String == "2.37.0")
        #expect(s["pieces"] as? [String] == DeskArrangement.known)
        #expect(s["anchors"] as? [String] == ["tl", "tc", "tr", "ml", "mr", "bl", "bc", "br"])
        #expect(s["shown"] as? [String] == ["message", "clock", "claude", "meetings"])
        let design = try #require(s["design"] as? [String: Any])
        #expect(design["font"] as? String == "EsteFont Pro")
        #expect(design["ink"] as? String == "#ffffff")
        #expect(design["plate"] as? Bool == false)
        #expect(design["shadow"] as? Bool == true)
        let layout = try #require(design["layout"] as? [String: [String: Any]])
        #expect(layout["clock"]?["anchor"] as? String == "bl")
        #expect(layout["claude"]?["style"] as? String == "bar")
        #expect(layout["meters"]?["hidden"] as? Bool == true)
        #expect(layout["clock"]?["style"] == nil)
        #expect(s["message_color"] as? String == "9ad7ff")
        #expect(s["notch_text_color"] as? String == "ffffff")
    }

    @Test func layoutTiltStyleInkAndFontsFollowTheSettings() throws {
        let d = suite()
        d.set("clock:ml:1.2:-18 claude:ml:1:-18:slim meters:tr:1:0:ring", forKey: "layout")
        d.set("8f5bd6,3a63e0,33FDFF", forKey: "inkColor")
        d.set(true, forKey: "plate")
        d.set(false, forKey: "inkShadow")
        d.set("Caveat", forKey: "font")
        d.set("Marker Felt", forKey: "messageFont")
        d.set("ffd08a", forKey: "messageColor")
        let s = try state(d)
        let design = try #require(s["design"] as? [String: Any])
        let layout = try #require(design["layout"] as? [String: [String: Any]])
        #expect(layout["clock"]?["tilt"] as? Int == -18)
        #expect(layout["clock"]?["size"] as? Double == 1.2)
        #expect(layout["claude"]?["style"] as? String == "slim")
        #expect(layout["meters"]?["style"] as? String == "ring")
        #expect(layout["message"]?["hidden"] as? Bool == true)
        #expect(design["ink"] as? [String] == ["#8f5bd6", "#3a63e0", "#33fdff"])
        #expect(design["plate"] as? Bool == true)
        #expect(design["shadow"] as? Bool == false)
        #expect(design["font"] as? String == "Caveat")
        let fonts = try #require(s["fonts"] as? [String: Any])
        #expect(fonts["message"] as? String == "Marker Felt")
        #expect(fonts["installed"] as? [String] == ["Helvetica Neue", "Caveat"])
        #expect(fonts["bundled"] as? [String] == ["EsteFont Pro", "EsteFont 26"])
        #expect(s["message_color"] as? String == "ffd08a")
        #expect(s["shown"] as? [String] == ["clock", "claude", "meters"])
    }

    @Test func theLimitsAreWhatTheValidatorEnforces() throws {
        let limits = try #require(try state(suite())["limits"] as? [String: Any])
        #expect((limits["piece_tilt"] as? [String: Int]) == ["min": -20, "max": 20])
        let style = try #require(limits["piece_style"] as? [String: Any])
        #expect(style["pieces"] as? [String] == DeskMeterStyles.pieces)
        #expect(style["values"] as? [String] == ["bar", "slim", "ring", "ticks"])
        #expect(style["default"] as? String == "bar")
        #expect(limits["name_chars"] as? Int == 40)
    }

    @Test func nothingSecretOrWindowsOnlyIsInTheFile() throws {
        let d = suite()
        d.set("a long pinned message line", forKey: "message")
        let data = DeskStateExport.stateJson(defaults: d, installedFonts: [], writerVersion: "2.37.0", fontAvailable: { _ in true })
        let text = String(decoding: data, as: UTF8.self)
        #expect(!text.contains("pinned message"))
        let s = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(Set(s.keys) == ["schema_version", "writer_version", "design", "pieces", "anchors", "fonts", "shown",
                                "message_color", "notch_text_color", "limits", "sprites"])
    }

    @Test func installedFamiliesAreValidNamesOnceEachAndCapped() {
        let many = (0..<2500).map { "Family \($0)" }
        #expect(DeskStateExport.installedFamilies(many).count == DeskStateExport.maxInstalledFonts)
        #expect(DeskStateExport.installedFamilies(["Caveat", "caveat", "", "/etc/evil", "Okay Font"]) == ["Caveat", "Okay Font"])
    }

    @MainActor
    @Test func theExporterWritesOwnerOnlyAndOnlyWhenTheBytesChange() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("deskstate-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("desk-state.json")
        let d = suite()
        let exporter = DeskStateExporter(url: url, defaults: d)
        let first = exporter.writeNow()
        let second = exporter.writeNow()
        d.set("clock:tr", forKey: "layout")
        let third = exporter.writeNow()
        #expect(first && !second && third)
        let mode = try FileManager.default.attributesOfItem(atPath: url.path)[.posixPermissions] as? Int
        #expect(mode == 0o600)
        let s = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        #expect((s["shown"] as? [String]) == ["clock"])
    }
}

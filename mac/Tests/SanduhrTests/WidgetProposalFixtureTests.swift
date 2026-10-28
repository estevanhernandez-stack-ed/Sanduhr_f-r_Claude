import Foundation
import Testing
@testable import Sanduhr

/// The shared proposal fixtures (`Fixtures/widget-proposals/`): the arguments an agent might send
/// `propose_widget`, and `expected.json`, what both builds must make of them. The Python MCP's
/// `build_widget` runs the same files in `integrations/tests/test_propose_widget.py`; a case that only
/// one of the two builds passes means one of them has drifted from Windows' `WidgetProposal.Build`.
///
/// Each entry of `expected.json` is `{ok, id, reasons: [field names], sha256: {path: hex}}` (`id` and
/// `sha256` only for a good proposal; the digests pin every generated file byte for byte, so the two
/// serializers agree on the pack.json, LICENSE and JSON text they write).
@Suite("Widget proposal fixtures")
struct WidgetProposalFixtureTests {
    /// The date the expected digests were made at (the LICENSE carries its year).
    static let now = Date(timeIntervalSince1970: 1_792_692_000)   // 2026-10-22T18:00:00Z

    static var root: String {
        Bundle.module.resourceURL!.appendingPathComponent("Fixtures/widget-proposals").path
    }

    static func expected() -> [String: [String: Any]] {
        let data = FileManager.default.contents(atPath: root + "/expected.json")!
        return try! JSONSerialization.jsonObject(with: data) as! [String: [String: Any]]
    }

    static func args(_ file: String) -> PackObject {
        let text = String(decoding: FileManager.default.contents(atPath: root + "/" + file)!, as: UTF8.self)
        return TJ.obj(text)
    }

    @Test func theCorpusIsAllThere() {
        let names = ((try? FileManager.default.contentsOfDirectory(atPath: Self.root)) ?? []).filter { $0 != "expected.json" }
        #expect(Set(names) == Set(Self.expected().keys))
        #expect(names.count >= 12)
    }

    @Test func everyProposalIsBuiltOrRefusedAsExpected() {
        for (file, want) in Self.expected().sorted(by: { $0.key < $1.key }) {
            let build = WidgetProposal.build(args: Self.args(file), now: Self.now)
            let wantOk = want["ok"] as! Bool
            let detail = build.reasons.map(\.description).joined(separator: " | ")
            #expect(build.ok == wantOk, "\(file): ok \(build.ok), expected \(wantOk): \(detail)")
            #expect(build.reasons.map(\.field) == (want["reasons"] as! [String]), "\(file): reasons \(detail)")
            if wantOk, let pack = build.pack {
                #expect(pack.id == (want["id"] as! String), "\(file): id")
                let digests = want["sha256"] as! [String: String]
                #expect(Set(pack.files.keys) == Set(digests.keys), "\(file): files \(pack.files.keys.sorted())")
                for (path, hex) in digests {
                    #expect(pack.files[path].map(Digest.sha256Hex) == hex, "\(file): \(path)")
                }
            } else {
                #expect(build.pack == nil || wantOk, "\(file): a refusal made a pack")
            }
        }
    }
}

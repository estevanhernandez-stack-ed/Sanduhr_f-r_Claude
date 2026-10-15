import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice A: a kept mod written as a `.sanduhrpack`, which is also a one-plugin
/// marketplace. On made-up homes only.
@Suite("Packs, mod export")
struct ModPackTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    private func kept(_ h: TempHome) throws -> (KeptMods, KeptMod) {
        let root = ".claude-personal/dev-mods/0123456789abcdef/ci-band"
        h.dir("\(root)/.claude-plugin/types/claude-code")
        h.dir("\(root)/hooks")
        h.file("\(root)/.claude-plugin/plugin.json", #"{"name": "ci-band", "version": "0.1.0", "description": "CI runs above the prompt."}"#)
        h.file("\(root)/.claude-plugin/types/claude-code/index.d.ts", "declare const x: number")
        h.file("\(root)/hooks/hooks.json", #"{"modules": ["./register.tsx"]}"#)
        h.file("\(root)/hooks/register.tsx", "export function register() {}")
        let store = KeptMods(dir: URL(fileURLWithPath: h.at("support/mods")))
        let mod = try store.keep(h.at(root), session: "0123456789abcdef", folder: h.at(".claude-personal"), now: Self.at)
        return (store, mod)
    }

    /// Unzips the pack with `ditto` into a new folder and returns it.
    private func unzip(_ pack: URL, in h: TempHome) throws -> URL {
        let out = URL(fileURLWithPath: h.at("unzipped-\(UUID().uuidString.prefix(6))"))
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        p.arguments = ["-x", "-k", pack.path, out.path]
        try p.run()
        p.waitUntilExit()
        #expect(p.terminationStatus == 0)
        return out
    }

    private func json(_ url: URL) throws -> [String: Any] {
        try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
    }

    @Test func thePackIsTheModAMarketplaceAndAManifest() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (store, mod) = try kept(h)
        var check = ModCheckReport()
        check.hooks = ["ui.render{component=AbovePrompt}"]
        let pack = URL(fileURLWithPath: h.at("out/\(ModPack.fileName(mod))"))
        try FileManager.default.createDirectory(at: pack.deletingLastPathComponent(), withIntermediateDirectories: true)
        try ModPack.write(mod, from: URL(fileURLWithPath: store.path(mod)), to: pack, check: check, version: "2.21.0", now: Self.at)
        #expect(pack.lastPathComponent == "ci-band-0.1.0.sanduhrpack")

        let root = try unzip(pack, in: h)
        let found = try FileManager.default.subpathsOfDirectory(atPath: root.path).filter {
            var dir: ObjCBool = false
            FileManager.default.fileExists(atPath: root.appendingPathComponent($0).path, isDirectory: &dir)
            return !dir.boolValue
        }.sorted()
        // The kept files under mod/, Claude Code's types left out, and the two pack files.
        #expect(found == [".claude-plugin/marketplace.json", "mod/.claude-plugin/plugin.json", "mod/hooks/hooks.json",
                          "mod/hooks/register.tsx", "sanduhr-pack.json"])

        let market = try json(root.appendingPathComponent(".claude-plugin/marketplace.json"))
        #expect(market["name"] as? String == "ci-band-pack" && market["description"] as? String == "ci-band, shared from Sanduhr.")
        #expect((market["owner"] as? [String: Any])?["name"] as? String != nil)
        let plugins = try #require(market["plugins"] as? [[String: Any]])
        #expect(plugins.count == 1 && plugins[0]["name"] as? String == "ci-band" && plugins[0]["source"] as? String == "./mod")

        let manifest = try json(root.appendingPathComponent("sanduhr-pack.json"))
        #expect(manifest["schema"] as? Int == 1 && manifest["kind"] as? String == "mod")
        #expect(manifest["created_by"] as? String == "Sanduhr 2.21.0")
        let modDoc = try #require(manifest["mod"] as? [String: Any])
        #expect(modDoc["name"] as? String == "ci-band" && modDoc["version"] as? String == "0.1.0" && modDoc["stamp"] as? String == mod.stamp)
        #expect((modDoc["check"] as? [String: Any])?["hooks"] as? [String] == ["ui.render{component=AbovePrompt}"])
        // Every listed file is there with its size and hash.
        let files = try #require(manifest["files"] as? [[String: Any]])
        #expect(files.map { $0["path"] as? String } == found.filter { $0.hasPrefix("mod/") })
        for f in files {
            let data = try Data(contentsOf: root.appendingPathComponent(f["path"] as! String))
            #expect(f["bytes"] as? Int == data.count)
            #expect(f["sha256"] as? String == SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined())
        }
        // Nothing about where it came from: no home path, no folder, no session.
        let text = try String(contentsOf: root.appendingPathComponent("sanduhr-pack.json"), encoding: .utf8)
        for leak in [h.at(""), ".claude-personal", "0123456789abcdef", "01234567", NSHomeDirectory()] {
            #expect(!text.contains(leak), "manifest names \(leak)")
        }
    }

    @Test func anExistingFileIsReplacedAndAMissingCopyRefuses() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (store, mod) = try kept(h)
        let pack = URL(fileURLWithPath: h.at("ci.sanduhrpack"))
        try Data("old".utf8).write(to: pack)
        try ModPack.write(mod, from: URL(fileURLWithPath: store.path(mod)), to: pack, check: nil, now: Self.at)
        let manifest = try json(try unzip(pack, in: h).appendingPathComponent("sanduhr-pack.json"))
        #expect((manifest["mod"] as? [String: Any])?["check"] == nil)

        let elsewhere = URL(fileURLWithPath: h.at("other.sanduhrpack"))
        #expect(throws: ModPack.Failure.unreadable) {
            try ModPack.write(mod, from: URL(fileURLWithPath: h.at("nowhere")), to: elsewhere, check: nil)
        }
        #expect(!FileManager.default.fileExists(atPath: elsewhere.path))
    }

    @Test func fileNamesAreSafe() {
        var mod = KeptMod(name: "ci-band", version: "1.0/beta", stamp: "abcdef012345", keptAt: Self.at, session: "x", files: 1, bytes: 1)
        #expect(ModPack.fileName(mod) == "ci-band-1.0-beta.sanduhrpack")
        mod.version = nil
        #expect(ModPack.fileName(mod) == "ci-band.sanduhrpack")
    }
}

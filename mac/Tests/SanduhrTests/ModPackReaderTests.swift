import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice B: opening a `.sanduhrpack`, checked before anything is unpacked and reviewed as
/// a proposal. On made-up homes only.
@Suite("Packs, opening a mod pack") @MainActor
struct ModPackReaderTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    /// A kept mod exported as a pack, in its own made-up home.
    private func exported(_ h: TempHome, name: String = "ci-band") throws -> (KeptMods, KeptMod, URL) {
        let root = ".claude-personal/dev-mods/0123456789abcdef/\(name)"
        h.dir("\(root)/.claude-plugin")
        h.dir("\(root)/hooks")
        h.file("\(root)/.claude-plugin/plugin.json", #"{"name": "\#(name)", "version": "0.1.0", "description": "CI runs above the prompt."}"#)
        h.file("\(root)/hooks/hooks.json", #"{"modules": ["./register.tsx"]}"#)
        h.file("\(root)/hooks/register.tsx", "export function register() {}")
        let store = KeptMods(dir: URL(fileURLWithPath: h.at("exporter/mods")))
        let mod = try store.keep(h.at(root), session: "0123456789abcdef", now: Self.at)
        let pack = URL(fileURLWithPath: h.at("\(name).sanduhrpack"))
        try ModPack.write(mod, from: URL(fileURLWithPath: store.path(mod)), to: pack, check: nil, version: "2.21.0", now: Self.at)
        return (store, mod, pack)
    }

    private func ditto(_ args: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        p.arguments = args
        try p.run()
        p.waitUntilExit()
        #expect(p.terminationStatus == 0)
    }

    /// Unzips `pack`, lets `change` edit the tree, and zips it again in place.
    private func rewrite(_ pack: URL, in h: TempHome, _ change: (URL) throws -> Void) throws {
        let tree = URL(fileURLWithPath: h.at("tree-\(UUID().uuidString.prefix(6))"))
        try ditto(["-x", "-k", pack.path, tree.path])
        try change(tree)
        try FileManager.default.removeItem(at: pack)
        try ditto(["-c", "-k", "--norsrc", tree.path, pack.path])
    }

    private func refusal(_ body: () throws -> Void) -> [String] {
        do { try body() } catch let r as ModPackReader.Refusal { return r.reasons } catch { return ["\(error)"] }
        return []
    }

    private func proposalStore(_ h: TempHome, allowed: Bool) -> ModProposalStore {
        let defaults = UserDefaults(suiteName: "packs-\(UUID().uuidString)")!
        defaults.set(allowed, forKey: ModProposalStore.switchKey)
        let paths = ModProposalStore.Paths(support: URL(fileURLWithPath: h.at("Support")),
                                           kept: KeptMods(dir: URL(fileURLWithPath: h.at("Support/mods"))))
        return ModProposalStore(paths: paths, defaults: defaults, now: { Self.at.addingTimeInterval(5) }, folders: { [] })
    }

    @Test func anExportedPackOpensAsTheSameMod() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (_, mod, pack) = try exported(h)
        let opened = try ModPackReader.open(pack, now: Self.at)
        #expect(opened.proposal.name == "ci-band" && opened.proposal.version == "0.1.0")
        #expect(opened.proposal.summary == "CI runs above the prompt.")
        #expect(opened.proposal.files.map(\.path) == [".claude-plugin/plugin.json", "hooks/hooks.json", "hooks/register.tsx"])
        #expect(opened.proposal.files.last?.text == "export function register() {}")
        #expect(opened.stamp == mod.stamp)
    }

    @Test func aChangedOrPaddedPackIsRefused() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (_, _, pack) = try exported(h)
        try rewrite(pack, in: h) { tree in
            try "export function register(on) { on('tool.call', () => {}) }".write(
                to: tree.appendingPathComponent("mod/hooks/register.tsx"), atomically: true, encoding: .utf8)
        }
        #expect(refusal { _ = try ModPackReader.open(pack, now: Self.at) }.first?.contains("doesn't match its manifest") == true)

        let (_, _, second) = try exported(h, name: "other-band")
        try rewrite(second, in: h) { tree in
            try "extra".write(to: tree.appendingPathComponent("mod/hooks/sneaky.ts"), atomically: true, encoding: .utf8)
        }
        #expect(refusal { _ = try ModPackReader.open(second, now: Self.at) }.first?.contains("doesn't list") == true)
    }

    @Test func aConfigPackOrAPlainZipIsNotAModPack() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (_, _, pack) = try exported(h)
        try rewrite(pack, in: h) { tree in
            let url = tree.appendingPathComponent(ModPack.manifestName)
            var doc = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
            doc["kind"] = "config"
            try JSONSerialization.data(withJSONObject: doc).write(to: url)
        }
        #expect(refusal { _ = try ModPackReader.open(pack, now: Self.at) }.first?.contains("config pack") == true)

        let plain = URL(fileURLWithPath: h.at("plain.zip"))
        h.dir("loose")
        h.file("loose/readme.md", "hi")
        try ditto(["-c", "-k", h.at("loose"), plain.path])
        #expect(refusal { _ = try ModPackReader.open(plain, now: Self.at) }.first?.contains("isn't a Sanduhr pack") == true)
        #expect(refusal { _ = try ModPackReader.open(URL(fileURLWithPath: h.at("nowhere")), now: Self.at) } == ["Sanduhr can't read that file."])
    }

    // MARK: The central directory, on hand-made zips

    private struct Raw {
        var name: String
        var size = 10
        var madeBy = 3
        var mode = 0o100644
        var flags = 0
        var method = 8
    }

    /// A zip of central-directory records and an end record only: what `entries` reads.
    private func zip(_ entries: [Raw], count: Int? = nil) -> Data {
        var cd = Data()
        func u16(_ v: Int, _ d: inout Data) { d.append(UInt8(v & 0xFF)); d.append(UInt8((v >> 8) & 0xFF)) }
        func u32(_ v: Int, _ d: inout Data) { u16(v & 0xFFFF, &d); u16((v >> 16) & 0xFFFF, &d) }
        for e in entries {
            let name = Data(e.name.utf8)
            u32(0x0201_4B50, &cd); u16(e.madeBy << 8 | 20, &cd); u16(20, &cd); u16(e.flags, &cd); u16(e.method, &cd)
            u16(0, &cd); u16(0, &cd); u32(0, &cd); u32(e.size, &cd); u32(e.size, &cd)
            u16(name.count, &cd); u16(0, &cd); u16(0, &cd); u16(0, &cd); u16(0, &cd); u32(e.mode << 16, &cd); u32(0, &cd)
            cd.append(name)
        }
        var out = Data([0x50, 0x4B, 0x03, 0x04])   // a token local header before the directory
        let offset = out.count
        out.append(cd)
        u32(0x0605_4B50, &out); u16(0, &out); u16(0, &out)
        u16(count ?? entries.count, &out); u16(count ?? entries.count, &out); u32(cd.count, &out); u32(offset, &out); u16(0, &out)
        return out
    }

    @Test func theDirectoryIsCheckedBeforeAnythingIsUnpacked() throws {
        let fine = try ModPackReader.entries(zip([Raw(name: "mod/"), Raw(name: "mod/a.ts"), Raw(name: "__MACOSX/._a.ts"), Raw(name: "mod/._a.ts"),
                                                  Raw(name: "mod/.DS_Store")]))
        #expect(fine.map(\.name) == ["mod", "mod/a.ts"])

        let cases: [([Raw], String)] = [
            ([Raw(name: "../evil.ts")], "'..'"),
            ([Raw(name: "mod/../../evil.ts")], "'..'"),
            ([Raw(name: "/etc/passwd")], "relative"),
            ([Raw(name: "mod\\evil.ts")], "relative"),
            ([Raw(name: "mod//a.ts")], "'//'"),
            ([Raw(name: "mod/link", mode: 0o120755)], "link"),
            ([Raw(name: "mod/fifo", mode: 0o010644)], "link or a special file"),
            ([Raw(name: "mod/a.ts", flags: 1)], "encrypted"),
            ([Raw(name: "mod/a.ts", method: 12)], "compressed"),
            ([Raw(name: "mod/big.ts", size: (2 << 20) + 1)], "larger than 2 MB"),
            ([Raw(name: "mod/a.ts"), Raw(name: "MOD/A.ts")], "twice"),
            ((0..<11).map { Raw(name: "mod/f\($0).ts", size: 2 << 20) }, "more than 20 MB"),
            ((0..<257).map { Raw(name: "mod/f\($0).ts") }, "at most 256"),
        ]
        for (entries, expected) in cases {
            let why = refusal { _ = try ModPackReader.entries(zip(entries)) }
            #expect(why.first?.contains(expected) == true, "\(entries.first?.name ?? ""): \(why)")
        }
        #expect(refusal { _ = try ModPackReader.entries(Data("not a zip at all, just words".utf8)) } == ["It isn't a zip file."])
        #expect(refusal { _ = try ModPackReader.entries(zip([Raw(name: "a")], count: 0xFFFF)) }.first?.contains("ZIP64") == true)
    }

    // MARK: Reviewed as a proposal

    @Test func anOpenedPackWaitsForReviewAndClaudeNeverHearsOfIt() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (_, _, pack) = try exported(h)
        // The agents switch is off: the user opened the file, so it still lands.
        let store = proposalStore(h, allowed: false)
        guard case .staged(let r) = try store.open(try ModPackReader.open(pack, now: Self.at)) else {
            Issue.record("expected the pack to be staged")
            return
        }
        #expect(r.fromFile && r.isPending && r.name == "ci-band")
        #expect(store.pendingFromFiles.map(\.id) == [r.id] && store.pendingFromClaude.isEmpty)
        #expect(store.isStaged(r))
        // The server's index has no line for it.
        let index = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: h.at("Support/mod-proposals.json")))) as? [String: Any])
        #expect((index["proposals"] as? [Any])?.isEmpty == true)
        // Try and Keep use the folder they're given, as no session sent it.
        let personal = h.at(".claude-personal")
        let tried = try store.trySession(r.id, claude: "/c", fallback: personal, open: false)
        defer { try? FileManager.default.removeItem(at: tried.script.deletingLastPathComponent()) }
        #expect(try String(contentsOf: tried.script, encoding: .utf8).contains("CLAUDE_CONFIG_DIR="))
        let kept = try store.approve(r.id, fallback: personal)
        #expect(kept.folder == personal && kept.session == "pack" && kept.proposed == true)

        // The same pack again: kept already, nothing staged.
        guard case .alreadyKept(let again) = try store.open(try ModPackReader.open(pack, now: Self.at)) else {
            Issue.record("expected the pack to be recognised as kept")
            return
        }
        #expect(again.stamp == kept.stamp && store.pending.isEmpty)
    }
}

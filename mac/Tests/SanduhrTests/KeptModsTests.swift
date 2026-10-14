import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice F: keeping a mod made in a session, unchanged and read-only, and forgetting it.
/// On made-up homes only.
@Suite("Mod Studio, kept mods")
struct KeptModsTests {
    private func sessionMod(_ h: TempHome, name: String = "ci-band", body: String = "export function register() {}") -> String {
        let root = ".claude/dev-mods/0123456789abcdef/\(name)"
        h.dir("\(root)/.claude-plugin/types/claude-code")
        h.dir("\(root)/hooks")
        h.file("\(root)/.claude-plugin/plugin.json", #"{"name": "\#(name)", "version": "0.1.0", "description": "CI runs above the prompt."}"#)
        h.file("\(root)/.claude-plugin/types/claude-code/index.d.ts", "declare const x: number")
        h.file("\(root)/hooks/hooks.json", #"{"modules": ["./register.tsx"]}"#)
        h.file("\(root)/hooks/register.tsx", body)
        h.file("\(root)/.DS_Store", "x")
        return h.at(root)
    }

    @Test func keepCopiesThePluginsFilesReadOnlyAndOnce() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let source = sessionMod(h)
        let store = KeptMods(dir: URL(fileURLWithPath: h.at("support/mods")))
        let kept = try store.keep(source, session: "0123456789abcdef", folder: h.at(".claude-personal"),
                                  now: Date(timeIntervalSince1970: 1_790_000_000))
        #expect(kept.folder == h.at(".claude-personal"))
        #expect(kept.name == "ci-band" && kept.version == "0.1.0" && kept.session == "01234567" && kept.files == 3)
        #expect(IntegrationScripts.isStampName(kept.stamp))
        let copy = store.path(kept)
        #expect(copy == h.at("support/mods/ci-band/\(kept.stamp)"))
        // Claude Code's types and Finder's litter stay behind.
        #expect(try KeptMods.files(in: URL(fileURLWithPath: copy))
            == [".claude-plugin/plugin.json", "hooks/hooks.json", "hooks/register.tsx"])
        let perms = try FileManager.default.attributesOfItem(atPath: copy + "/hooks/register.tsx")[.posixPermissions] as? Int
        #expect(perms == 0o444)
        // The same files again: the copy already kept. Changed files: a second version beside it.
        #expect(try store.keep(source, session: "fedcba9876543210") == kept)
        #expect(store.kept(source) == kept)
        try "export function register(on) {}".write(toFile: source + "/hooks/register.tsx", atomically: true, encoding: .utf8)
        #expect(store.kept(source) == nil)
        let second = try store.keep(source, session: "fedcba9876543210", now: Date(timeIntervalSince1970: 1_790_000_100))
        #expect(second.stamp != kept.stamp && store.list().map(\.id) == [second.id, kept.id])
        // The session's own folder is left alone.
        #expect(FileManager.default.fileExists(atPath: source + "/.claude-plugin/types/claude-code/index.d.ts"))
    }

    @Test func forgetDeletesTheCopyAndItsRecord() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let store = KeptMods(dir: URL(fileURLWithPath: h.at("support/mods")))
        let kept = try store.keep(sessionMod(h), session: "0123456789abcdef")
        try store.forget(kept)
        #expect(store.list().isEmpty)
        #expect(!FileManager.default.fileExists(atPath: h.at("support/mods/ci-band")))
        #expect(FileManager.default.fileExists(atPath: h.at(".claude/dev-mods/0123456789abcdef/ci-band/hooks/register.tsx")))
    }

    @Test func whatIsRefused() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let store = KeptMods(dir: URL(fileURLWithPath: h.at("support/mods")))
        #expect(throws: KeptMods.Failure.reserved) { try store.keep(sessionMod(h, name: IntegrationScripts.modName), session: "s") }
        #expect(throws: KeptMods.Failure.badName("CI Band")) {
            let source = sessionMod(h, name: "ci-band-2")
            try #"{"name": "CI Band"}"#.write(toFile: source + "/.claude-plugin/plugin.json", atomically: true, encoding: .utf8)
            try store.keep(source, session: "s")
        }
        h.dir("loose/hooks")
        h.file("loose/hooks/hooks.json")
        #expect(throws: KeptMods.Failure.notAPlugin) { try store.keep(h.at("loose"), session: "s") }
        let linked = sessionMod(h, name: "linked")
        try FileManager.default.createSymbolicLink(atPath: linked + "/hooks/secret", withDestinationPath: h.at(".claude/settings.json"))
        #expect(throws: KeptMods.Failure.hasLink("hooks/secret")) { try store.keep(linked, session: "s") }
        let big = sessionMod(h, name: "big", body: String(repeating: "x", count: KeptMods.maxFileBytes + 1))
        #expect(throws: KeptMods.Failure.tooBig) { try store.keep(big, session: "s") }
        #expect(store.list().isEmpty)
    }

    @Test func tryOpensClaudeOnTheKeptCopyInAnEmptyFolder() {
        let script = KeptMods.tryScript(claude: "/opt/homebrew/bin/claude", mod: "/x/mods/ci-band/abc", folder: "/tmp/it's here")
        #expect(script == """
        #!/bin/zsh
        cd '/tmp/it'\\''s here' || exit 1
        exec /opt/homebrew/bin/claude --plugin-dir /x/mods/ci-band/abc

        """)
        // In the folder whose session made it; the default folder needs no setting.
        let personal = KeptMods.tryScript(claude: "/c", mod: "/m", folder: "/t", config: "/h/.claude-personal", home: "/h")
        #expect(personal.contains("\nCLAUDE_CONFIG_DIR=/h/.claude-personal exec /c --plugin-dir /m\n"))
        #expect(!KeptMods.tryScript(claude: "/c", mod: "/m", folder: "/t", config: "/h/.claude", home: "/h").contains("CLAUDE_CONFIG_DIR"))
    }
}

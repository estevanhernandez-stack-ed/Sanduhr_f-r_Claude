import Foundation
import Testing
@testable import Sanduhr

/// A made-up home with Claude Code folders, and a made-up app support folder with the scripts
/// "inside the app". Nothing here touches the real ~/.claude*, ~/.claude.json or app support.
struct IntegrationRig {
    let home = TempHome()
    let app: URL
    let support: URL
    let python = "/bin/echo"   // exists and is executable; never run

    init() {
        app = URL(fileURLWithPath: home.at("App/Resources/integrations"))
        support = URL(fileURLWithPath: home.at("Support/Sanduhr/integrations"))
        try? FileManager.default.createDirectory(at: app, withIntermediateDirectories: true)
        ship(version: "1")
    }

    /// Puts a version of the scripts in the app, as an update would.
    func ship(version: String) {
        for name in IntegrationScripts.names {
            FileManager.default.createFile(atPath: app.appendingPathComponent(name).path,
                                           contents: Data("# \(name) v\(version)\n".utf8))
        }
    }

    var scripts: IntegrationScripts { IntegrationScripts(source: app, dir: support) }

    func bytes(_ relative: String) -> Data? { FileManager.default.contents(atPath: home.at(relative)) }
    func text(_ relative: String) -> String? { bytes(relative).map { String(decoding: $0, as: UTF8.self) } }
    func json(_ relative: String) -> [String: Any] {
        (bytes(relative).flatMap { try? JSONSerialization.jsonObject(with: $0) } as? [String: Any]) ?? [:]
    }

    /// Every file under the home except the scripts' folder, with its bytes.
    func snapshot() -> [String: Data] {
        var out: [String: Data] = [:]
        let fm = FileManager.default
        guard let e = fm.enumerator(atPath: home.path) else { return out }
        while let rel = e.nextObject() as? String {
            if rel.hasPrefix("Support") || rel.hasPrefix("App") { continue }
            let p = home.at(rel)
            var isDir: ObjCBool = false
            if fm.fileExists(atPath: p, isDirectory: &isDir), !isDir.boolValue { out[rel] = fm.contents(atPath: p) }
        }
        return out
    }

    func cleanUp() { home.cleanUp() }
}

@Suite("Integration scripts")
struct IntegrationScriptsTests {
    @Test func refreshStampsLinksAndPrunes() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        let s = r.scripts
        #expect(!s.isInstalled)
        s.refreshIfInstalled()                               // nothing installed: nothing written
        #expect(!FileManager.default.fileExists(atPath: r.support.path))

        let one = try s.refresh()
        #expect(IntegrationScripts.isStampName(one))
        #expect(s.installedStamp == one && s.isCurrent)
        #expect(try String(contentsOfFile: s.installedPath(IntegrationScripts.mcpScript), encoding: .utf8) == "# sanduhr_mcp.py v1\n")
        #expect(try s.refresh() == one)                      // same scripts: same folder

        // install.sh's flat copies sit beside the stamps and are never touched.
        FileManager.default.createFile(atPath: r.support.appendingPathComponent("sanduhr_mcp.py").path, contents: Data("flat".utf8))

        r.ship(version: "2")
        #expect(!s.isCurrent)
        let two = try s.refresh()
        #expect(two != one)
        // The folder swapped out stays for one refresh, then goes.
        #expect(FileManager.default.fileExists(atPath: r.support.appendingPathComponent(one).path))
        r.ship(version: "3")
        let three = try s.refresh()
        let left = Set(try FileManager.default.contentsOfDirectory(atPath: r.support.path))
        #expect(left == [three, two, "current", "sanduhr_mcp.py"])

        // An altered stamped folder is rebuilt.
        try Data("tampered".utf8).write(to: r.support.appendingPathComponent("\(three)/sanduhr_mcp.py"))
        #expect(try s.refresh() == three)
        #expect(try String(contentsOfFile: s.installedPath(IntegrationScripts.mcpScript), encoding: .utf8) == "# sanduhr_mcp.py v3\n")

        s.removeAll()
        #expect(Set(try FileManager.default.contentsOfDirectory(atPath: r.support.path)) == ["sanduhr_mcp.py"])
    }

    @Test func theRealScriptsHaveAStamp() {
        let repo = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations")
        #expect(IntegrationScripts.stamp(of: repo).map(IntegrationScripts.isStampName) == true)
    }
}

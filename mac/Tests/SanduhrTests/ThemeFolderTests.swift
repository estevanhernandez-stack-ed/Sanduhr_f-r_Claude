import Foundation
import Testing
@testable import Sanduhr

/// Item 34 (c): a user theme deleted outside the app. Temp folders only; the real themes folder,
/// the registry and the real defaults are never touched.

@Suite("Theme deleted outside the app")
struct ThemeFolderTests {
    let gone = Theme(id: "gone", displayName: "Gone", palette: ThemeRegistry.builtIn[0].palette)

    func desk(_ values: [String: Any] = [:]) -> MemoryDefaults {
        let d = MemoryDefaults()
        d.values = values
        return d
    }

    func tempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-themes-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    @Test func fileGoneFallsBackToTheDefault() {
        let result = UsageViewModel.afterReload(gone, fileExists: { _ in false }, desk: desk())
        #expect(result == .fallBack(ThemeRegistry.default))
        #expect(ThemeRegistry.default.id == "obsidian")
    }

    @Test func fileStillThereKeepsTheTheme() {
        // Present but not loading (an editor mid-save): no fallback.
        var asked: [String] = []
        let result = UsageViewModel.afterReload(gone, fileExists: { asked.append($0); return true }, desk: desk())
        #expect(result == .keep)
        #expect(asked == ["gone"])
    }

    @Test func resolvingThemesNeverLookAtTheFolder() {
        let aurora = ThemeRegistry.theme(id: "aurora")!
        #expect(UsageViewModel.afterReload(aurora, fileExists: { _ in Issue.record("asked"); return false },
                                           desk: desk()) == .keep)
        // Match Desk under a new ink: the fresh copy.
        let red = desk(["inkColor": "ff0000"])
        guard case .update(let fresh) = UsageViewModel.afterReload(
            DeskThemeMapping.builtIn, fileExists: { _ in false }, desk: red) else {
            Issue.record("expected an update"); return
        }
        #expect(fresh.id == DeskThemeMapping.id)
    }

    @Test func fileIDsAreLowercasedStemsOfJSONFiles() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        for name in ["Sunset.json", "neon.JSON", "notes.txt"] {
            try Data("{}".utf8).write(to: dir.appendingPathComponent(name))
        }
        #expect(UserThemes.fileIDs(in: dir) == ["sunset", "neon"])
        try FileManager.default.removeItem(at: dir.appendingPathComponent("Sunset.json"))
        #expect(UserThemes.fileIDs(in: dir) == ["neon"])
    }

    @Test func watcherSeesADeleteOnce() async throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let file = dir.appendingPathComponent("sunset.json")
        try Data("{}".utf8).write(to: file)
        let queue = DispatchQueue(label: "theme-watcher-test")
        let count = Counter()
        let watcher = ThemeFolderWatcher(dir: dir, queue: queue) { count.bump() }
        // Let it arm, then delete and wait past the settle time.
        try await Task.sleep(nanoseconds: 200_000_000)
        try FileManager.default.removeItem(at: file)
        try Data("{}".utf8).write(to: dir.appendingPathComponent("a.json"))
        var waited = 0
        while count.value == 0 && waited < 40 {
            try await Task.sleep(nanoseconds: 100_000_000)
            waited += 1
        }
        try await Task.sleep(nanoseconds: 500_000_000)
        // The delete and the write a moment later fold into one change.
        #expect(count.value == 1)
        withExtendedLifetime(watcher) {}
    }
}

/// A thread-safe tally for the watcher's callback.
private final class Counter: @unchecked Sendable {
    private let lock = NSLock()
    private var n = 0
    var value: Int { lock.lock(); defer { lock.unlock() }; return n }
    func bump() { lock.lock(); n += 1; lock.unlock() }
}

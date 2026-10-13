import AppKit
import Foundation
import Testing
@testable import Sanduhr

/// AppKit refuses `terminate` while a window has a sheet attached (the signed-out widget's
/// Welcome sheet): every Quit goes through AppQuit, which ends the sheets first.
@Suite("Quit with a sheet up")
@MainActor
struct AppQuitTests {
    @Test func noQuitCallsTerminateDirectly() throws {
        let sources = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("Sources/Sanduhr")
        let files = FileManager.default.enumerator(at: sources, includingPropertiesForKeys: nil)?
            .compactMap { $0 as? URL }.filter { $0.pathExtension == "swift" } ?? []
        #expect(files.count > 50)
        var direct: [String] = []
        for file in files where file.lastPathComponent != "AppQuit.swift" {
            let text = try String(contentsOf: file, encoding: .utf8)
            if text.contains("NSApp.terminate(") || text.contains("NSApplication.terminate(") {
                direct.append(file.lastPathComponent)
            }
        }
        #expect(direct.isEmpty, "Quit through AppQuit.now() or AppDelegate.quitSanduhr(_:): \(direct)")
    }

    @Test func windowsWithoutSheetsEndNothing() {
        let w = NSWindow(contentRect: .init(x: 0, y: 0, width: 100, height: 100), styleMask: [.titled], backing: .buffered, defer: true)
        #expect(AppQuit.endSheets(in: [w]) == 0)
    }

    @Test func theAppDelegateAnswersQuit() {
        #expect(AppDelegate.instancesRespond(to: #selector(AppDelegate.quitSanduhr(_:))))
    }
}

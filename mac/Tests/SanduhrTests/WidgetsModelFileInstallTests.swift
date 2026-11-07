import Foundation
import Testing
@testable import Sanduhr

/// Settings, Widgets, "Install widget from file..." (spec 20) through `WidgetsModel`: the panel is only the source of
/// bytes. A temp store, a temp defaults suite standing in for the Desk's, and no network.
@Suite("Widgets model, install from file")
@MainActor
struct WidgetsModelFileInstallTests {
    private let companionKey = CompanionSettings.packKey

    private func model(_ root: WidgetTestRoot, _ scratch: WidgetScratchDefaults) -> WidgetsModel {
        WidgetsModel(store: root.store(), defaults: scratch.defaults,
                     makeClient: { WidgetCatalogClient(fetch: { _, _ in nil }, marker: $0) }, changed: {})
    }

    private func zip(_ name: String, version: String? = nil) -> Data {
        var files = WidgetPackFixtures.load("good", name)
        if let version { files = StoreFixtures.withVersion(files, version) }
        return WidgetTestKit.zipPack(files)
    }

    private func desk(_ scratch: WidgetScratchDefaults, on: Bool, companionPlaced: Bool) {
        scratch.defaults.set(on, forKey: DeskController.enabledKey)
        var layout = DeskArrangement(DeskLayout.standard)
        if companionPlaced { layout.show("companion") } else { layout.hide("companion") }
        scratch.defaults.set(layout.string, forKey: DeskArrangeMode.layoutKey)
    }

    private func name(_ pack: String) -> String {
        StoreFixtures.valid("good", pack).manifest.name
    }

    @Test func aSpriteBecomesTheCompanionAndTheLineSaysWhereItWent() {
        let cases: [(on: Bool, placed: Bool, tail: String)] = [
            (true, true, " is on your Desk now. It came from your file and stays on this Mac."),
            (false, true, " is your companion now. Turn the Desk on in Settings, Desk to see it."),
            (true, false, " is your companion now. It is hidden; tick Companion in Settings, Desk to show it."),
        ]
        for c in cases {
            let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
            desk(scratch, on: c.on, companionPlaced: c.placed)
            let layoutBefore = scratch.defaults.string(forKey: DeskArrangeMode.layoutKey)
            let m = model(root, scratch)
            m.installFromFile(zip("good-sprite"))
            #expect(m.fileInstallStatus == name("good-sprite") + c.tail, "\(m.fileInstallStatus ?? "nil")")
            #expect(scratch.defaults.string(forKey: companionKey) == "good-sprite")
            #expect(m.companion.pack == "good-sprite")
            // The layout is the Desk's: read, never written.
            #expect(scratch.defaults.string(forKey: DeskArrangeMode.layoutKey) == layoutBefore)
            let row = root.store().load().find("good-sprite")
            #expect(row?.source == .local && row?.madeHere == true)
            #expect(m.installed.first { $0.id == "good-sprite" }?.madeHere == true)
        }
    }

    @Test func theDeskItselfIsNotTurnedOnOrPlaced() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        let m = model(root, scratch)
        m.installFromFile(zip("good-sprite"))
        #expect(scratch.defaults.object(forKey: DeskController.enabledKey) == nil)
        #expect(scratch.defaults.object(forKey: DeskArrangeMode.layoutKey) == nil)
    }

    @Test func aPaletteInstallsAndTheCompanionStaysAsItWas() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        scratch.defaults.set("earlier-pick", forKey: companionKey)
        let m = model(root, scratch)
        m.installFromFile(zip("good-palette"))
        #expect(m.fileInstallStatus == name("good-palette") + " is installed from your file. It stays on this Mac.")
        #expect(scratch.defaults.string(forKey: companionKey) == "earlier-pick")
        #expect(root.store().load().find("good-palette")?.source == .local)

        // With no pick before, there is still none.
        let other = WidgetScratchDefaults()
        let m2 = model(WidgetTestRoot(), other)
        m2.installFromFile(zip("good-palette"))
        #expect(other.defaults.object(forKey: companionKey) == nil)
    }

    @Test func aRefusedZipLeavesTheCompanionAndCarriesTheRefusal() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        scratch.defaults.set("earlier-pick", forKey: companionKey)
        let m = model(root, scratch)
        let bad = WidgetTestKit.zipPack(WidgetPackFixtures.load("bad", "bad-no-license"))
        m.installFromFile(bad)
        #expect(m.fileInstallStatus?.contains("LICENSE is missing") == true, "\(m.fileInstallStatus ?? "nil")")
        #expect(scratch.defaults.string(forKey: companionKey) == "earlier-pick")
        #expect(root.store().load().packs.isEmpty && m.installed.isEmpty)

        m.installFromFile(Data(count: WidgetZip.maxZipBytes + 1))
        #expect(m.fileInstallStatus == WidgetFileInstall.tooLarge)
        m.installFromFile(Data("not a zip".utf8))
        #expect(m.fileInstallStatus?.hasSuffix("so nothing was installed.") == true)
        #expect(scratch.defaults.string(forKey: companionKey) == "earlier-pick" && root.store().load().packs.isEmpty)
    }

    @Test func anUnreadableFileGivesTheReadSentenceAndInstallsNothing() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        scratch.defaults.set("earlier-pick", forKey: companionKey)
        let m = model(root, scratch)
        m.installFromFile(nil)
        #expect(m.fileInstallStatus == "The file couldn't be read, so nothing was installed.")
        #expect(root.store().load().packs.isEmpty)
        #expect(scratch.defaults.string(forKey: companionKey) == "earlier-pick")
    }

    @Test func theStatusClearsWhenThePanelOpens() {
        let m = model(WidgetTestRoot(), WidgetScratchDefaults())
        m.installFromFile(nil)
        #expect(m.fileInstallStatus != nil)
        m.clearFileInstallStatus()
        #expect(m.fileInstallStatus == nil)
    }

    @Test func aNewSpriteReplacesAnEarlierPickAndAnUpdateKeepsIt() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        scratch.defaults.set("some-other-sprite", forKey: companionKey)
        let m = model(root, scratch)
        m.installFromFile(zip("good-sprite"))
        #expect(scratch.defaults.string(forKey: companionKey) == "good-sprite")

        // A second sprite takes the pick over.
        m.installFromFile(zip("good-sprite-stages"))
        #expect(scratch.defaults.string(forKey: companionKey) == "good-sprite-stages")
        // The first one again, updated: it becomes the pick again, as an install of a sprite does.
        m.installFromFile(zip("good-sprite", version: "1.0.1"))
        #expect(scratch.defaults.string(forKey: companionKey) == "good-sprite")
        #expect(m.fileInstallStatus?.contains("companion") == true || m.fileInstallStatus?.contains("Desk") == true)

        // Updating the pick itself keeps it and the pack count at two.
        m.installFromFile(zip("good-sprite", version: "1.0.2"))
        #expect(scratch.defaults.string(forKey: companionKey) == "good-sprite")
        #expect(m.installed.count == 2)
        #expect(m.installed.first { $0.id == "good-sprite" }?.version == "1.0.2")
        #expect(m.activeCompanion == "good-sprite")
    }

    @Test func installingPostsThePackChangeSoTheDeskReloads() {
        let root = WidgetTestRoot(), scratch = WidgetScratchDefaults()
        let m = model(root, scratch)
        var posted = 0
        let token = NotificationCenter.default.addObserver(forName: .widgetPacksDidChange, object: nil, queue: nil) { note in
            if note.object as AnyObject? === m { posted += 1 }
        }
        defer { NotificationCenter.default.removeObserver(token) }
        m.installFromFile(zip("good-sprite"))
        #expect(posted == 1)
        m.installFromFile(nil)
        m.installFromFile(Data("nope".utf8))
        #expect(posted == 1)
    }
}

/// The Settings view's source: the button, its label and the zip-only panel (the view is a SwiftUI body, so the
/// strings are pinned from the file).
@Suite("Widgets settings, install from file source")
struct WidgetsSettingsFileInstallSourceTests {
    private func source() throws -> String {
        let file = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Sources/Sanduhr/Views/WidgetsSettings.swift")
        return try String(contentsOf: file, encoding: .utf8)
    }

    @Test func theButtonTheLabelAndTheZipOnlyPanel() throws {
        let text = try source()
        #expect(text.contains("Button(\"Install widget from file\\u{2026}\")"))
        #expect(text.contains(".accessibilityLabel(\"Install widget from file\")"))
        #expect(text.contains("panel.allowedContentTypes = [UTType.zip]"))
        #expect(text.contains("panel.allowsMultipleSelection = false"))
        #expect(text.contains("panel.canChooseDirectories = false"))
        #expect(text.contains("panel.canCreateDirectories = false"))
        #expect(text.contains("WidgetZip.maxZipBytes + 1"))
        #expect(text.contains("model.clearFileInstallStatus()"))
        #expect(text.contains("model.installFromFile("))
    }

    @Test func noEmDashInTheNewCopy() throws {
        let text = try source()
        let start = try #require(text.range(of: "private struct WidgetsFromFileRow"))
        let end = try #require(text.range(of: "private struct WidgetsInstalledRow"))
        #expect(!text[start.lowerBound..<end.lowerBound].contains("\u{2014}"))
    }
}

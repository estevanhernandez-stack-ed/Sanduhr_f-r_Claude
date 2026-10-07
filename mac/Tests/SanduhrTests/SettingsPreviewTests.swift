import AppKit
import Foundation
import SwiftUI
import Testing
@testable import Sanduhr

/// Item 68: every Settings preview is the real surface fed a preview model built from the
/// pane's settings and the live data, through the same functions the surfaces use; sample data is
/// labeled; a preview reports no frames and registers no click areas.
@Suite("Settings previews")
@MainActor
struct SettingsPreviewTests {
    private func suite() -> UserDefaults {
        UserDefaults(suiteName: "sanduhr.tests.previews.\(UUID().uuidString)")!
    }

    // MARK: Which pane shows which preview

    @Test func everyVisiblePaneHasAPreview() {
        #expect(SettingsPreviewKind.of(.notch) == .notch)
        #expect(SettingsPreviewKind.of(.deskLayout) == .layout)
        #expect(SettingsPreviewKind.of(.deskLook) == .look)
        #expect(SettingsPreviewKind.of(.deskMeters) == .meters)
        #expect(SettingsPreviewKind.of(.message) == .message)
        #expect(SettingsPreviewKind.of(.nowPlaying) == .nowPlaying)
        #expect(SettingsPreviewKind.of(.widgetLook) == .widget)
        #expect(SettingsPreviewKind.of(.pacing) == .widget)
        #expect(SettingsPreviewKind.of(.general) == .menuBar)
        #expect(SettingsPreviewKind.of(.integrations) == .integrations)
        // Themes has its gallery; the rest draw nothing on screen.
        for s in [SettingsSection.themes, .alerts, .credentials, .usage, .updates, .about] {
            #expect(SettingsPreviewKind.of(s) == nil)
        }
        // Every kind is reachable from some section.
        let reached = Set(SettingsSection.allCases.compactMap(SettingsPreviewKind.of))
        #expect(reached == Set(SettingsPreviewKind.allCases))
    }

    // MARK: Sample labels

    @Test func sampleLabelsNameWhatIsMadeUp() {
        #expect(PreviewSamples().label == nil)
        #expect(PreviewSamples.meters.label == "Sample: meters")
        #expect(PreviewSamples([.track, .meters]).label == "Sample: meters, track")
        #expect(PreviewSamples([.indicators]).label == "Sample: camera and mic")
        // A card names only what it can show: Look has no track.
        let look = PreviewSamples([.track, .message]).intersection(SettingsPreviewKind.look.relevantSamples)
        #expect(look.label == "Sample: message")
        #expect(SettingsPreviewKind.layout.relevantSamples.isEmpty)
        // The statusline preview always runs on sample input.
        #expect(SettingsPreviewKind.integrations.relevantSamples.contains(.statusline))
    }

    // MARK: Scaled, not cropped

    @Test func scaleFitsBothDimensions() {
        let room = CGSize(width: 500, height: 136)
        for content in [CGSize(width: 680, height: 90), CGSize(width: 300, height: 400), CGSize(width: 2000, height: 50)] {
            let s = PreviewScale.factor(content: content, room: room)
            #expect(content.width * s <= room.width + 0.001)
            #expect(content.height * s <= room.height + 0.001)
        }
        // Never past the maximum, and 1 for nothing measured yet.
        #expect(PreviewScale.factor(content: CGSize(width: 10, height: 10), room: room, maximum: 1.5) == 1.5)
        #expect(PreviewScale.factor(content: .zero, room: room) == 1)
        #expect(PreviewScale.height == 160)
    }

    // MARK: The preview model is the real one, fed the same input

    @Test func freshInstallShowsLabeledSamplesNeverBlank() {
        let live = DeskModel()
        let preview = DeskModel()
        let desk = suite()
        let samples = SurfacePreviewData.fill(preview, from: live, islandUp: true, desk: desk)
        #expect(samples.isSuperset(of: [.meters, .meetings, .message, .track, .watcher]))
        #expect(!preview.meters.isEmpty)
        #expect(preview.claudeLine != nil)
        #expect(preview.meetings.map(\.id) == ["demo-1", "demo-2", "demo-3"])
        #expect(preview.message == DeskModel.demoMessage)
        #expect(preview.nowPlaying?.state == .playing)
        #expect(preview.watchers.count == 1)
        // The indicators' switches are off in a fresh suite: nothing shows, nothing is sample.
        #expect(!samples.contains(.indicators))
        #expect(preview.avSpot == .none)
    }

    @Test func sampleMetersComeFromTheDesksOwnFunctions() {
        let now = Date()
        let sample = SurfacePreviewData.sampleUsage(now: now)
        let preview = DeskModel()
        SurfacePreviewData.fill(preview, from: DeskModel(), islandUp: false, desk: suite(), now: now)
        // The Desk's rows for the same numbers with the saved Meters settings.
        let desk = UserDefaults.desk
        let expected = DeskMeterRow.rows(from: MeterVisibility.visible(sample, hidden: MeterVisibility.hidden(in: desk)),
                                         now: now) { MeterWarningSettings.saved($0, in: desk) }
        #expect(preview.meters.map(\.tier) == expected.map(\.tier))
        #expect(preview.meters.map(\.percent) == expected.map(\.percent))
        #expect(preview.meters.map(\.warning) == expected.map(\.warning))
        #expect(preview.claudeCompact == DeskClaudeText.compact(DeskUsage(usage: sample, fetchedAt: now), now: now))
        #expect(preview.claudeLine == DeskClaudeText.line(DeskUsage(usage: sample, fetchedAt: now)))
    }

    @Test func liveDataWinsOverSamples() {
        let now = Date()
        let live = DeskModel()
        let usage = UsageResponse(tiers: [.fiveHour: TierUsage(utilization: 7, resetsAt: nil)])
        live.update(DeskUsage(usage: usage, fetchedAt: now))
        live.message = "{write} hello"
        live.meetings = [Meeting(id: "m1", time: "9:00", title: "Standup", start: now, end: now + 900, link: nil, service: nil)]
        live.watchers = [SurfacePreviewData.sampleWatcher(now: now)].map { var w = $0; w.id = "a:live"; return w }
        live.nowPlaying = SurfacePreviewData.sampleTrack(.paused, now: now)
        let preview = DeskModel()
        let samples = SurfacePreviewData.fill(preview, from: live, islandUp: true, desk: suite(), now: now)
        #expect(samples.intersection([.meters, .meetings, .message, .track, .watcher]).isEmpty)
        #expect(preview.claudeLine == live.claudeLine)
        #expect(preview.meters == live.meters)
        #expect(preview.message == "{write} hello")
        #expect(preview.meetings.map(\.id) == ["m1"])
        #expect(preview.watchers.map(\.id) == ["a:live"])
        #expect(preview.nowPlaying?.state == .paused)
    }

    @Test func indicatorsFollowTheirSwitchesAndPlacement() {
        let desk = suite()
        desk.set(AVCameraDotMode.always.rawValue, forKey: AVCameraDotMode.key)
        let preview = DeskModel()
        let samples = SurfacePreviewData.fill(preview, from: DeskModel(), islandUp: true, desk: desk)
        #expect(samples.contains(.indicators))
        #expect(preview.avIndicators == AVIndicators(camera: true, mic: false))
        // The spot is the controller's: AVIndicatorPlacement over the saved places and side.
        #expect(preview.avSpot == AVIndicatorPlacement.spot(preview.avIndicators, islandUp: true, in: desk))
        #expect(preview.avSpot == .beside(.saved(in: desk)))
        SurfacePreviewData.fill(preview, from: DeskModel(), islandUp: false, desk: desk)
        #expect(preview.avSpot == .badge)
    }

    // MARK: No frames, no click areas

    @Test func previewsReportNoFrames() {
        #expect(SurfacePreview.reportsFrames(preview: false))
        #expect(!SurfacePreview.reportsFrames(preview: true))
    }

    /// Hosts the Desk's meters and meetings over a filled model, as a preview or not, and returns
    /// the model with whatever frames arrived and how often a click area changed.
    private func host(preview: Bool) -> (DeskModel, Int) {
        let model = DeskModel()
        SurfacePreviewData.fill(model, from: DeskModel(), islandUp: false, desk: suite())
        var hits = 0
        model.onHitAreasChange = { hits += 1 }
        let view = NSHostingView(rootView: VStack {
            DeskPiece(widget: .meters, model: model, alignment: .leading)
            DeskPiece(widget: .meetings, model: model, alignment: .leading)
        }.environment(\.isSurfacePreview, preview))
        view.frame = CGRect(x: 0, y: 0, width: 600, height: 600)
        let window = NSWindow(contentRect: view.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = view
        view.layoutSubtreeIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.2))
        window.contentView = nil
        return (model, hits)
    }

    @Test func aHostedPreviewLeavesTheModelsFramesEmpty() {
        let (preview, hits) = host(preview: true)
        #expect(preview.metersFrame == .zero)
        #expect(preview.meterRowFrames.isEmpty)
        #expect(preview.meetingsFrame == .zero)
        #expect(preview.rowFrames.isEmpty)
        #expect(hits == 0)
        // The same views on the Desk do report: the check above is not vacuous.
        let (desk, deskHits) = host(preview: false)
        #expect(desk.metersFrame != .zero)
        #expect(!desk.rowFrames.isEmpty)
        #expect(deskHits > 0)
    }

    @Test func fillRegistersNoClickAreas() {
        let preview = DeskModel()
        SurfacePreviewData.fill(preview, from: DeskModel(), islandUp: true, desk: suite())
        #expect(preview.onHitAreasChange == nil)
    }

    // MARK: Layout map

    @Test func layoutMapPlacesWhatTheDeskDraws() {
        let layout = "message:tl clock:bl claude:bl meters:br meetings:bl nowPlaying:tr"
        let corners = DeskLayoutMap.corners(layout: layout, showMeetings: true, showClaude: true)
        #expect(corners[.tl] == ["Message"])
        #expect(corners[.bl] == ["Clock and date", "Claude line", "Meetings"])
        #expect(corners[.br] == ["Claude meters (bars)"])
        #expect(corners[.tr] == ["Now playing"])
        // The same pieces DeskLayout.placed says DeskView draws, whatever the switches.
        for (meetings, claude) in [(true, false), (false, true), (false, false)] {
            let map = DeskLayoutMap.corners(layout: layout, showMeetings: meetings, showClaude: claude)
            let names = Set(map.values.flatMap { $0 })
            let placed = DeskLayout.placed(layout, showMeetings: meetings, showClaude: claude)
            let expected = Set(DeskLayout.widgets.filter { placed.contains($0.key) }.map(\.name))
            #expect(names == expected)
        }
    }

    @Test func layoutMapMarginsAddTheDock() {
        let i = DeskLayoutMap.insets(left: 52, right: 52, top: 40, bottom: 60, dock: .bottom, reach: 70, scale: 0.5)
        #expect(i.bottom == 65)
        #expect(i.leading == 26)
        let side = DeskLayoutMap.insets(left: 52, right: 52, top: 40, bottom: 60, dock: .left, reach: 70, scale: 0.5)
        #expect(side.leading == 61)
        #expect(side.bottom == 30)
    }

    // MARK: Notch

    @Test func notchCanvasIsTheWingsWindow() {
        let g = NotchPreviewGeometry.typical
        #expect(g.pad == NotchWingsView.windowPad(notchHeight: g.notch.height))
        #expect(g.wingsWidth == g.notch.width + g.pad * 2)
        #expect(g.notchRect.minX == NotchGlowLayout.reach + g.pad)
        #expect(g.canvasWidth == g.wingsWidth + NotchGlowLayout.reach * 2)
        #expect(NotchPreview.spoken(enabled: true, hasNotch: false).contains("no notch"))
    }

    // MARK: Message, Meters, Now Playing

    @Test func replayOnlyForMovingLines() {
        #expect(DeskMessagePreview.replays("{write} ship it"))
        #expect(DeskMessagePreview.replays("{shimmer} ship it"))
        #expect(DeskMessagePreview.replays("{sweep} ship it"))
        #expect(DeskMessagePreview.replays("{sweep:20} {font:bold} ship it"))
        #expect(!DeskMessagePreview.replays("{font:smallcaps} ship it"))
        #expect(!DeskMessagePreview.replays("{glow} ship it"))
        #expect(!DeskMessagePreview.replays(nil))
        #expect(DeskMessagePreview.spoken("{write} ship it").hasSuffix("ship it"))
    }

    @Test func metersNotesNameHiddenLimits() {
        let m = DeskModel()
        m.update(DeskUsage(usage: SurfacePreviewData.sampleUsage(), fetchedAt: Date()))
        m.reportedTiers = [.fiveHour, .sevenDay, .sevenDayOpus]
        m.meters = m.meters.filter { $0.tier != .sevenDayOpus }
        #expect(DeskMetersPreview.notes(m).first == "Hidden: \(Tier.sevenDayOpus.label)")
    }

    @Test func nowPlayingWingFollowsItsPlace() {
        #expect(NowPlayingPreview.showing(left: .nowPlaying) == .init(left: .nowPlaying, right: .nothing))
        #expect(NowPlayingPreview.showing(left: .meetingOrTime) == .init(left: .nothing, right: .nowPlaying))
        // Paused tracks hide through Now Playing's own rule.
        var prefs = NowPlayingPrefs()
        prefs.hideWhilePaused = true
        #expect(prefs.visible(SurfacePreviewData.sampleTrack(.paused)) == nil)
        #expect(prefs.visible(SurfacePreviewData.sampleTrack(.playing)) != nil)
    }

    // MARK: Menu bar and widget

    @Test func menuBarPreviewReadsAsTheItem() {
        let usage = SurfacePreviewData.sampleUsage()
        let rotate = MenuBarPreview.readings(usage, mode: .rotate)
        #expect(rotate == [MenuBarText.reading(usage, mode: .rotate, step: 0),
                           MenuBarText.reading(usage, mode: .rotate, step: 1)])
        #expect(rotate.map { $0?.text } == ["S 42%", "W 91%"])
        #expect(MenuBarPreview.readings(usage, mode: .higher).map { $0?.text } == ["91%"])
        #expect(MenuBarPreview.spoken([nil], mode: .session).contains("hourglass alone"))
    }

    @Test func menuBarLookIsTheStatusItems() {
        func look(_ p: Int) -> MenuBarItemLook { MenuBarItemLook(MenuBarReading(tier: .fiveHour, percent: p, text: "\(p)%")) }
        #expect(look(12).urgency == .normal)
        #expect(look(75).urgency == .high)
        #expect(look(89).urgency == .high)
        #expect(look(90).urgency == .critical)
        #expect(look(12).title == " 12%")
        #expect(look(12).color == .labelColor)
    }

    @Test func widgetSampleCardsHideAndCompactAsTheWidget() {
        let usage = SurfacePreviewData.sampleUsage()
        let all = UsageViewModel.visibleTiers(usage, hidden: [], compact: false)
        #expect(all.map(\.tier) == [.fiveHour, .sevenDay, .sevenDayOpus])
        #expect(UsageViewModel.visibleTiers(usage, hidden: [.sevenDayOpus], compact: false).map(\.tier) == [.fiveHour, .sevenDay])
        #expect(UsageViewModel.visibleTiers(usage, hidden: [], compact: true).map(\.tier) == [.sevenDay])
        #expect(WidgetPreview.spoken(WidgetCards(rows: all), theme: "Obsidian").contains("Session (5hr) 42%"))
    }

    // MARK: Statusline

    @Test func statuslinePreviewRunsSanduhrsOwnScript() {
        // Without the script there is nothing to run; with it, the script alone, as Claude Code runs it.
        let none = IntegrationScripts(source: URL(fileURLWithPath: "/nonexistent-\(UUID().uuidString)"), dir: URL(fileURLWithPath: "/tmp"))
        #expect(IntegrationInstaller(home: "/tmp", scripts: none).sampleArguments() == nil)
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("previews-\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let script = dir.appendingPathComponent(IntegrationScripts.statuslineScript)
        FileManager.default.createFile(atPath: script.path, contents: Data())
        let some = IntegrationScripts(source: dir, dir: dir)
        #expect(IntegrationInstaller(home: "/tmp", scripts: some).sampleArguments() == [script.path])
        try? FileManager.default.removeItem(at: dir)
        #expect(IntegrationsPreview.placeholder(loaded: true, ran: false, python: nil).hasPrefix("No preview"))
        #expect(IntegrationsPreview.placeholder(loaded: false, ran: false, python: nil) == "Running…")
    }
}

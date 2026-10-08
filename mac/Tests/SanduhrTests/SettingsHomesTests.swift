import Foundation
import Testing
@testable import Sanduhr

/// Settings v2, slice 2 (item 72): one home per feature. The pages and their raw values, the
/// aliases for the pages that merged, and the launch migrations that keep an existing install's
/// choices reading the same. Every test uses a throwaway defaults suite, never the real one.

private func scratch(_ name: String = #function) -> UserDefaults {
    let suite = "sanduhr.tests.settings-v2.\(UUID().uuidString)"
    let d = UserDefaults(suiteName: suite)!
    d.removePersistentDomain(forName: suite)
    return d
}

/// Every key a suite holds, for "a second run writes nothing".
private func snapshot(_ d: UserDefaults) -> [String: String] {
    let keys = ["layout", "showClaude", "hotKeys", "hotKeyJoin", "hotKeySettings", "avPlace", "avSide",
                "notchLeft", "notchRight", "notchStrip"]
    var out: [String: String] = [:]
    for k in keys { if let v = d.object(forKey: k) { out[k] = "\(v)" } }
    return out
}

@Suite("Settings v2 pages")
struct SettingsPagesTests {
    @Test func sixteenPagesInFiveGroups() {
        #expect(SettingsSection.allCases.count == 16)
        #expect(SettingsSection.groups.map(\.sections.count) == [4, 6, 2, 2, 2])
        #expect(SettingsSection.groups.flatMap(\.sections) == SettingsSection.allCases)
        #expect(SettingsSection.allCases.map(\.title) == [
            "General", "Accounts", "Usage", "Alerts",
            "Desk", "Desk Look", "Message", "Notch", "Now Playing", "Watchers",
            "Claude Code", "Mods & Config", "Widget", "Themes", "Updates", "About",
        ])
    }

    @Test func rawValuesThatNamePagesAreKept() {
        for raw in ["general", "alerts", "credentials", "usage", "deskLook", "message", "notch", "nowPlaying",
                    "themes", "updates", "about", "integrations", "mods", "deskLayout", "widgetLook", "watchers"] {
            #expect(SettingsSection(rawValue: raw) != nil, "\(raw)")
            #expect(SettingsSection.resolve(raw)?.anchor == nil, "\(raw)")
        }
    }

    @Test func mergedPagesAliasToTheirNewHome() {
        #expect(SettingsSection(rawValue: "deskMeters") == nil)
        #expect(SettingsSection(rawValue: "pacing") == nil)
        let meters = SettingsSection.resolve("deskMeters")
        #expect(meters?.section == .alerts)
        #expect(meters?.anchor == "each-limit")
        let pacing = SettingsSection.resolve("pacing")
        #expect(pacing?.section == .widgetLook)
        #expect(pacing?.anchor == "pacing")
        #expect(SettingsSection.resolve("layout") == nil)
    }

    @Test func theDebugSettingsActionTakesTheAliases() {
        func parse(_ s: String) -> DebugRequest { DebugLink.parse(URL(string: s)!) }
        #expect(parse("sanduhr://debug/action?name=settings&arg=deskMeters").command
                == .action(.settings(.alerts, anchor: SettingsAnchor.eachLimit), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=desk-meters").command
                == .action(.settings(.alerts, anchor: SettingsAnchor.eachLimit), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=pacing").command
                == .action(.settings(.widgetLook, anchor: SettingsAnchor.pacing), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=watchers").command
                == .action(.settings(.watchers), dir: nil))
        let bad = parse("sanduhr://debug/action?name=settings&arg=meters")
        #expect(bad.command == nil)
        #expect(bad.error?.contains("deskMeters") == true)
    }

    @MainActor @Test func openingAPageClearsAnOldAnchor() {
        let nav = SettingsNavigation()
        nav.selection = .alerts
        nav.anchor = SettingsAnchor.eachLimit
        nav.selection = .notch
        #expect(nav.anchor == nil)
    }

    @Test func previewsFollowTheirPages() {
        #expect(SettingsPreviewKind.of(.alerts) == .meters)
        #expect(SettingsPreviewKind.of(.watchers) == .watchers)
        #expect(SettingsPreviewKind.watchers.relevantSamples == [.watcher])
        #expect(SettingsPreviewKind.integrations.relevantSamples == [.statusline])
    }

    @Test func linksNameTheNewHomes() {
        #expect(WatcherMenu.settingsSection == .watchers)
        #expect(LimitMenu.meterSettings == "Alerts Settings…")
        #expect(SettingsNames.retiredNames(in: "Layout Settings…") == ["Layout Settings…"])
        #expect(SettingsNames.retiredNames(in: "Show the Claude meters on the desktop") == ["Show the Claude meters on the desktop"])
    }

    /// Each control in the names table has exactly one page.
    @Test func everyNamedControlHasOneHome() {
        var homes: [String: Set<SettingsSection>] = [:]
        for (name, page) in SettingsNames.table { homes[name, default: []].insert(page) }
        for (name, pages) in homes { #expect(pages.count == 1, "\(name)") }
        #expect(SettingsNames.table.first { $0.name == SettingsNames.deskSwitch }?.page == .deskLayout)
        #expect(SettingsNames.table.first { $0.name == SettingsNames.showWidget }?.page == .widgetLook)
        #expect(SettingsNames.table.first { $0.name == SettingsNames.metersAbovePrompt }?.page == .integrations)
        #expect(SettingsNames.table.first { $0.name == AVPlace.pickerTitle }?.page == .notch)
    }

    @Test func notchPlacesNoLongerOfferCameraAndMic() {
        #expect(!NotchContent.choices.contains(.avIndicators))
        #expect(NotchContent.choices.count == NotchContent.allCases.count - 1)
    }

    @Test func generalShowsStatusNotSwitches() {
        #expect(SurfaceStatus.desk(on: true) == "Desk: on")
        #expect(SurfaceStatus.notch(on: true, deskOn: false) == "Notch: on, waiting for the Desk")
        #expect(SurfaceStatus.notch(on: false, deskOn: true) == "Notch: off")
        #expect(SurfaceStatus.widget(.whileDeskOff, shown: false) == "Widget: hidden while desk is on, hidden now")
        #expect(SurfaceStatus.menuBar(.rotate).hasPrefix("Menu bar: "))
    }
}

@Suite("Settings v2 migrations")
struct SettingsMigrationTests {
    // MARK: Show the Claude meters on the desktop

    @Test func claudeMetersOffHidesBothPiecesAndRetiresTheKey() {
        let d = scratch()
        d.set(false, forKey: "showClaude")
        d.set("message:tl clock:bl:1.2 claude:bl meters:br meetings:bl", forKey: "layout")
        #expect(SettingsMigrations.claudeMetersSwitch(d))
        #expect(d.string(forKey: "layout") == "message:tl clock:bl:1.2 meetings:bl")
        #expect(d.object(forKey: "showClaude") == nil)
        // Second run: nothing to move.
        #expect(!SettingsMigrations.claudeMetersSwitch(d))
        #expect(d.string(forKey: "layout") == "message:tl clock:bl:1.2 meetings:bl")
    }

    @Test func claudeMetersOffWithTheStandardLayout() {
        let d = scratch()
        d.set(false, forKey: "showClaude")
        SettingsMigrations.claudeMetersSwitch(d)
        #expect(d.string(forKey: "layout") == "message:tl clock:bl meetings:bl")
    }

    @Test func claudeMetersOnLeavesTheLayoutAlone() {
        let d = scratch()
        d.set(true, forKey: "showClaude")
        SettingsMigrations.claudeMetersSwitch(d)
        #expect(d.object(forKey: "layout") == nil)
        #expect(d.object(forKey: "showClaude") == nil)
        let never = scratch()
        #expect(!SettingsMigrations.claudeMetersSwitch(never))
        #expect(never.object(forKey: "layout") == nil)
    }

    @Test func aPieceShownAgainAfterTheMigrationStaysShown() {
        let d = scratch()
        d.set(false, forKey: "showClaude")
        SettingsMigrations.run(d)
        d.set(DeskLayout.placing("claude", in: "tr", layout: d.string(forKey: "layout")!), forKey: "layout")
        SettingsMigrations.run(d)
        #expect(DeskArrangement(d.string(forKey: "layout")!).placement("claude")?.anchor == .tr)
    }

    // MARK: The shortcuts

    @Test func oneShortcutSwitchBecomesTwo() {
        let off = scratch()
        off.set(false, forKey: "hotKeys")
        #expect(SanduhrHotKeys.migrate(off))
        #expect(off.object(forKey: "hotKeyJoin") as? Bool == false)
        #expect(off.object(forKey: "hotKeySettings") as? Bool == false)
        #expect(off.object(forKey: "hotKeys") as? Bool == false)   // kept for an older build

        let unset = scratch()
        #expect(SanduhrHotKeys.isOn(.join, in: unset))
        SanduhrHotKeys.migrate(unset)
        #expect(unset.object(forKey: "hotKeyJoin") as? Bool == true)
        #expect(unset.object(forKey: "hotKeySettings") as? Bool == true)
    }

    @Test func theShortcutSplitIsIdempotent() {
        let d = scratch()
        d.set(true, forKey: "hotKeys")
        SanduhrHotKeys.migrate(d)
        d.set(false, forKey: "hotKeyJoin")
        #expect(!SanduhrHotKeys.migrate(d))
        #expect(!SanduhrHotKeys.isOn(.join, in: d))
        #expect(SanduhrHotKeys.isOn(.settings, in: d))
    }

    @Test func shortcutNames() {
        #expect(SanduhrHotKeys.Shortcut.settings.title == "Option+S opens Settings")
        #expect(SanduhrHotKeys.Shortcut.join.title == "Option+J joins the next meeting")
        #expect(DeskController.hotKeysKey == "hotKeys")
    }

    // MARK: Camera and mic

    @Test func aWingOnCameraAndMicBecomesThePlacement() {
        let d = scratch()
        d.set("avIndicators", forKey: "notchRight")
        d.set("left", forKey: "avSide")
        #expect(AVPlace.saved(in: d) == .right)   // before the migration, as 2.10.0 drew it
        #expect(AVPlace.migrate(d))
        #expect(d.string(forKey: "avPlace") == "right")
        #expect(d.object(forKey: "notchRight") == nil)   // back to its default, the Claude meters
        #expect(NotchContent.saved(.right, in: d) == .meters)
        #expect(!AVPlace.migrate(d))
        #expect(AVPlace.saved(in: d) == .right)
    }

    @Test func theStripOnCameraAndMicBecomesUnderTheCamera() {
        let d = scratch()
        d.set("avIndicators", forKey: "notchStrip")
        AVPlace.migrate(d)
        #expect(AVPlace.saved(in: d) == .strip)
        #expect(d.object(forKey: "notchStrip") == nil)
    }

    @Test func theSidePickerBecomesBesideTheCamera() {
        let left = scratch()
        left.set("left", forKey: "avSide")
        left.set("time", forKey: "notchLeft")
        AVPlace.migrate(left)
        #expect(AVPlace.saved(in: left) == .besideLeft)
        #expect(left.string(forKey: "notchLeft") == "time")
        let unset = scratch()
        AVPlace.migrate(unset)
        #expect(AVPlace.saved(in: unset) == .besideRight)
    }

    @Test func aLeftoverCameraAndMicContentIsCleared() {
        let d = scratch()
        AVPlace.migrate(d)
        d.set("avIndicators", forKey: "notchLeft")   // an older build wrote it after the update
        #expect(AVPlace.migrate(d))
        #expect(d.object(forKey: "notchLeft") == nil)
        #expect(AVPlace.saved(in: d) == .besideRight)
    }

    @Test func besideTheCameraAlsoWritesTheSide() {
        let d = scratch()
        AVPlace.write(.besideLeft, to: d)
        #expect(d.string(forKey: "avSide") == "left")
        AVPlace.write(.strip, to: d)
        #expect(d.string(forKey: "avSide") == "left")
        #expect(AVPlace.saved(in: d).side == nil)
    }

    @Test func aPlacedWingShowsTheIndicatorsOnlyWhileTheyShow() {
        let both = AVIndicators(camera: true, mic: true)
        #expect(NotchContent.effective(.time, at: .left, nowPlaying: nil, idle: .automatic,
                                       indicators: both, avPlace: .left) == .avIndicators)
        // With nothing in use the wing shows its own content, not a default.
        #expect(NotchContent.effective(.time, at: .left, nowPlaying: nil, idle: .automatic,
                                       indicators: AVIndicators(), avPlace: .left) == .time)
        #expect(NotchContent.effective(.time, at: .right, nowPlaying: nil, idle: .automatic,
                                       indicators: both, avPlace: .left) == .time)
        #expect(AVIndicatorPlacement.chosen(left: .time, right: .meters, strip: .meetingOrMeters,
                                            wingText: true, chinText: false, chin: 26, placed: .left) == [.left])
        #expect(AVIndicatorPlacement.chosen(left: .time, right: .meters, strip: .meetingOrMeters,
                                            wingText: false, chinText: false, chin: 26, placed: .left).isEmpty)
        #expect(AVIndicatorPlacement.chosen(left: .time, right: .meters, strip: .meetingOrMeters,
                                            wingText: true, chinText: true, chin: 26, placed: .besideLeft).isEmpty)
    }

    // MARK: A 2.10.0 profile

    /// An existing install's choices read the same after the update: layout, wings, camera and
    /// mic placement, the shortcuts and the menu bar.
    @Test func a2100ProfileReadsTheSame() {
        let d = scratch()
        let layout = "message:tl:1.4 clock:bl claude:bl meters:br nowPlaying:tr watchers:ml meetings:bl"
        d.set(layout, forKey: "layout")
        d.set("watchers", forKey: "notchLeft")
        d.set("avIndicators", forKey: "notchRight")
        d.set("nowPlaying", forKey: "notchStrip")
        d.set("right", forKey: "avSide")
        d.set(false, forKey: "hotKeys")
        d.set(true, forKey: "showClaude")
        d.set("rotate", forKey: MenuBarMode.key)
        d.set(true, forKey: "notch")
        d.set(true, forKey: "deskEnabled")

        SettingsMigrations.run(d)
        let once = snapshot(d)
        #expect(!SettingsMigrations.run(d))
        #expect(snapshot(d) == once)

        #expect(d.string(forKey: "layout") == layout)
        #expect(NotchContent.saved(.left, in: d) == .watchers)
        #expect(NotchContent.saved(.strip, in: d) == .nowPlaying)
        #expect(AVPlace.saved(in: d) == .right)
        #expect(NotchContent.saved(.right, in: d) == .meters)
        #expect(!SanduhrHotKeys.isOn(.join, in: d) && !SanduhrHotKeys.isOn(.settings, in: d))
        #expect(d.string(forKey: MenuBarMode.key) == "rotate")
        #expect(WatcherPlacement.notchSpot(in: d) == .left)
    }
}

@Suite("Settings, Watchers")
struct WatchersPageTests {
    @Test func theNotchPickerWritesTheNotchPagesKeys() {
        let d = scratch()
        #expect(WatcherPlacement.notchSpot(in: d) == .off)
        WatcherPlacement.setNotchSpot(.right, in: d)
        #expect(d.string(forKey: "notchRight") == "watchers")
        #expect(WatcherPlacement.notchSpot(in: d) == .right)
        WatcherPlacement.setNotchSpot(.strip, in: d)
        #expect(d.object(forKey: "notchRight") == nil)
        #expect(d.string(forKey: "notchStrip") == "watchers")
        WatcherPlacement.setNotchSpot(.off, in: d)
        #expect(d.object(forKey: "notchStrip") == nil)
        #expect(WatcherPlacement.notchSpot(in: d) == .off)
        // A place with other content keeps it.
        d.set("time", forKey: "notchLeft")
        WatcherPlacement.setNotchSpot(.right, in: d)
        #expect(d.string(forKey: "notchLeft") == "time")
    }

    @Test func thePageSaysWhyAWaitingWatcherDoesntGlow() {
        #expect(WatcherPlacement.glowStatus(deskOn: false, places: ["desk"]).contains("the Desk is off"))
        #expect(WatcherPlacement.glowStatus(deskOn: true, places: []).contains("aren't placed anywhere"))
        #expect(WatcherPlacement.glowStatus(deskOn: true, places: ["right", "desk"])
                == "Now: watchers show on the right wing and the Desk, so one that waits on you glows the notch once.")
        #expect(WatcherPlacement.glowStatus(deskOn: true, places: ["strip"]).contains("show under the camera"))
        #expect(WatcherPlacement.glowRule.hasPrefix("The notch glows once"))
    }

    @Test func aNotchPlaceThatCantShowSaysSo() {
        #expect(WatcherPlacement.notchHint(spot: .off, notch: false, wingText: true, chinText: false, chin: 0) == nil)
        #expect(WatcherPlacement.notchHint(spot: .left, notch: false, wingText: true, chinText: false, chin: 26)?
            .hasPrefix("The notch is off") == true)
        #expect(WatcherPlacement.notchHint(spot: .strip, notch: true, wingText: true, chinText: false, chin: 26) != nil)
        #expect(WatcherPlacement.notchHint(spot: .strip, notch: true, wingText: true, chinText: true, chin: 26) == nil)
        #expect(WatcherPlacement.notchHint(spot: .right, notch: true, wingText: false, chinText: true, chin: 26) != nil)
    }

    /// The glow follows the placement: the rule the page states is the one WatcherStore uses.
    @Test func placementsDecideTheGlow() {
        let placed = WatcherPlacement.places(notch: true, wingText: true, chinText: false, chin: 26,
                                             left: .watchers, right: .meters, strip: .nothing, layoutPlaced: [])
        #expect(placed == ["left"])
        let none = WatcherPlacement.places(notch: true, wingText: true, chinText: false, chin: 26,
                                           left: .time, right: .meters, strip: .nothing, layoutPlaced: ["clock"])
        #expect(none.isEmpty)
    }
}

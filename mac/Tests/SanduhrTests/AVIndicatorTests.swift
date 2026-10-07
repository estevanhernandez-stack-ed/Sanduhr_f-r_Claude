import Foundation
import CoreGraphics
import CoreAudio
import Testing
@testable import Sanduhr

/// The camera and mic indicators (item 67): what shows, where, their sizes, the read-only menu,
/// the strip's click area, the mic's device following and the debug action. No camera, microphone,
/// window or defaults involved.

@Suite("AV indicators: what shows")
struct AVIndicatorStateTests {
    @Test func eachSignalShowsOnlyWithItsSwitch() {
        #expect(AVIndicators.shown(cameraInUse: true, micInUse: true, cameraSwitch: false, micSwitch: false) == AVIndicators())
        #expect(AVIndicators.shown(cameraInUse: true, micInUse: true, cameraSwitch: true, micSwitch: false)
                == AVIndicators(camera: true, mic: false))
        #expect(AVIndicators.shown(cameraInUse: false, micInUse: true, cameraSwitch: true, micSwitch: true)
                == AVIndicators(camera: false, mic: true))
        #expect(AVIndicators.shown(cameraInUse: false, micInUse: false, cameraSwitch: true, micSwitch: true).any == false)
    }

    @Test func switchesAreOffByDefaultKeys() {
        #expect(AVIndicators.cameraKey == "avCameraDot")
        #expect(AVIndicators.micKey == "avMicGlyph")
        let defaults = UserDefaults(suiteName: "av-test-\(UUID().uuidString)")!
        #expect(defaults.bool(forKey: AVIndicators.cameraKey) == false)
        #expect(defaults.bool(forKey: AVIndicators.micKey) == false)
        #expect(AVIndicatorSide.saved(in: defaults) == .right)
    }

    @Test func spokenNamesWhatIsInUseOnly() {
        #expect(AVIndicators(camera: true).spoken == "Camera in use")
        #expect(AVIndicators(mic: true).spoken == "Microphone in use")
        #expect(AVIndicators(camera: true, mic: true).spoken == "Camera in use, microphone in use")
    }

    @Test func sideResolvesToRight() {
        #expect(AVIndicatorSide.resolve(raw: nil) == .right)
        #expect(AVIndicatorSide.resolve(raw: "nonsense") == .right)
        #expect(AVIndicatorSide.resolve(raw: "left") == .left)
        #expect(AVIndicatorSide.left.label == "Left of the camera")
    }
}

@Suite("AV indicators: placement")
struct AVIndicatorPlacementTests {
    let both = AVIndicators(camera: true, mic: true)

    @Test func nothingShowsWithNothingInUse() {
        #expect(AVIndicatorPlacement.spot(AVIndicators(), islandUp: true, side: .right, chosen: []) == .none)
        #expect(AVIndicatorPlacement.spot(AVIndicators(), islandUp: false, side: .left, chosen: [.strip]) == .none)
    }

    @Test func besideTheCameraByDefault() {
        #expect(AVIndicatorPlacement.spot(both, islandUp: true, side: .right, chosen: []) == .beside(.right))
        #expect(AVIndicatorPlacement.spot(both, islandUp: true, side: .left, chosen: []) == .beside(.left))
    }

    @Test func aChosenPlaceTakesThemFromBesideTheCamera() {
        #expect(AVIndicatorPlacement.spot(both, islandUp: true, side: .right, chosen: [.left]) == .places)
    }

    @Test func withoutTheIslandTheyGetTheirOwnTab() {
        // The island off, or a screen without a notch (no island window): the tab at the top.
        #expect(AVIndicatorPlacement.spot(both, islandUp: false, side: .right, chosen: []) == .badge)
        #expect(AVIndicatorPlacement.spot(AVIndicators(mic: true), islandUp: false, side: .left, chosen: [.right]) == .badge)
    }

    @Test func chosenNeedsAPlaceThatDraws() {
        #expect(AVIndicatorPlacement.chosen(left: .avIndicators, right: .meters, strip: .meetingOrMeters,
                                            wingText: true, chinText: false, chin: 26) == [.left])
        // Wing text off: the wings draw nothing, so they stay beside the camera.
        #expect(AVIndicatorPlacement.chosen(left: .avIndicators, right: .avIndicators, strip: .nothing,
                                            wingText: false, chinText: true, chin: 26) == [])
        #expect(AVIndicatorPlacement.chosen(left: .time, right: .meters, strip: .avIndicators,
                                            wingText: true, chinText: true, chin: 26) == [.strip])
        #expect(AVIndicatorPlacement.chosen(left: .time, right: .meters, strip: .avIndicators,
                                            wingText: true, chinText: true, chin: 0) == [])
        #expect(AVIndicatorPlacement.chosen(left: .time, right: .meters, strip: .avIndicators,
                                            wingText: true, chinText: false, chin: 26) == [])
    }

    @Test func spotNamesForStateYAML() {
        #expect(AVIndicatorSpot.none.name == "none")
        #expect(AVIndicatorSpot.beside(.left).name == "beside_left")
        #expect(AVIndicatorSpot.beside(.right).name == "beside_right")
        #expect(AVIndicatorSpot.places.name == "places")
        #expect(AVIndicatorSpot.badge.name == "badge")
    }
}

@Suite("AV indicators: the Camera and mic choice")
struct AVIndicatorNotchContentTests {
    @Test func standsAsideWithNothingToShow() {
        for place in [NotchContent.Place.left, .right, .strip] {
            #expect(NotchContent.effective(.avIndicators, at: place, hasLine: false, idle: .automatic) == place.fallback)
            #expect(NotchContent.effective(.avIndicators, at: place, hasLine: false, idle: .automatic,
                                           hasIndicators: true) == .avIndicators)
            #expect(NotchContent.effective(.avIndicators, at: place, nowPlaying: nil, idle: .nothing,
                                           indicators: AVIndicators(mic: true)) == .avIndicators)
            #expect(NotchContent.effective(.avIndicators, at: place, nowPlaying: nil, idle: .nothing,
                                           indicators: AVIndicators()) == place.fallback)
        }
    }

    @Test func otherChoicesIgnoreTheIndicators() {
        #expect(NotchContent.effective(.time, at: .left, hasLine: false, idle: .automatic, hasIndicators: true) == .time)
        #expect(NotchContent.effective(.watchers, at: .right, hasLine: false, idle: .automatic,
                                       hasWatcher: false, hasIndicators: true) == .meters)
    }

    @Test func drawsNoText() {
        #expect(NotchContent.avIndicators.label == "Camera and mic")
        #expect(NotchContent.avIndicators.text(at: .strip, meetings: [], meters: "5h 7%", message: "hi", now: Date()) == nil)
        #expect(NotchContent.resolve(.left, raw: "avIndicators") == .avIndicators)
    }
}

@Suite("AV indicators: sizes")
struct AVIndicatorLayoutTests {
    let size: CGFloat = 16

    @Test func widthFollowsWhatShows() {
        #expect(AVIndicatorLayout.contentWidth(AVIndicators(), size: size) == 0)
        let dot = AVIndicatorLayout.dot(size), mic = AVIndicatorLayout.mic(size)
        #expect(AVIndicatorLayout.contentWidth(AVIndicators(camera: true), size: size) == dot)
        #expect(AVIndicatorLayout.contentWidth(AVIndicators(mic: true), size: size) == mic)
        #expect(AVIndicatorLayout.contentWidth(AVIndicators(camera: true, mic: true), size: size)
                == dot + mic + AVIndicatorLayout.gap(size))
    }

    @Test func besideRoomIsZeroForNothingAndAddsTheSpacing() {
        #expect(AVIndicatorLayout.besideRoom(AVIndicators(), size: size) == 0)
        let one = AVIndicatorLayout.besideRoom(AVIndicators(camera: true), size: size)
        #expect(one == AVIndicatorLayout.dot(size) + AVIndicatorLayout.spacing)
        #expect(AVIndicatorLayout.maxRoom(size) >= one)
        // Small: it never takes a wing's room, and the island's window allows for it.
        #expect(AVIndicatorLayout.maxRoom(size) < 40)
    }

    @Test func tabSitsAgainstTheNotchOrAtTheTopCenter() {
        let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
        let notch = CGRect(x: 662, y: 0, width: 188, height: 32)
        let right = AVIndicatorLayout.badgeFrame(screen: screen, notch: notch, barHeight: 32, side: .right, width: 40)
        #expect(right == CGRect(x: 850, y: 950, width: 40, height: 32))
        let left = AVIndicatorLayout.badgeFrame(screen: screen, notch: notch, barHeight: 32, side: .left, width: 40)
        #expect(left == CGRect(x: 622, y: 950, width: 40, height: 32))
        let external = CGRect(x: 1512, y: 0, width: 2560, height: 1440)
        let top = AVIndicatorLayout.badgeFrame(screen: external, notch: nil, barHeight: 25, side: .left, width: 40)
        #expect(top == CGRect(x: 1512 + 1280 - 20, y: 1415, width: 40, height: 25))
        #expect(AVIndicatorLayout.badgeWidth(AVIndicators(camera: true), size: size)
                == AVIndicatorLayout.dot(size) + AVIndicatorLayout.badgePadding * 2)
    }
}

@Suite("AV indicators: the menu")
struct AVIndicatorMenuTests {
    @Test func readOnlyLinesThenSettings() {
        let both = AVIndicatorMenu.items(AVIndicators(camera: true, mic: true))
        #expect(both.map(\.title) == ["Camera in use", "Microphone in use", "Indicator Settings…"])
        #expect(both.map(\.enabled) == [false, false, true])
        #expect(both.last?.separatorBefore == true)
        #expect(AVIndicatorMenu.items(AVIndicators(mic: true)).map(\.title) == ["Microphone in use", "Indicator Settings…"])
        #expect(AVIndicatorMenu.items(AVIndicators()).first?.title == "Camera and microphone not in use")
    }

    @Test func nothingMutesOrChangesADevice() {
        let titles = AVIndicatorMenu.items(AVIndicators(camera: true, mic: true)).map { $0.title.lowercased() }
        for word in ["mute", "unmute", "turn off", "stop", "switch", "device"] {
            #expect(!titles.contains { $0.contains(word) })
        }
    }

    @Test func theNSMenuMatches() {
        let menu = AVIndicatorMenu.menu(AVIndicators(camera: true, mic: true))
        let rows = menu.items.filter { !$0.isSeparatorItem }
        #expect(rows.map(\.title) == ["Camera in use", "Microphone in use", "Indicator Settings…"])
        #expect(rows.map(\.isEnabled) == [false, false, true])
        #expect(rows[0].action == nil && rows[1].action == nil)
        #expect(menu.items.filter(\.isSeparatorItem).count == 1)
    }
}

@Suite("AV indicators: the strip's click area")
struct AVIndicatorHitTests {
    let frame = CGRect(x: 700, y: 34, width: 40, height: 26)

    func input(_ shows: Bool) -> DeskElements.Input {
        var i = DeskElements.Input()
        i.avStrip = shows
        i.stripAVFrame = frame
        return i
    }

    @Test func drawnInTheStripItTakesTheClick() {
        let elements = DeskElements.build(input(true))
        #expect(elements == [DeskElement(kind: .avIndicators, key: "strip", frame: frame)])
        let hit = DeskHitTest.element(at: CGPoint(x: 720, y: 47), in: elements)
        #expect(hit?.kind == .avIndicators)
        #expect(DeskHitTest.hasMenu(hit))
        // The slack still counts, past it nothing does.
        #expect(DeskHitTest.element(at: CGPoint(x: frame.maxX + 7, y: 47), in: elements)?.kind == .avIndicators)
        #expect(DeskHitTest.element(at: CGPoint(x: frame.maxX + 9, y: 47), in: elements) == nil)
        #expect(DeskFrameCheck.problem(elements, window: CGSize(width: 1512, height: 982)) == nil)
    }

    @Test func notDrawnNoElement() {
        #expect(DeskElements.build(input(false)).isEmpty)
        #expect(DeskElement.Kind.avIndicators.rawValue == "av_indicators")
        #expect(DeskHitTest.priority.contains(.avIndicators))
    }
}

@Suite("Mic monitor: following the default input")
struct MicWatchTests {
    @Test func followsTheDefaultDevice() {
        var w = MicWatch()
        #expect(w.follow(42) == MicWatch.Change(unwatch: nil, watch: 42))
        #expect(w.device == 42)
        // The same device again: nothing to do.
        #expect(w.follow(42) == MicWatch.Change())
        // A headset connects: the old one goes, the new one is watched.
        #expect(w.follow(77) == MicWatch.Change(unwatch: 42, watch: 77))
        // No input device left.
        #expect(w.follow(nil) == MicWatch.Change(unwatch: 77, watch: nil))
        #expect(w.follow(AudioObjectID(kAudioObjectUnknown)) == MicWatch.Change())
        #expect(w.device == nil)
    }

    @Test func resetHandsBackTheWatchedDevice() {
        var w = MicWatch()
        _ = w.follow(5)
        #expect(w.reset() == 5)
        #expect(w.reset() == nil)
    }

    @Test func eventsFeedTheSameSettleAsCameras() {
        let t0 = Date(timeIntervalSince1970: 1_000)
        var w = MicWatch()
        var a = CameraActivity()
        _ = w.follow(42)
        a.reduce(w.event(running: true), now: t0)
        #expect(a.inUse)
        // Moving to an idle headset turns it off only after the settle, like a camera stopping.
        _ = w.follow(77)
        let wait = a.reduce(w.event(running: false), now: t0.addingTimeInterval(1))
        #expect(wait == CameraActivity.offDelay)
        #expect(a.inUse)
        a.reduce(.settle, now: t0.addingTimeInterval(1 + CameraActivity.offDelay))
        #expect(!a.inUse)
        // No device: nothing runs.
        _ = w.follow(nil)
        #expect(w.event(running: true) == .devices([:]))
    }
}

@Suite("AV indicators: debug action")
struct AVIndicatorDebugTests {
    @Test func avTestParses() {
        guard case .success(.avTest(.camera, on: true)) = DebugLink.action("av-test", arg: "camera on") else {
            Issue.record("av-test camera on should parse"); return
        }
        guard case .success(.avTest(.mic, on: false)) = DebugLink.action("av-test", arg: "MIC-off") else {
            Issue.record("av-test MIC-off should parse"); return
        }
        for bad in [nil, "camera", "mic maybe", "speaker on", "camera on now"] {
            guard case .failure = DebugLink.action("av-test", arg: bad) else {
                Issue.record("av-test \(bad ?? "nil") should fail"); return
            }
        }
        #expect(DebugAction.names.contains("av-test"))
    }

    @Test func stateYAMLHoldsBooleansAndAPlaceOnly() {
        var s = DebugStateInput()
        s.avIndicators = AVIndicatorsDebug(camera: true, mic: false, shown: "beside_right")
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("av_indicators:\n  camera: true\n  mic: false\n  shown: beside_right"))
    }
}

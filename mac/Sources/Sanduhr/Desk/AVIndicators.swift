import Foundation
import CoreGraphics

/// The camera and mic indicators (item 67): a red recording dot while a camera is in use and a
/// mic glyph while the microphone is, on the notch. Read-only: Sanduhr never mutes, never changes
/// a device, and learns only that a camera or the default microphone is in use, never which app
/// or anything captured. Nothing is logged or saved. Pure, so the decisions test on their own.
///
///   defaults write com.626labs.sanduhr.desk avCameraDotMode -string always   (the red dot: never,
///                                                                            hiddenLight or always; never by default)
///   defaults write com.626labs.sanduhr.desk avMicGlyph -bool true      (the mic glyph; off by default)
///   defaults write com.626labs.sanduhr.desk avPulse -bool false        (the dot's gentle pulse; on)
///   defaults write com.626labs.sanduhr.desk avSide -string left        (beside the camera: left or right)
struct AVIndicators: Equatable {
    var camera = false
    var mic = false

    static let micKey = "avMicGlyph"
    static let pulseKey = "avPulse"

    var any: Bool { camera || mic }

    /// What shows: the dot when its mode wants it (AVCameraDotMode.shows), the mic only while its
    /// switch is on.
    static func shown(cameraInUse: Bool, micInUse: Bool, cameraSwitch: Bool, micSwitch: Bool) -> AVIndicators {
        AVIndicators(camera: cameraSwitch && cameraInUse, mic: micSwitch && micInUse)
    }

    /// VoiceOver's words for what shows: "Camera in use, microphone in use".
    var spoken: String {
        var parts: [String] = []
        if camera { parts.append(AVIndicatorMenu.cameraLine) }
        if mic { parts.append(camera ? "microphone in use" : AVIndicatorMenu.micLine) }
        return parts.isEmpty ? "Camera and microphone not in use" : parts.joined(separator: ", ")
    }
}

/// When the red dot shows (Settings, Desk, Notch, "Show the red dot"). A MacBook's built-in camera
/// has its own green light that can't be turned off, so a dot for it only repeats that light:
/// `hiddenLight` shows it only for a camera whose light the person can't see
/// (CameraLightVisibility: not built in, or built in with the lid closed).
enum AVCameraDotMode: String, CaseIterable, Identifiable {
    case never, hiddenLight, always

    static let key = "avCameraDotMode"
    /// The switch before the picker (`-bool`): on becomes `hiddenLight`.
    static let legacyKey = "avCameraDot"

    var id: String { rawValue }

    var label: String {
        switch self {
        case .never: "Never"
        case .hiddenLight: "For cameras without a visible light"
        case .always: "Always"
        }
    }

    /// The saved mode; unset falls back to the old switch (on: `hiddenLight`), else never.
    static func resolve(raw: String?, legacy: Bool?) -> AVCameraDotMode {
        if let mode = raw.flatMap(AVCameraDotMode.init(rawValue:)) { return mode }
        return legacy == true ? .hiddenLight : .never
    }

    static func saved(in defaults: UserDefaults) -> AVCameraDotMode {
        resolve(raw: defaults.string(forKey: key), legacy: defaults.object(forKey: legacyKey) as? Bool)
    }

    /// Writes the old switch's choice as a mode once, and removes the old key.
    static func migrate(_ defaults: UserDefaults) {
        guard let legacy = defaults.object(forKey: legacyKey) as? Bool else { return }
        if defaults.string(forKey: key) == nil {
            defaults.set(resolve(raw: nil, legacy: legacy).rawValue, forKey: key)
        }
        defaults.removeObject(forKey: legacyKey)
    }

    /// Whether the camera monitor needs to run.
    var watches: Bool { self != .never }

    /// Whether the dot shows for a camera in use.
    func shows(inUse: Bool, withoutVisibleLight: Bool) -> Bool {
        switch self {
        case .never: false
        case .hiddenLight: inUse && withoutVisibleLight
        case .always: inUse
        }
    }
}

/// Which side of the camera the indicators sit on, on the island (Settings, Desk, Notch).
enum AVIndicatorSide: String, CaseIterable, Identifiable {
    case left, right

    static let key = "avSide"
    var id: String { rawValue }

    var label: String {
        switch self {
        case .left: "Left of the camera"
        case .right: "Right of the camera"
        }
    }

    /// Unset or unknown means right, beside the menu bar's own status icons.
    static func resolve(raw: String?) -> AVIndicatorSide {
        raw.flatMap(AVIndicatorSide.init(rawValue:)) ?? .right
    }

    static func saved(in defaults: UserDefaults) -> AVIndicatorSide { resolve(raw: defaults.string(forKey: key)) }
}

/// Where the indicators draw now.
enum AVIndicatorSpot: Equatable {
    /// Nothing to show (switches off, or nothing in use).
    case none
    /// On the island, beside the camera cutout: the island grows on that side by their room, so
    /// the wings keep theirs.
    case beside(AVIndicatorSide)
    /// In the wings or the strip set to Camera and mic (NotchContent.avIndicators).
    case places
    /// Their own small black tab at the top: beside the notch while the island is off, or at the
    /// top center on a screen without a notch, like the camera light.
    case badge

    /// state.yaml's `av_indicators.shown`.
    var name: String {
        switch self {
        case .none: "none"
        case .beside(let side): "beside_\(side.rawValue)"
        case .places: "places"
        case .badge: "badge"
        }
    }
}

enum AVIndicatorPlacement {
    /// The notch places set to Camera and mic that can draw them: a wing while the wings show
    /// text, the strip while it has height and shows text.
    static func chosen(left: NotchContent, right: NotchContent, strip: NotchContent,
                       wingText: Bool, chinText: Bool, chin: Double) -> [NotchContent.Place] {
        var out: [NotchContent.Place] = []
        if wingText, left == .avIndicators { out.append(.left) }
        if wingText, right == .avIndicators { out.append(.right) }
        if chinText, chin > 0, strip == .avIndicators { out.append(.strip) }
        return out
    }

    /// Where they draw: nowhere with nothing to show; on the island while it is up (in a chosen
    /// place when there is one, else beside the camera); else in their own tab.
    static func spot(_ shown: AVIndicators, islandUp: Bool, side: AVIndicatorSide,
                     chosen: [NotchContent.Place]) -> AVIndicatorSpot {
        guard shown.any else { return .none }
        guard islandUp else { return .badge }
        return chosen.isEmpty ? .beside(side) : .places
    }

    /// `spot` with the places and the side as saved in `desk`: what the controller and Settings'
    /// Notch preview (item 68) both ask.
    static func spot(_ shown: AVIndicators, islandUp: Bool, in desk: UserDefaults) -> AVIndicatorSpot {
        let places = chosen(
            left: NotchContent.saved(.left, in: desk), right: NotchContent.saved(.right, in: desk),
            strip: NotchContent.saved(.strip, in: desk),
            wingText: desk.object(forKey: "notchText") as? Bool ?? true,
            chinText: desk.bool(forKey: "notchChinText"),
            chin: desk.object(forKey: "notchChin") as? Double ?? 26)
        return spot(shown, islandUp: islandUp, side: .saved(in: desk), chosen: places)
    }
}

/// The indicators' sizes, from the notch text size (`size`, as the wings use it).
enum AVIndicatorLayout {
    /// The red dot's diameter.
    static func dot(_ size: CGFloat) -> CGFloat { (size * 0.55).rounded() }
    /// The mic glyph's box width.
    static func mic(_ size: CGFloat) -> CGFloat { ceil(size * 0.75) }
    /// Between the dot and the mic.
    static func gap(_ size: CGFloat) -> CGFloat { (size * 0.4).rounded() }
    /// Between the indicators and a wing's text, beside the camera.
    static let spacing: CGFloat = 6
    /// The tab's padding on each side (AVIndicatorSpot.badge).
    static let badgePadding: CGFloat = 10

    /// The width of what shows, 0 for nothing.
    static func contentWidth(_ shown: AVIndicators, size: CGFloat) -> CGFloat {
        (shown.camera ? dot(size) : 0) + (shown.mic ? mic(size) : 0) + (shown.camera && shown.mic ? gap(size) : 0)
    }

    /// The room the island grows by beside the camera: the content and the spacing to the wing.
    static func besideRoom(_ shown: AVIndicators, size: CGFloat) -> CGFloat {
        let content = contentWidth(shown, size: size)
        return content > 0 ? content + spacing : 0
    }

    /// The widest the beside room gets, so the wings' window never needs resizing.
    static func maxRoom(_ size: CGFloat) -> CGFloat {
        besideRoom(AVIndicators(camera: true, mic: true), size: size)
    }

    /// The tab's frame in AppKit screen coordinates (bottom-left origin), flush with the top:
    /// against the notch on `side`, or centered on a screen without one. `notch` is in points from
    /// the screen's top-left corner, as NSScreen.cameraNotch gives it.
    static func badgeFrame(screen: CGRect, notch: CGRect?, barHeight: CGFloat, side: AVIndicatorSide,
                           width: CGFloat) -> CGRect {
        let height = max(barHeight, notch?.height ?? 0)
        let x: CGFloat
        if let notch {
            x = side == .left ? screen.minX + notch.minX - width : screen.minX + notch.maxX
        } else {
            x = screen.midX - width / 2
        }
        return CGRect(x: x, y: screen.maxY - height, width: width, height: height)
    }

    /// The tab's width for what shows.
    static func badgeWidth(_ shown: AVIndicators, size: CGFloat) -> CGFloat {
        contentWidth(shown, size: size) + badgePadding * 2
    }
}

/// The indicators' click menu: read-only lines naming what Sanduhr can see, then Notch
/// Settings…. Nothing in it mutes or changes a device.
enum AVIndicatorMenu {
    static let cameraLine = "Camera in use"
    static let micLine = "Microphone in use"
    static let noneLine = "Camera and microphone not in use"
    static let settingsTitle = "Notch Settings…"

    struct Item: Equatable {
        var title: String
        var enabled: Bool
        var separatorBefore = false
    }

    static func items(_ shown: AVIndicators) -> [Item] {
        var out: [Item] = []
        if shown.camera { out.append(Item(title: cameraLine, enabled: false)) }
        if shown.mic { out.append(Item(title: micLine, enabled: false)) }
        if out.isEmpty { out.append(Item(title: noneLine, enabled: false)) }
        out.append(Item(title: settingsTitle, enabled: true, separatorBefore: true))
        return out
    }
}

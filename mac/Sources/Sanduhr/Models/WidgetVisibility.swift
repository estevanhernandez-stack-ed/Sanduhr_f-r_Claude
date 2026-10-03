import Foundation

/// When the widget shows on its own (Settings, General, Surfaces). Standard defaults, key
/// `widgetVisibility`; absent means `always`, so an updated install keeps today's behavior.
/// A brand-new install starts with `whileDeskOff` (DeskFirstRun). `panelHidden` still means
/// "hidden right now": this setting only decides what happens at launch, when Desk turns on or
/// off, and when sign-in finishes.
enum WidgetVisibility: String, CaseIterable, Identifiable {
    /// Never hidden by Sanduhr: it shows unless you hide it, and stays as you left it.
    case always
    /// Hidden whenever Desk is on, shown when Desk is off.
    case whileDeskOff
    /// Only when asked: a meter click, Tools, Show Widget.
    case onRequest

    static let key = "widgetVisibility"

    var id: String { rawValue }

    var label: String {
        switch self {
        case .always: "Always shown"
        case .whileDeskOff: "Hidden while Desk is on"
        case .onRequest: "Only when I open it"
        }
    }

    /// The saved choice, or `always` when none is saved (or it is not one this version knows).
    static func saved(in store: DefaultsStore = UserDefaults.standard) -> WidgetVisibility {
        (store.object(forKey: key) as? String).flatMap(WidgetVisibility.init(rawValue:)) ?? .always
    }
}

/// The moments the choice takes over again. Showing or hiding from a menu in between is a
/// one-off that lasts until the next of these.
enum WidgetVisibilityEvent: Equatable {
    case launch
    case deskChanged
    /// The first successful fetch after a launch without a session key: onboarding is done,
    /// so the widget follows the choice as it would have at launch.
    case signedIn
    /// The choice itself was changed in Settings.
    case choiceChanged
}

enum WidgetVisibilityRule {
    /// Whether the widget should show after `event`, or nil to leave it as it is (`always`
    /// never moves it; at launch that means `panelHidden` decides, as before). Without a
    /// session key the new choices always show it, so sign-in can run.
    static func shouldShow(setting: WidgetVisibility, deskOn: Bool, hasSessionKey: Bool,
                           event: WidgetVisibilityEvent) -> Bool? {
        switch setting {
        case .always: return nil
        case _ where !hasSessionKey: return true
        case .whileDeskOff: return !deskOn
        case .onRequest: return false
        }
    }

    /// The widget's state after `event`, given whether it shows now (a manual show or hide
    /// included).
    static func resolve(showing: Bool, setting: WidgetVisibility, deskOn: Bool,
                        hasSessionKey: Bool, event: WidgetVisibilityEvent) -> Bool {
        shouldShow(setting: setting, deskOn: deskOn, hasSessionKey: hasSessionKey, event: event) ?? showing
    }
}

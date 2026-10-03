import Foundation

/// Whether the debug hooks answer. Off unless `defaults write com.626labs.sanduhr debugHooks
/// -bool true` is set or Sanduhr was started with SANDUHR_DEBUG_HOOKS=1, so a shipped build
/// ignores every sanduhr://debug/... link.
enum DebugGate {
    static let defaultsKey = "debugHooks"
    static let environmentKey = "SANDUHR_DEBUG_HOOKS"

    static func isOn(defaultsFlag: Bool, environment: [String: String]) -> Bool {
        if defaultsFlag { return true }
        guard let value = environment[environmentKey]?.lowercased() else { return false }
        return ["1", "true", "yes"].contains(value)
    }
}

/// What a hook action does, checked and parsed from the link.
enum DebugAction: Equatable {
    case showWidget, hideWidget
    /// Open Settings at a section, or where it was left (nil).
    case settings(SettingsSection?)
    case closeSettings
    case refresh
    case testAlert
    case pulse(Tier)
    /// One of the Tools items: .deepWork, .pacing or .snake, done as the menus do it.
    case tool(MenuCommand)
    case desk(Bool)
    case notch(Bool)
    /// The camera light shown or hidden by hand, as Tools, Camera Light does.
    case cameraLight(Bool)

    static let names = ["show-widget", "hide-widget", "settings", "close-settings", "refresh",
                        "test-alert", "pulse", "tool", "desk", "notch", "camera-light"]
}

enum DebugCommand: Equatable {
    case snapshot(dir: String)
    case action(DebugAction, dir: String?)
}

/// A parsed sanduhr://debug/... link: the command, or why there is none. `dir` is kept either
/// way, so the error can be written where the caller is waiting.
struct DebugRequest: Equatable {
    var command: DebugCommand?
    var error: String?
    var dir: String?
}

enum DebugLink {
    /// sanduhr://debug/<command>?…
    static func isDebug(_ url: URL) -> Bool {
        url.scheme?.lowercased() == "sanduhr" && url.host?.lowercased() == "debug"
    }

    static func parse(_ url: URL) -> DebugRequest {
        let parts = URLComponents(url: url, resolvingAgainstBaseURL: false)
        var query: [String: String] = [:]
        for item in parts?.queryItems ?? [] where query[item.name] == nil {
            query[item.name] = item.value ?? ""
        }
        let dir = query["dir"].flatMap { $0.isEmpty ? nil : $0 }
        let name = url.path.split(separator: "/").first.map(String.init)?.lowercased() ?? ""
        var request = DebugRequest(dir: dir)
        switch name {
        case "snapshot":
            if let dir { request.command = .snapshot(dir: dir) } else { request.error = "snapshot needs dir=<path>" }
        case "action":
            switch action(query["name"] ?? "", arg: query["arg"].flatMap { $0.isEmpty ? nil : $0 }) {
            case .success(let a): request.command = .action(a, dir: dir)
            case .failure(let e): request.error = e.message
            }
        default:
            request.error = name.isEmpty ? "no debug command" : "unknown debug command: \(name)"
        }
        return request
    }

    struct Failure: Error { let message: String }

    static func action(_ name: String, arg: String?) -> Result<DebugAction, Failure> {
        func bad(_ why: String) -> Result<DebugAction, Failure> { .failure(Failure(message: why)) }
        func onOff(_ make: (Bool) -> DebugAction) -> Result<DebugAction, Failure> {
            switch arg?.lowercased() {
            case "on", "true", "1": return .success(make(true))
            case "off", "false", "0": return .success(make(false))
            default: return bad("\(name) needs arg=on or arg=off")
            }
        }
        switch name.lowercased() {
        case "show-widget": return .success(.showWidget)
        case "hide-widget": return .success(.hideWidget)
        case "settings":
            guard let arg else { return .success(.settings(nil)) }
            guard let section = section(arg) else {
                return bad("unknown settings section: \(arg) (one of \(SettingsSection.allCases.map(\.rawValue).joined(separator: ", ")))")
            }
            return .success(.settings(section))
        case "close-settings": return .success(.closeSettings)
        case "refresh": return .success(.refresh)
        case "test-alert": return .success(.testAlert)
        case "pulse":
            guard let arg else { return .success(.pulse(.fiveHour)) }
            guard let tier = Tier(rawValue: arg.lowercased()) else { return bad("unknown tier: \(arg)") }
            return .success(.pulse(tier))
        case "tool":
            switch arg?.lowercased() {
            case "deep-work": return .success(.tool(.deepWork))
            case "pacing": return .success(.tool(.pacing))
            case "snake": return .success(.tool(.snake))
            default: return bad("tool needs arg=deep-work, pacing or snake")
            }
        case "desk": return onOff(DebugAction.desk)
        case "notch": return onOff(DebugAction.notch)
        case "camera-light": return onOff(DebugAction.cameraLight)
        case "": return bad("action needs name=<action>")
        default: return bad("unknown action: \(name) (one of \(DebugAction.names.joined(separator: ", ")))")
        }
    }

    /// A section by its raw value, ignoring case and dashes ("desk-layout" is deskLayout).
    static func section(_ s: String) -> SettingsSection? {
        let key = s.lowercased().replacingOccurrences(of: "-", with: "")
        return SettingsSection.allCases.first { $0.rawValue.lowercased() == key }
    }
}

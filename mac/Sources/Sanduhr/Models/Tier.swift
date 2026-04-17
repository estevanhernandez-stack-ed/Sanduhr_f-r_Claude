import Foundation

/// The usage tiers the Claude API reports. Raw value is the JSON key; `label`
/// is what the widget shows. Order here is the display order.
/// Mirrors `TIER_LABELS` in sanduhr.py:43-52.
enum Tier: String, CaseIterable, Codable, Hashable {
    case fiveHour          = "five_hour"
    case sevenDay          = "seven_day"
    case sevenDaySonnet    = "seven_day_sonnet"
    case sevenDayOpus      = "seven_day_opus"
    case sevenDayCowork    = "seven_day_cowork"
    case sevenDayOmelette  = "seven_day_omelette"
    case sevenDayOauthApps = "seven_day_oauth_apps"
    case iguanaNecktie     = "iguana_necktie"

    var label: String {
        switch self {
        case .fiveHour:          return "Session (5hr)"
        case .sevenDay:          return "Weekly — All Models"
        case .sevenDaySonnet:    return "Weekly — Sonnet"
        case .sevenDayOpus:      return "Weekly — Opus"
        case .sevenDayCowork:    return "Weekly — Cowork"
        case .sevenDayOmelette:  return "Weekly — Routines"
        case .sevenDayOauthApps: return "Weekly — OAuth Apps"
        case .iguanaNecktie:     return "Weekly — Special"
        }
    }

    /// Total duration (seconds) between resets. Used for pace/burn math.
    /// sanduhr.py:188 — `5*3600 if tier_key == "five_hour" else 7*86400`.
    var totalSeconds: Double {
        self == .fiveHour ? 5 * 3600 : 7 * 86400
    }
}

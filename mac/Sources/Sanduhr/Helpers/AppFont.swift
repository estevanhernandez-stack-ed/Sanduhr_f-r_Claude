import SwiftUI
import AppKit

/// The widget's font choice. Empty family means the system font (SF Pro),
/// which is the default and what every theme was designed against.
///
/// `@Observable` so any view whose body calls `Font.app(...)` re-renders
/// when the family changes: Observation tracks the `family` read made
/// inside the static helper, no environment plumbing needed.
@Observable
final class FontSettings {
    static let shared = FontSettings()
    static let defaultsKey = "fontFamily"

    var family: String {
        didSet { UserDefaults.standard.set(family, forKey: Self.defaultsKey) }
    }

    private init() {
        family = UserDefaults.standard.string(forKey: Self.defaultsKey) ?? ""
    }

    /// Every font family installed for this user, for the Settings picker.
    static func installedFamilies() -> [String] {
        NSFontManager.shared.availableFontFamilies.sorted {
            $0.localizedCaseInsensitiveCompare($1) == .orderedAscending
        }
    }

    // Family name -> the PostScript name of its regular face. Cached because
    // Font.app runs on every render of every card.
    @ObservationIgnored private var faceCache: [String: String?] = [:]

    /// The face to hand `Font.custom`, or nil when the family is the system
    /// font or is no longer installed (a removed font falls back quietly).
    func faceName() -> String? {
        let fam = family
        guard !fam.isEmpty else { return nil }
        if let hit = faceCache[fam] { return hit }
        var face: String?
        if let members = NSFontManager.shared.availableMembers(ofFontFamily: fam) {
            // Each member is [postScriptName, styleName, weight, traits].
            let regular = members.first { ($0[1] as? String)?.lowercased() == "regular" }
            face = (regular ?? members.first)?.first as? String
        }
        faceCache[fam] = face
        return face
    }
}

extension Font {
    /// Drop-in for `.system(size:weight:design:)` that honors the user's
    /// font choice. Monospaced text (theme JSON, Matrix digits) stays on the
    /// system font so it keeps its alignment.
    static func app(size: CGFloat,
                    weight: Font.Weight = .regular,
                    design: Font.Design = .default) -> Font {
        if design != .monospaced, let face = FontSettings.shared.faceName() {
            return Font.custom(face, size: size).weight(weight)
        }
        return .system(size: size, weight: weight, design: design)
    }
}

/// Subtle mode: the panel's glass, card fills, borders and theme strip disappear, leaving the
/// numbers and bars over the desktop with a soft text shadow. Same Observation trick as
/// FontSettings: views that read `Chrome.opacity` re-render when it flips.
@Observable
final class DisplaySettings {
    static let shared = DisplaySettings()
    static let defaultsKey = "subtleMode"

    var subtle: Bool {
        didSet { UserDefaults.standard.set(subtle, forKey: Self.defaultsKey) }
    }

    private init() {
        subtle = UserDefaults.standard.bool(forKey: Self.defaultsKey)
    }
}

enum Chrome {
    /// 1 normally, 0 in subtle mode. Applied to backgrounds and borders only, never to text.
    static var opacity: Double { DisplaySettings.shared.subtle ? 0 : 1 }
}

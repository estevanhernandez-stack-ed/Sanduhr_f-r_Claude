import Foundation
import SwiftUI

/// Loads user-authored theme JSON files from
/// ~/Library/Application Support/Sanduhr/themes/ and registers them with
/// `ThemeRegistry` so they appear in the theme strip alongside the
/// built-ins. Call once at app bootstrap before anything reads the
/// registry.
///
/// File schema mirrors windows/src/sanduhr/themes.py (same snake_case
/// keys, same glass-tuning dials). File stem becomes the theme id:
/// `sunset.json` → id "sunset". Missing required fields → file skipped,
/// warning logged.
enum UserThemes {
    static func load() {
        let dir = appSupportThemesDir()
        try? FileManager.default.createDirectory(
            at: dir, withIntermediateDirectories: true)

        guard let files = try? FileManager.default.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: nil)
        else { return }

        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .convertFromSnakeCase

        for file in files
            .filter({ $0.pathExtension.lowercased() == "json" })
            .sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {

            let id = file.deletingPathExtension().lastPathComponent.lowercased()
            if id.isEmpty { continue }
            guard let data = try? Data(contentsOf: file) else {
                NSLog("[Sanduhr] Could not read user theme: \(file.lastPathComponent)")
                continue
            }
            do {
                let dto = try decoder.decode(ThemeDTO.self, from: data)
                ThemeRegistry.register(dto.toTheme(id: id))
            } catch {
                NSLog("[Sanduhr] Invalid user theme \(file.lastPathComponent): \(error)")
            }
        }
    }

    static func appSupportThemesDir() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory,
                                             in: .userDomainMask).first!
        return base.appendingPathComponent("Sanduhr/themes", isDirectory: true)
    }
}

// MARK: - JSON DTO

/// Match docs/themes/template.json exactly (snake_case auto-converted).
/// Optional glass-tuning dials default to values from
/// windows/src/sanduhr/themes.py::_DEFAULT_GLASS_TUNING.
private struct ThemeDTO: Decodable {
    // Required colors.
    let name: String
    let bg, glass, titleBg, border, footerBg, barBg: String
    let text, textSecondary, textDim, textMuted: String
    let accent, paceMarker, sparkline: String

    // Optional colors.
    let glassOnMica: String?

    // Optional glass-tuning dials.
    let glassAlpha: Double?
    let borderAlpha: Double?
    let borderTint: String?
    let accentBloom: AccentBloomDTO?
    let innerHighlight: InnerHighlightDTO?
    let cardCornerRadius: Double?

    // Optional Matrix-style overrides.
    let optsOutOfMica: Bool?
    let monospaceFont: String?

    // Optional v2.0.4 dials.
    let ghostAlpha: Double?
    let breathPeriodMs: Int?

    struct AccentBloomDTO: Decodable {
        let blur: Double
        let alpha: Double
    }
    struct InnerHighlightDTO: Decodable {
        let color: String
        let alpha: Double
    }

    func toTheme(id: String) -> Theme {
        let optsOut = optsOutOfMica ?? false
        return Theme(
            id: id, displayName: name,
            palette: .init(
                bg: .hex(bg), glass: .hex(glass),
                titleBg: .hex(titleBg), border: .hex(border),
                text: .hex(text), textSecondary: .hex(textSecondary),
                textDim: .hex(textDim), textMuted: .hex(textMuted),
                accent: .hex(accent), barBg: .hex(barBg),
                footerBg: .hex(footerBg), paceMarker: .hex(paceMarker),
                sparkline: .hex(sparkline),
                glassOnMica: .hex(glassOnMica ?? glass),
                overlayOpacity: optsOut ? 0.82 : 0.18,
                numericFontDesign: monospaceFont != nil ? .monospaced : .rounded,
                glassAlpha: glassAlpha ?? 0.80,
                borderAlpha: borderAlpha ?? 0.40,
                borderTint: borderTint.map { .hex($0) },
                accentBloom: accentBloom.map {
                    .init(blur: CGFloat($0.blur), alpha: $0.alpha)
                } ?? .init(blur: 4, alpha: 0.45),
                innerHighlight: innerHighlight.map {
                    .init(color: .hex($0.color), alpha: $0.alpha)
                },
                cardCornerRadius: cardCornerRadius.map { CGFloat($0) } ?? 10,
                ghostAlpha: ghostAlpha ?? 1.0,
                breathPeriodMs: breathPeriodMs ?? 2800))
    }
}

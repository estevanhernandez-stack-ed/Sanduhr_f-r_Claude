import SwiftUI
import AppKit

/// The notch, extended. On a Mac with a camera notch, Desk draws pure black that continues the
/// cutout a little wider and a little lower, with soft rounded corners, so it reads as one
/// bigger island. Inside the extra strip under the hardware notch it prints one short line:
/// the next meeting when one starts within the hour, otherwise the Claude meters.
/// Nothing in the menu bar lives there (the menu bar never draws behind the notch, and Ice's
/// split bar leaves the middle clear), so it is free space.
///
///   defaults write com.626labs.sanduhr.desk notch -bool true         (turn it on; off by default)
///   defaults write com.626labs.sanduhr.desk notchWings -float 36     (extra width on each side)
///   defaults write com.626labs.sanduhr.desk notchChin -float 26      (extra height below the notch; 0 = none)
/// On a screen without a notch (an external display) nothing is drawn.
struct NotchView: View {
    var model: DeskModel

    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchChin", store: .desk) private var chin = 26.0
    @AppStorage("font", store: .desk) private var font = ""
    @AppStorage("notchChinText", store: .desk) private var showChinText = false
    @AppStorage("notchTextColor", store: .desk) private var textColor = "ffffff"
    @AppStorage("notchText", store: .desk) private var wingText = true

    var body: some View {
        // Extra height 0 means no strip under the camera at all: just the wings.
        if enabled, chin > 0, let notch = model.notchRect {
            let height = notch.height + chin
            TimelineView(.periodic(from: .now, by: 15)) { context in
                // Same widths as the wings above, so the strip and the wings stay one shape
                // even when a wing grows to fit its text.
                let w = NotchWingsView.layout(model: model, now: context.date, wings: wings,
                                              showText: wingText, font: font, notchHeight: notch.height)
                ZStack(alignment: .bottom) {
                    IslandShape(flare: 8, radius: min(16, chin * 0.7))
                        .fill(Color.black)
                    if showChinText, let line = line(now: context.date) {
                        Text(line)
                            .font(.custom(font, size: max(11, chin * 0.55)))
                            .lineLimit(1)
                            .truncationMode(.tail)
                            .minimumScaleFactor(0.7)
                            .foregroundStyle(LinearGradient.ink(textColor))
                            .opacity(0.85)
                            .padding(.horizontal, 18)
                            .frame(height: chin)
                    }
                }
                .frame(width: notch.width + w.left + w.right, height: height)
                .offset(x: (w.right - w.left) / 2)
            }
            .position(x: notch.midX, y: height / 2)
            .allowsHitTesting(false)
        }
    }

    /// Next meeting within the hour beats the Claude line; with neither, the island stays empty.
    private func line(now: Date) -> String? {
        if let next = model.meetings.first(where: { $0.end > now }) {
            let mins = Int(next.start.timeIntervalSince(now) / 60)
            if next.start <= now { return "now  \(next.title)" }
            if mins < 60 { return "\(next.title) in \(max(1, mins))m" }
        }
        if let compact = model.claudeCompact { return compact }
        return nil
    }
}

/// A rectangle hanging from the top edge with rounded bottom corners and small outward flares
/// at the top, the same silhouette as the hardware notch, so the two blend.
struct IslandShape: Shape {
    let flare: CGFloat
    let radius: CGFloat

    func path(in r: CGRect) -> Path {
        var p = Path()
        p.move(to: CGPoint(x: r.minX - flare, y: r.minY))
        p.addQuadCurve(to: CGPoint(x: r.minX, y: r.minY + flare), control: CGPoint(x: r.minX, y: r.minY))
        p.addLine(to: CGPoint(x: r.minX, y: r.maxY - radius))
        p.addQuadCurve(to: CGPoint(x: r.minX + radius, y: r.maxY), control: CGPoint(x: r.minX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.maxX - radius, y: r.maxY))
        p.addQuadCurve(to: CGPoint(x: r.maxX, y: r.maxY - radius), control: CGPoint(x: r.maxX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.maxX, y: r.minY + flare))
        p.addQuadCurve(to: CGPoint(x: r.maxX + flare, y: r.minY), control: CGPoint(x: r.maxX, y: r.minY))
        p.closeSubpath()
        return p
    }
}

/// The wings: the part of the island level with the menu bar, beside the hardware notch.
/// The desktop window sits below the menu bar layer, so this lives in its own small window
/// above the menu bar instead, where it always shows, like the notch itself. It is only
/// menu-bar tall, so it never covers an app's content.
///
/// Text rides in the wings, since they are visible over every app:
///   left wing   the next meeting when one starts within the hour ("standup 12m", "now standup"),
///               otherwise the time (so the macOS clock can go analog or hide)
///   right wing  the Claude meters ("5h 7%  wk 63%") while Sanduhr's numbers are fresh
/// A wing grows past the Wings setting when its text needs the room.
/// Click the island to open Desk's settings.
struct NotchWingsView: View {
    var model: DeskModel
    let notchWidth: CGFloat
    let notchHeight: CGFloat
    /// How far down the black reaches: the notch or the menu bar, whichever is taller.
    let barHeight: CGFloat
    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchText", store: .desk) private var showText = true
    @AppStorage("notchTextColor", store: .desk) private var textColor = "ffffff"
    @AppStorage("font", store: .desk) private var font = ""

    var body: some View {
        if enabled {
            TimelineView(.periodic(from: .now, by: 15)) { context in
                let w = Self.layout(model: model, now: context.date, wings: wings,
                                    showText: showText, font: font, notchHeight: notchHeight)
                let left = w.leftText, right = w.rightText, size = w.size
                let wingL = w.left, wingR = w.right
                if wingL > 0 || wingR > 0 {
                    ZStack {
                        IslandShape(flare: 8, radius: min(10, barHeight * 0.3))
                            .fill(Color.black)
                        HStack(spacing: 0) {
                            label(left, size).frame(width: max(0, wingL - 10), alignment: .trailing)
                            Color.clear.frame(width: notchWidth + 20)
                            label(right, size).frame(width: max(0, wingR - 10), alignment: .leading)
                        }
                    }
                    .frame(width: notchWidth + wingL + wingR, height: barHeight)
                    .offset(x: (wingR - wingL) / 2)
                    .contentShape(Rectangle())
                    .onTapGesture { DeskController.shared.showSettings() }
                    .help("Sanduhr Settings")
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                }
            }
        }
    }

    private func label(_ text: String?, _ size: CGFloat) -> some View {
        Text(text ?? "")
            .font(.custom(font, size: size))
            .foregroundStyle(LinearGradient.ink(textColor))
            .opacity(0.88)
            .lineLimit(1)
            .truncationMode(.tail)
    }

    /// Wing widths and texts, shared with the strip under the notch so both draw one shape.
    struct Layout { let left: CGFloat; let right: CGFloat; let leftText: String?; let rightText: String?; let size: CGFloat }

    static func layout(model: DeskModel, now: Date, wings: Double, showText: Bool,
                       font: String, notchHeight: CGFloat) -> Layout {
        let left = showText ? leftText(model: model, now: now) : nil
        let right = showText ? model.claudeCompact : nil
        let size = max(10, notchHeight * 0.42)
        return Layout(left: min(maxWings, max(wings, width(left, size, font) + 22)),
                      right: min(maxWings, max(wings, width(right, size, font) + 22)),
                      leftText: left, rightText: right, size: size)
    }

    private static func leftText(model: DeskModel, now: Date) -> String {
        if let next = model.meetings.first(where: { $0.end > now }) {
            let title = next.title.count > 18 ? String(next.title.prefix(17)) + "…" : next.title
            if next.start <= now { return "now \(title)" }
            let mins = Int(next.start.timeIntervalSince(now) / 60)
            if mins < 60 { return "\(title) \(max(1, mins))m" }
        }
        let f = DateFormatter()
        f.dateFormat = "h:mm"
        return f.string(from: now)
    }

    private static func width(_ text: String?, _ size: CGFloat, _ font: String) -> CGFloat {
        guard let text, !text.isEmpty else { return 0 }
        // The setting holds a family name; measure with that family's regular face.
        let nsFont = NSFontManager.shared.font(withFamily: font, traits: [], weight: 5, size: size)
            ?? NSFont(name: font, size: size) ?? NSFont.systemFont(ofSize: size)
        return ceil((text as NSString).size(withAttributes: [.font: nsFont]).width)
    }

    /// Widest a wing can get, so the window never needs resizing.
    static let maxWings: CGFloat = 180
}

import SwiftUI
import AppKit

/// Settings, Desk, Layout's preview (item 68): a screen-shaped map with the menu bar, the Dock
/// on its edge and every placed piece outlined in its corner, in stacking order, inside the
/// margins. Its own view so the Dock clearance work (item 59) can extend it.
struct DeskLayoutMap: View {
    /// The screen's size in points (the Desk's screen).
    let screen: CGSize
    let menuBar: CGFloat
    let dock: DockPrefs
    /// The Dock's reach into the screen in points, 0 when it hides.
    let dockReach: CGFloat

    @AppStorage("layout", store: .desk) private var layout = DeskLayout.standard
    @AppStorage("left", store: .desk) private var left = 52.0
    @AppStorage("right", store: .desk) private var right = 52.0
    @AppStorage("top", store: .desk) private var top = 40.0
    @AppStorage("bottom", store: .desk) private var bottom = 60.0
    @AppStorage("showMeetings", store: .desk) private var showMeetings = true
    @AppStorage("showClaude", store: .desk) private var showClaude = true

    /// The map's height; its width follows the screen's shape.
    static let height: CGFloat = 132

    var body: some View {
        let scale = Self.height / max(1, screen.height)
        let width = screen.width * scale
        let corners = Self.corners(layout: layout, showMeetings: showMeetings, showClaude: showClaude)
        let insets = Self.insets(left: left, right: right, top: top + menuBar, bottom: bottom,
                                 dock: dock.side, reach: dockReach, scale: scale)
        ZStack(alignment: .topLeading) {
            RoundedRectangle(cornerRadius: 6).fill(Color.white.opacity(0.06))
            Rectangle().fill(Color.white.opacity(0.18)).frame(height: max(2, menuBar * scale))
            dockBand(width: width, scale: scale)
            HStack(alignment: .top, spacing: 0) {
                column(top: corners[.tl] ?? [], bottom: corners[.bl] ?? [], alignment: .leading)
                Spacer(minLength: 8)
                column(top: corners[.tr] ?? [], bottom: corners[.br] ?? [], alignment: .trailing)
            }
            .padding(insets)
        }
        .frame(width: width, height: Self.height)
        .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.white.opacity(0.5), lineWidth: 1))
    }

    private func column(top: [String], bottom: [String], alignment: HorizontalAlignment) -> some View {
        VStack(alignment: alignment, spacing: 3) {
            ForEach(top, id: \.self) { piece($0) }
            Spacer(minLength: 4)
            ForEach(bottom, id: \.self) { piece($0) }
        }
        .frame(maxHeight: .infinity)
    }

    private func piece(_ name: String) -> some View {
        Text(name)
            .font(.system(size: 9, weight: .medium))
            .foregroundStyle(.white.opacity(0.9))
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 5)
            .padding(.vertical, 2)
            .background(RoundedRectangle(cornerRadius: 3).strokeBorder(Color.white.opacity(0.7), lineWidth: 0.75))
    }

    @ViewBuilder
    private func dockBand(width: CGFloat, scale: CGFloat) -> some View {
        let thick = max(3, (dockReach > 0 ? dockReach : DockGeometry.estimatedThickness(tilesize: dock.tilesize)) * scale)
        let band = RoundedRectangle(cornerRadius: 2)
            .fill(Color.white.opacity(dockReach > 0 ? 0.32 : 0.14))
        switch dock.side {
        case .bottom:
            band.frame(width: width * 0.5, height: thick)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
        case .left:
            band.frame(width: thick, height: Self.height * 0.5)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        case .right:
            band.frame(width: thick, height: Self.height * 0.5)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .trailing)
        }
    }

    /// Each corner's pieces by name, in stacking order: what DeskView draws for `layout`
    /// (DeskLayout.placed and DeskLayout.parse, in DeskLayout.widgets' order).
    static func corners(layout: String, showMeetings: Bool, showClaude: Bool) -> [DeskView.Slot: [String]] {
        let placed = DeskLayout.placed(layout, showMeetings: showMeetings, showClaude: showClaude)
        let slots = DeskLayout.parse(layout)
        var out: [DeskView.Slot: [String]] = [:]
        for w in DeskLayout.widgets where placed.contains(w.key) {
            guard let raw = slots[w.key], let slot = DeskView.Slot(rawValue: raw) else { continue }
            out[slot, default: []].append(w.name)
        }
        return out
    }

    /// The margins on the map: the saved ones (top below the menu bar) and the Dock's reach on its
    /// side, as DeskView pads its columns, scaled to the map.
    static func insets(left: Double, right: Double, top: Double, bottom: Double,
                       dock: DockSide, reach: CGFloat, scale: CGFloat) -> EdgeInsets {
        let d = DockInsets.on(dock, reach)
        return EdgeInsets(top: CGFloat(top) * scale, leading: (CGFloat(left) + d.left) * scale,
                          bottom: (CGFloat(bottom) + d.bottom) * scale, trailing: (CGFloat(right) + d.right) * scale)
    }
}

/// The Layout pane's card: the map of the Desk's screen.
struct DeskLayoutPreview: View {
    var live: DeskModel

    var body: some View {
        let screen = NSScreen.main ?? NSScreen.screens.first
        let frame = screen?.frame.size ?? CGSize(width: 1512, height: 982)
        let menuBar = screen.map { $0.frame.maxY - $0.visibleFrame.maxY } ?? 24
        let prefs = DockFollower.readPrefs()
        let reach = prefs.autohide ? 0 : live.dockInsets.amount(on: prefs.side)
        SettingsPreviewCard(kind: .layout, label: "A map of the screen with each Desk piece in its corner, the menu bar at the top and the Dock on its edge.",
                            maximum: 1.2) {
            DeskLayoutMap(screen: frame, menuBar: menuBar, dock: prefs, dockReach: reach)
        }
    }
}

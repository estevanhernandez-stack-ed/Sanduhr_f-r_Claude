import SwiftUI
import AppKit

/// Settings, Desk, Layout's preview (items 68, 59): a screen-shaped map with the menu bar, the
/// notch (and the island under it while it draws), the Dock on its edge and every placed piece
/// outlined at its anchor, in stacking order and at its size, inside the margins. Laid out the
/// way DeskView lays out the Desk: the sides' columns with their middles centered, the centers'
/// column with the top center below the notch.
struct DeskLayoutMap: View {
    /// The screen's size in points (the Desk's screen).
    let screen: CGSize
    let menuBar: CGFloat
    let dock: DockPrefs
    /// The Dock's reach into the screen in points, 0 when it hides.
    let dockReach: CGFloat
    /// The camera notch in screen points from the top left, nil on a screen without one.
    var notch: CGRect? = nil

    @AppStorage("layout", store: .desk) private var layout = DeskLayout.standard
    @AppStorage("left", store: .desk) private var left = 52.0
    @AppStorage("right", store: .desk) private var right = 52.0
    @AppStorage("top", store: .desk) private var top = 40.0
    @AppStorage("bottom", store: .desk) private var bottom = 60.0
    @AppStorage("showMeetings", store: .desk) private var showMeetings = true
    @AppStorage(DeskController.notchKey, store: .desk) private var island = false
    @AppStorage("notchChin", store: .desk) private var chin = 26.0

    /// The map's height; its width follows the screen's shape.
    static let height: CGFloat = 132
    /// A piece's label size on the map at size 1.
    static let labelSize: CGFloat = 9

    /// One piece as the map draws it.
    struct Piece: Equatable {
        var name: String
        var scale: Double
    }

    var body: some View {
        let scale = Self.height / max(1, screen.height)
        let width = screen.width * scale
        let anchors = Self.anchors(layout: layout, showMeetings: showMeetings, showClaude: true)
        let insets = Self.insets(left: left, right: right, top: top + menuBar, bottom: bottom,
                                 dock: dock.side, reach: dockReach, scale: scale)
        let notchBottom = DeskAnchorGeometry.notchBottom(notch: notch, island: island, chin: chin)
        let drop = DeskAnchorGeometry.centerDrop(contentTop: CGFloat(top) + menuBar, notchBottom: notchBottom) * scale
        ZStack(alignment: .topLeading) {
            RoundedRectangle(cornerRadius: 6).fill(Color.white.opacity(0.06))
            Rectangle().fill(Color.white.opacity(0.18)).frame(height: max(2, menuBar * scale))
            notchShape(bottom: notchBottom, scale: scale)
            dockBand(width: width, scale: scale)
            HStack(alignment: .top, spacing: 0) {
                column(anchors, .left, alignment: .leading)
                Spacer(minLength: 8)
                column(anchors, .right, alignment: .trailing)
            }
            .overlay { centerColumn(anchors, drop: drop) }
            .padding(insets)
        }
        .frame(width: width, height: Self.height)
        .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.white.opacity(0.5), lineWidth: 1))
    }

    @ViewBuilder
    private func column(_ anchors: [DeskAnchor: [Piece]], _ side: DeskAnchor.Column,
                        alignment: HorizontalAlignment) -> some View {
        let middle = DeskAnchor.at(side, .middle).flatMap { anchors[$0] } ?? []
        VStack(alignment: alignment, spacing: 3) {
            stack(DeskAnchor.at(side, .top).flatMap { anchors[$0] } ?? [])
            Spacer(minLength: 4)
            stack(DeskAnchor.at(side, .bottom).flatMap { anchors[$0] } ?? [])
        }
        .frame(maxHeight: .infinity)
        .overlay(alignment: Alignment(horizontal: alignment, vertical: .center)) {
            if !middle.isEmpty {
                VStack(alignment: alignment, spacing: 3) { stack(middle) }.fixedSize()
            }
        }
    }

    @ViewBuilder
    private func centerColumn(_ anchors: [DeskAnchor: [Piece]], drop: CGFloat) -> some View {
        let top = anchors[.tc] ?? []
        let bottom = anchors[.bc] ?? []
        if !top.isEmpty || !bottom.isEmpty {
            VStack(spacing: 3) {
                stack(top)
                Spacer(minLength: 4)
                stack(bottom)
            }
            .padding(.top, drop)
        }
    }

    private func stack(_ pieces: [Piece]) -> some View {
        ForEach(pieces, id: \.name) { piece($0) }
    }

    private func piece(_ p: Piece) -> some View {
        Text(p.name)
            .font(.system(size: Self.labelSize * CGFloat(p.scale), weight: .medium))
            .foregroundStyle(.white.opacity(0.9))
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 5 * CGFloat(p.scale))
            .padding(.vertical, 2 * CGFloat(p.scale))
            .background(RoundedRectangle(cornerRadius: 3).strokeBorder(Color.white.opacity(0.7), lineWidth: 0.75))
    }

    /// The notch, and the island's strip under it while the island draws, at the top center.
    @ViewBuilder
    private func notchShape(bottom: CGFloat, scale: CGFloat) -> some View {
        if let notch, bottom > 0 {
            RoundedRectangle(cornerRadius: 3)
                .fill(Color.black)
                .frame(width: max(6, notch.width * scale), height: max(2, bottom * scale))
                .frame(maxWidth: .infinity, alignment: .top)
                .accessibilityHidden(true)
        }
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

    /// Each anchor's pieces by name and size, in stacking order: what DeskView draws for
    /// `layout` (DeskArrangement.stacks, the same call).
    static func anchors(layout: String, showMeetings: Bool, showClaude: Bool) -> [DeskAnchor: [Piece]] {
        let names = Dictionary(uniqueKeysWithValues: DeskLayout.widgets.map { ($0.key, $0.name) })
        return DeskArrangement(layout).stacks(showMeetings: showMeetings, showClaude: showClaude)
            .mapValues { stack in stack.map { Piece(name: names[$0.widget] ?? $0.widget, scale: $0.scale) } }
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
        SettingsPreviewCard(kind: .layout, label: "A map of the screen with each Desk piece at its place, in its order and at its size, the menu bar and the notch at the top and the Dock on its edge.",
                            maximum: 1.2) {
            DeskLayoutMap(screen: frame, menuBar: menuBar, dock: prefs, dockReach: reach, notch: live.notchRect)
        }
    }
}

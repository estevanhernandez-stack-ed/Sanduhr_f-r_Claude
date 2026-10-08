import SwiftUI
import AppKit

/// Where the anchors are on the Desk, in DeskArrange.space: the content rectangle the stacks
/// hang in (margins, the menu bar and the Dock's reach already taken off) and how far the top
/// center drops below the notch or the island. DeskView hands it down while arranging.
struct DeskArrangeGeometry: Equatable {
    var content: CGRect
    var centerDrop: CGFloat
}

extension DeskArrangeMode {
    /// The anchor a drag of `widget` at `point` lands on: the stack under the pointer (its own
    /// first), else the nearest anchor (DeskArrange.target). The drop and the lit anchor agree.
    func target(of widget: String, at point: CGPoint, geometry: DeskArrangeGeometry) -> DeskAnchor {
        let stacks = working.map { DeskArrange.stackBounds($0, frames: frames) } ?? [:]
        return DeskArrange.target(point: point, content: geometry.content, centerDrop: geometry.centerDrop,
                                  stacks: stacks, own: working?.placement(widget)?.anchor)
    }
}

private struct DeskArrangeGeometryKey: EnvironmentKey {
    static let defaultValue: DeskArrangeGeometry? = nil
}

extension EnvironmentValues {
    /// Set on the Desk's columns while arranging (item 60); nil otherwise.
    var deskArrangeGeometry: DeskArrangeGeometry? {
        get { self[DeskArrangeGeometryKey.self] }
        set { self[DeskArrangeGeometryKey.self] = newValue }
    }
}

extension View {
    /// A Desk piece in Arrange mode (item 60): its outline, its name and a resize handle, a drag
    /// to move or reorder it. Outside Arrange mode it changes nothing, and the piece keeps its
    /// identity across the switch (a message's {write} does not replay).
    func deskArrangeable(_ placement: DeskPlacement, mode: DeskArrangeMode) -> some View {
        modifier(DeskArrangeable(placement: placement, mode: mode))
    }
}

private struct DeskArrangeable: ViewModifier {
    let placement: DeskPlacement
    let mode: DeskArrangeMode

    @Environment(\.deskArrangeGeometry) private var geometry
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var resizeStart: ResizeStart?

    private struct ResizeStart {
        var scale: Double
        var size: CGSize
    }

    /// Room between a piece and its outline, in points.
    static let outlinePad: CGFloat = 6

    private var widget: String { placement.widget }
    private var name: String { DeskLayout.widgets.first { $0.key == widget }?.name ?? widget }

    func body(content: Content) -> some View {
        let active = mode.active
        let dragging = mode.drag?.widget == widget
        let corner = DeskArrange.handle(placement.anchor)
        content
            // A piece with nothing to draw (now playing while nothing plays) still has a box to grab.
            .frame(minWidth: active ? 60 : nil, minHeight: active ? 20 : nil)
            .background { if active { frameReporter } }
            .overlay { if active { outline } }
            .overlay(alignment: Alignment(horizontal: corner.trailing ? .trailing : .leading,
                                          vertical: corner.bottom ? .bottom : .top)) {
                if active { handle(corner) }
            }
            .opacity(active && dragging ? 0.35 : 1)
            .contentShape(Rectangle())
            .gesture(move, including: active ? .all : .subviews)
            .accessibilityActions {
                if active { arrangeActions }
            }
    }

    private var frameReporter: some View {
        GeometryReader { geo in
            Color.clear
                .onAppear { mode.frames[widget] = geo.frame(in: .named(DeskArrange.space)) }
                .onChange(of: geo.frame(in: .named(DeskArrange.space))) { _, f in mode.frames[widget] = f }
        }
    }

    private var outline: some View {
        RoundedRectangle(cornerRadius: 8)
            .strokeBorder(Color.white.opacity(0.85), style: StrokeStyle(lineWidth: 1.5, dash: [6, 4]))
            .shadow(color: .black.opacity(0.5), radius: 2)
            .padding(-Self.outlinePad)
            .overlay(alignment: .topLeading) {
                Text(name)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(Color.black.opacity(0.55)))
                    .fixedSize()
                    .offset(x: -Self.outlinePad, y: -Self.outlinePad - 20)
            }
            .allowsHitTesting(false)
    }

    private func handle(_ corner: (trailing: Bool, bottom: Bool)) -> some View {
        Circle()
            .fill(Color.white)
            .overlay(Circle().strokeBorder(Color.accentColor, lineWidth: 2))
            .frame(width: 12, height: 12)
            .shadow(color: .black.opacity(0.5), radius: 2)
            .frame(width: 24, height: 24)
            .contentShape(Rectangle())
            .offset(x: (corner.trailing ? 1 : -1) * (Self.outlinePad + 6),
                    y: (corner.bottom ? 1 : -1) * (Self.outlinePad + 6))
            .gesture(resize)
            .accessibilityHidden(true)
    }

    private var move: some Gesture {
        DragGesture(minimumDistance: 3, coordinateSpace: .named(DeskArrange.space))
            .onChanged { v in
                mode.drag = DeskArrangeDrag(widget: widget, location: v.location, translation: v.translation,
                                            frame: mode.drag?.frame ?? mode.frames[widget] ?? .zero)
            }
            .onEnded { v in drop(at: v.location) }
    }

    /// The drop: the stack under the pointer or else the nearest anchor, in front of the first
    /// piece there whose middle is below the pointer (so a drop within the piece's own stack
    /// reorders it, however tall the stack).
    private func drop(at location: CGPoint) {
        guard let geometry, let working = mode.working else {
            mode.drag = nil
            return
        }
        let anchor = mode.target(of: widget, at: location, geometry: geometry)
        let stack = working.stack(anchor)
            .filter { $0.widget != widget }
            .map { (widget: $0.widget, frame: mode.frames[$0.widget] ?? .zero) }
        let before = DeskArrange.pieceAfter(y: location.y, in: stack)
        withAnimation(DeskArrange.snapAnimation(reduceMotion: reduceMotion)) {
            mode.edit { $0.put(widget, at: anchor, before: before) }
            mode.drag = nil
        }
    }

    private var resize: some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .named(DeskArrange.space))
            .onChanged { v in
                let start = resizeStart ?? ResizeStart(scale: placement.scale, size: mode.frames[widget]?.size ?? .zero)
                if resizeStart == nil { resizeStart = start }
                let scale = DeskArrange.scale(from: start.scale, size: start.size, drag: v.translation, anchor: placement.anchor)
                if scale != placement.scale { mode.edit { $0.setScale(widget, scale) } }
            }
            .onEnded { _ in resizeStart = nil }
    }

    /// VoiceOver: the same edits without a drag.
    @ViewBuilder
    private var arrangeActions: some View {
        Button("Move Up") { mode.edit { $0.move(widget, by: -1) } }
        Button("Move Down") { mode.edit { $0.move(widget, by: 1) } }
        Button("Bigger") { mode.edit { $0.setScale(widget, placement.scale + DeskArrangement.scaleStep) } }
        Button("Smaller") { mode.edit { $0.setScale(widget, placement.scale - DeskArrangement.scaleStep) } }
    }
}

/// The full-screen plate under the pieces while arranging: a drawn pixel everywhere, so the
/// window server hands this transparent window every click on the screen (DeskPointerMenu), too
/// faint to see.
struct DeskArrangePlate: View {
    var body: some View {
        Color.black.opacity(DeskPointerMenu.hitPlateOpacity)
            .ignoresSafeArea()
            .accessibilityHidden(true)
    }
}

/// Over the pieces while arranging: the eight anchors, lit while a piece is dragged with the one
/// it would land on brightest, the dragged piece's ghost under the pointer, and the bar with
/// Cancel and Done in the open middle of the screen.
struct DeskArrangeOverlay: View {
    let mode: DeskArrangeMode
    let geometry: DeskArrangeGeometry

    var body: some View {
        ZStack(alignment: .topLeading) {
            if let drag = mode.drag {
                lights(drag)
                ghost(drag)
            }
            DeskArrangeBar()
                .fixedSize()
                .position(x: geometry.content.midX, y: geometry.content.midY)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func lights(_ drag: DeskArrangeDrag) -> some View {
        let target = mode.target(of: drag.widget, at: drag.location, geometry: geometry)
        return ForEach(DeskAnchor.allCases, id: \.self) { anchor in
            let lit = anchor == target
            Circle()
                .fill(lit ? Color.accentColor : Color.white.opacity(0.35))
                .overlay(Circle().strokeBorder(Color.white, lineWidth: lit ? 2 : 1))
                .frame(width: lit ? 22 : 14, height: lit ? 22 : 14)
                .shadow(color: .black.opacity(0.5), radius: 3)
                .position(DeskAnchorGeometry.point(anchor, in: geometry.content, centerDrop: geometry.centerDrop))
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    private func ghost(_ drag: DeskArrangeDrag) -> some View {
        let f = drag.frame
        return RoundedRectangle(cornerRadius: 8)
            .fill(Color.accentColor.opacity(0.18))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.white.opacity(0.9), lineWidth: 1.5))
            .frame(width: max(40, f.width + 12), height: max(20, f.height + 12))
            .position(x: f.midX + drag.translation.width, y: f.midY + drag.translation.height)
            .allowsHitTesting(false)
            .accessibilityHidden(true)
    }
}

/// Arrange mode's bar: what to do, Cancel and Done.
struct DeskArrangeBar: View {
    var body: some View {
        VStack(spacing: 8) {
            Text("Arrange Desk").font(.headline)
            Text("Drag a piece to any of the eight places, or up and down its stack. Drag its round handle to resize it.")
                .font(.callout)
                .multilineTextAlignment(.center)
                .frame(width: 300)
            Text("Escape or Done keeps the new layout. Cancel puts it back as it was.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(width: 300)
            HStack(spacing: 10) {
                Button("Cancel") { DeskController.shared.endArrange(keep: false) }
                Button("Done") { DeskController.shared.endArrange(keep: true) }
                    .buttonStyle(.borderedProminent)
            }
            .controlSize(.large)
            .padding(.top, 4)
        }
        .padding(18)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
        .environment(\.colorScheme, .dark)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Arrange Desk")
    }
}

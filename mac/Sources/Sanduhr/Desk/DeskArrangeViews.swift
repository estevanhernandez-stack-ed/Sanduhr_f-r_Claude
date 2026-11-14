import SwiftUI
import AppKit

/// Where the anchors are on the Desk, in DeskArrange.space: the content rectangle the stacks
/// hang in (margins, the menu bar and the Dock's reach already taken off) and how far the top
/// center drops below the notch or the island. DeskView hands it down while arranging.
struct DeskArrangeGeometry: Equatable {
    var content: CGRect
    var centerDrop: CGFloat
    /// The Dock's reach at the bottom of the window, so the Hidden strip sits above it.
    var dockBottom: CGFloat = 0
}

private struct DeskArrangeGeometryKey: EnvironmentKey {
    static let defaultValue: DeskArrangeGeometry? = nil
}

private struct DeskArrangeWindowKey: EnvironmentKey {
    static let defaultValue: DeskArrangeWindow? = nil
}

extension EnvironmentValues {
    /// Set on the Desk's columns while arranging (item 60); nil otherwise.
    var deskArrangeGeometry: DeskArrangeGeometry? {
        get { self[DeskArrangeGeometryKey.self] }
        set { self[DeskArrangeGeometryKey.self] = newValue }
    }

    /// The Desk window a piece is drawn on, as Arrange sees it (2.44); set with the geometry.
    var deskArrangeWindow: DeskArrangeWindow? {
        get { self[DeskArrangeWindowKey.self] }
        set { self[DeskArrangeWindowKey.self] = newValue }
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
    @Environment(\.deskArrangeWindow) private var window
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var resizeStart: ResizeStart?

    private struct ResizeStart {
        var scale: Double
        var size: CGSize
    }

    /// Room between a piece and its outline, in points.
    static let outlinePad: CGFloat = 6

    private var widget: String { placement.widget }
    /// The window's display key: `.main` until a window reports itself.
    private var windowKey: DeskDisplay { window?.display ?? .main }
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
                .onAppear { mode.setFrame(geo.frame(in: .named(DeskArrange.space)), of: widget, in: windowKey) }
                .onChange(of: geo.frame(in: .named(DeskArrange.space))) { _, f in mode.setFrame(f, of: widget, in: windowKey) }
                .onDisappear { mode.setFrame(nil, of: widget, in: windowKey) }
        }
    }

    /// The dashed outline, and above it the name tag with its Hide button (the one part of the
    /// outline that takes clicks).
    private var outline: some View {
        RoundedRectangle(cornerRadius: 8)
            .strokeBorder(Color.white.opacity(0.85), style: StrokeStyle(lineWidth: 1.5, dash: [6, 4]))
            .shadow(color: .black.opacity(0.5), radius: 2)
            .padding(-Self.outlinePad)
            .allowsHitTesting(false)
            .overlay(alignment: .topLeading) {
                nameTag.offset(x: -Self.outlinePad, y: -Self.outlinePad - 20)
            }
    }

    private var nameTag: some View {
        HStack(spacing: 4) {
            Text(name)
            if showsShowOn { DeskArrangeShowOnMenu(mode: mode, placement: placement, window: windowKey) }
            Button { mode.edit { $0.hide(widget) } } label: {
                Image(systemName: "xmark.circle.fill")
            }
            .buttonStyle(.plain)
            .help(DeskArrangeCopy.hide)
            .accessibilityLabel("\(DeskArrangeCopy.hide) \(name)")
        }
        .font(.system(size: 11, weight: .semibold))
        .foregroundStyle(.white)
        .padding(.horizontal, 6)
        .padding(.vertical, 2)
        .background(Capsule().fill(Color.black.opacity(0.55)))
        .fixedSize()
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
                                            frame: mode.drag?.frame ?? mode.layoutFrame(of: widget, in: windowKey) ?? .zero,
                                            window: windowKey, origin: window?.rect.origin ?? .zero)
            }
            .onEnded { v in drop(at: v.location) }
    }

    /// The drop (DeskArrangeMode.drop, DeskArrange.dropped): onto the window under the pointer, the
    /// stack there or else the nearest anchor, in front of the first piece whose middle is below the
    /// pointer. Within this window that is the drop from before 2.44 (a reorder within its stack).
    private func drop(at location: CGPoint) {
        guard let geometry, mode.working != nil else {
            mode.drag = nil
            return
        }
        let source = window ?? DeskArrangeWindow(display: .main, rect: .zero, geometry: geometry)
        withAnimation(DeskArrange.snapAnimation(reduceMotion: reduceMotion)) {
            mode.drop(widget, at: location, from: source)
            mode.drag = nil
        }
    }

    /// Show on in the chrome: offered with more than one display, never for the companion.
    private var showsShowOn: Bool {
        mode.offersShowOn && !DeskWindowPlan.mainOnly.contains(widget)
    }

    private var resize: some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .named(DeskArrange.space))
            .onChanged { v in
                let start = resizeStart ?? ResizeStart(scale: placement.scale,
                                                       size: mode.layoutFrame(of: widget, in: windowKey)?.size ?? .zero)
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
        Button(DeskArrangeCopy.hide) { mode.edit { $0.hide(widget) } }
        if showsShowOn {
            Button("\(DeskArrangeCopy.showOn) \(DeskArrangeCopy.showOnThis)") { mode.showOn(widget, all: false, window: windowKey) }
            Button("\(DeskArrangeCopy.showOn) \(DeskArrangeCopy.showOnAll)") { mode.showOn(widget, all: true, window: windowKey) }
        }
    }
}

/// Show on, in a piece's name tag (2.44): This display or All displays, the one in force checked.
private struct DeskArrangeShowOnMenu: View {
    let mode: DeskArrangeMode
    let placement: DeskPlacement
    let window: DeskDisplay

    var body: some View {
        let all = placement.monitor == .all
        Menu {
            Toggle(DeskArrangeCopy.showOnThis, isOn: choice(all: false, on: !all))
            Toggle(DeskArrangeCopy.showOnAll, isOn: choice(all: true, on: all))
        } label: {
            Image(systemName: all ? "display.2" : "display")
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .help(DeskArrangeCopy.showOn)
        .accessibilityLabel(DeskArrangeCopy.showOn)
    }

    private func choice(all: Bool, on: Bool) -> Binding<Bool> {
        Binding(get: { on }, set: { picked in
            if picked { mode.showOn(placement.widget, all: all, window: window) }
        })
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
/// it would land on brightest, the dragged piece's ghost under the pointer, and at the bottom of
/// the plate the Hidden strip, with a Show button for each piece the layout leaves out. The bar
/// with Cancel and Done is not here: it floats above every window in its own panel
/// (DeskArrangeBarController), so an app window can never cover it.
struct DeskArrangeOverlay: View {
    let mode: DeskArrangeMode
    let geometry: DeskArrangeGeometry
    /// This window (2.44): the lights show on the window under the pointer, the ghost follows the
    /// pointer across windows, and the Hidden strip is the main window's.
    var window: DeskArrangeWindow? = nil

    private var display: DeskDisplay { window?.display ?? .main }

    var body: some View {
        ZStack(alignment: .topLeading) {
            if let drag = mode.drag {
                lights(drag)
                ghost(drag)
            }
            if display == .main, let hidden = mode.working?.hidden, !hidden.isEmpty {
                hiddenStrip(hidden)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    /// "Hidden: Clock and date [Show] …", above the Dock's reach.
    private func hiddenStrip(_ hidden: [String]) -> some View {
        HStack(spacing: 8) {
            Text(DeskArrangeCopy.hiddenStrip).font(.system(size: 11, weight: .semibold))
            ForEach(hidden, id: \.self) { widget in
                let name = DeskLayout.widgets.first { $0.key == widget }?.name ?? widget
                HStack(spacing: 4) {
                    Text(name).font(.system(size: 11))
                    Button(DeskArrangeCopy.show) { mode.edit { $0.show(widget) } }
                        .controlSize(.mini)
                        .accessibilityLabel("\(DeskArrangeCopy.show) \(name)")
                }
                .padding(.leading, 6)
                .padding(.trailing, 2)
                .padding(.vertical, 2)
                .background(Capsule().fill(Color.white.opacity(0.12)))
            }
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Capsule().fill(Color.black.opacity(0.55)))
        .environment(\.colorScheme, .dark)
        .fixedSize()
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
        .padding(.bottom, geometry.dockBottom + 12)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(DeskArrangeCopy.hiddenStrip)
    }

    /// The drag's landing, when it is on this window.
    private func target(_ drag: DeskArrangeDrag) -> DeskAnchor? {
        let here = window ?? DeskArrangeWindow(display: .main, rect: .zero, geometry: geometry)
        let source = mode.windows[drag.window] ?? (drag.window == here.display ? here : nil)
        guard let source, let land = mode.landing(of: drag.widget, at: drag.location, from: source),
              land.window == here.display else { return nil }
        return land.anchor
    }

    @ViewBuilder
    private func lights(_ drag: DeskArrangeDrag) -> some View {
        if let target = target(drag) { anchorDots(lit: target) }
    }

    private func anchorDots(lit target: DeskAnchor) -> some View {
        ForEach(DeskAnchor.allCases, id: \.self) { anchor in
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
        let origin = window?.rect.origin ?? .zero
        let center = drag.ghostCenter
        return RoundedRectangle(cornerRadius: 8)
            .fill(Color.accentColor.opacity(0.18))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.white.opacity(0.9), lineWidth: 1.5))
            .frame(width: max(40, f.width + 12), height: max(20, f.height + 12))
            .position(x: center.x - origin.x, y: center.y - origin.y)
            .allowsHitTesting(false)
            .accessibilityHidden(true)
    }
}

/// Arrange mode's bar: what to do, Cancel and Done. It sits in DeskArrangeBarController's
/// floating panel, never on the Desk window.
struct DeskArrangeBar: View {
    var body: some View {
        VStack(spacing: 8) {
            Text(DeskArrangeCopy.barTitle).font(.headline)
            Text(DeskArrangeCopy.barHint)
                .font(.callout)
                .multilineTextAlignment(.center)
                .frame(width: 300)
            Text(DeskArrangeCopy.barKeys)
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
        .fixedSize()
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
        .environment(\.colorScheme, .dark)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(DeskArrangeCopy.barTitle)
    }
}

/// The bar's panel: borderless, above normal windows (Settings included), on every Space. It
/// takes the key so Return and Escape reach Sanduhr wherever Arrange mode was started from.
final class DeskArrangeBarPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
    /// Escape is Arrange mode's Cancel (DeskController's key monitor): the panel never closes itself.
    override func cancelOperation(_ sender: Any?) {}
}

/// Shows Arrange mode's bar (item 60) in its own small floating panel at the middle of the screen
/// the Desk is on, while arranging, and closes it when Arrange mode ends. The drag lights and the
/// ghost stay on the Desk window.
final class DeskArrangeBarController {
    private var panel: DeskArrangeBarPanel?

    /// state.yaml's `desk_arrange.bar_visible`.
    var isVisible: Bool { panel?.isVisible ?? false }

    /// Opens the panel on `screen` (or moves it there) and makes it key.
    func show(on screen: NSScreen?) {
        let p = panel ?? makePanel()
        panel = p
        place(on: screen)
        p.makeKeyAndOrderFront(nil)
    }

    /// Centers the open panel on `screen`'s visible frame (a display change while arranging).
    func place(on screen: NSScreen?) {
        guard let p = panel, let visible = (screen ?? NSScreen.main)?.visibleFrame else { return }
        p.contentView?.layoutSubtreeIfNeeded()
        let size = DeskArrange.barSize(fitting: p.contentView?.fittingSize)
        p.setFrame(NSRect(origin: DeskArrange.barOrigin(size: size, in: visible), size: size), display: true)
    }

    func close() {
        panel?.orderOut(nil)
        panel?.close()
        panel = nil
    }

    private func makePanel() -> DeskArrangeBarPanel {
        let p = DeskArrangeBarPanel(contentRect: NSRect(x: 0, y: 0, width: 340, height: 180),
                                    styleMask: [.borderless, .nonactivatingPanel],
                                    backing: .buffered, defer: false)
        p.isReleasedWhenClosed = false
        p.level = .floating
        p.isFloatingPanel = true
        p.hidesOnDeactivate = false
        p.becomesKeyOnlyIfNeeded = false
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.title = DeskArrangeCopy.barTitle
        let host = FirstClickHostingView(rootView: DeskArrangeBar().fixedSize())
        p.contentView = host
        return p
    }
}

/// A window Arrange mode puts away while it runs (Settings, where it is often started from) and
/// brings back after.
@MainActor
protocol DeskArrangeHideable: AnyObject {
    var isVisible: Bool { get }
    func orderOut(_ sender: Any?)
    func makeKeyAndOrderFront(_ sender: Any?)
}

extension NSWindow: DeskArrangeHideable {}

/// The windows Arrange mode put away: `hide` orders out the visible ones and remembers them,
/// `restore` brings exactly those back (key and in front) and forgets them.
@MainActor
final class DeskArrangeStash {
    private(set) var hidden: [DeskArrangeHideable] = []

    func hide(_ windows: [DeskArrangeHideable?]) {
        for case let w? in windows where w.isVisible && !hidden.contains(where: { $0 === w }) {
            w.orderOut(nil)
            hidden.append(w)
        }
    }

    func restore() {
        let back = hidden
        hidden = []
        back.forEach { $0.makeKeyAndOrderFront(nil) }
    }
}

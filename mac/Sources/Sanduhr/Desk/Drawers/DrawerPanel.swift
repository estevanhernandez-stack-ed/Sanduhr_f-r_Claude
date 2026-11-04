import AppKit
import QuickLookUI
import SwiftUI

/// What one drawer's panel shows. The controller builds it from the service's published values;
/// the panel redraws only when it changes.
struct DrawerPanelState: Equatable {
    var id: String
    var name: String
    var entries: [DrawerEntry]
    var folded: Bool
    var look: DrawerLook
    var opacity: Int
    var showAllExtensions: Bool
    /// The look Look ▸ marks: this drawer's own, or nil for "same as all".
    var ownLook: DrawerLook?
    /// The Desktop Folder permission is denied (`DesktopPermission`).
    var desktopDenied = false
}

/// What a panel asks its controller to do. Plain closures so the panel holds no service.
final class DrawerPanelActions {
    var toggleFold: () -> Void = {}
    /// A title-bar drag asks where the frame settles (Option held turns snapping off).
    var dragProposed: (CGRect, Bool) -> CGRect = { f, _ in f }
    var dragEnded: (CGRect) -> Void = { _ in }
    var open: ([String]) -> Void = { _ in }
    var reveal: ([String]) -> Void = { _ in }
    var renameItem: (String) -> Void = { _ in }
    var trash: ([String]) -> Void = { _ in }
    /// Files dropped on the panel (paths), for the service's hand move.
    var drop: ([String]) -> Void = { _ in }
    var renameDrawer: () -> Void = {}
    var hide: () -> Void = {}
    var setLook: (DrawerLook?) -> Void = { _ in }
    var tidy: () -> Void = {}
    var openSettings: () -> Void = {}
}

/// A menu item that runs a closure, so the panel needs no target objects.
final class DrawerMenuItem: NSMenuItem {
    private var handler: (() -> Void)?

    convenience init(_ title: String, key: String = "", checked: Bool = false, handler: @escaping () -> Void) {
        self.init(title: title, action: #selector(fire), keyEquivalent: key)
        self.handler = handler
        target = self
        state = checked ? .on : .off
    }

    @objc private func fire() { handler?() }
}

/// The non-activating panel a drawer lives in: it takes the key for the keyboard (Return, Space,
/// Command-R, Command-Delete) without activating Sanduhr, so the app in front stays in front.
final class DrawerPanelWindow: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: DrawerPlacement.width, height: DrawerPlacement.openHeight),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isReleasedWhenClosed = false
        level = DeskClickLayer.level
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        hidesOnDeactivate = false
        isMovable = false
    }

    override func becomeKey() {
        super.becomeKey()
        (contentView as? DrawerPanelView)?.focusGrid()
    }
}

/// The title: name and count, folded or open. A click folds and opens; a drag moves the panel.
final class DrawerTitleBar: NSView {
    private let label = NSTextField(labelWithString: "")
    var actions = DrawerPanelActions()
    var menuProvider: (() -> NSMenu)?
    private var startMouse = NSPoint.zero
    private var startOrigin = NSPoint.zero
    private var dragging = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        label.font = .systemFont(ofSize: 13, weight: .semibold)
        label.textColor = NSColor.white.withAlphaComponent(0.92)
        label.lineBreakMode = .byTruncatingTail
        addSubview(label)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    var title: String {
        get { label.stringValue }
        set { label.stringValue = newValue }
    }

    override func layout() {
        super.layout()
        label.frame = NSRect(x: 14, y: (bounds.height - 18) / 2, width: max(0, bounds.width - 28), height: 18)
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func menu(for event: NSEvent) -> NSMenu? { menuProvider?() }

    override func mouseDown(with event: NSEvent) {
        guard let w = window else { return }
        startMouse = NSEvent.mouseLocation
        startOrigin = w.frame.origin
        dragging = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let w = window else { return }
        let now = NSEvent.mouseLocation
        let dx = now.x - startMouse.x, dy = now.y - startMouse.y
        if !dragging && hypot(dx, dy) < 3 { return }
        dragging = true
        var frame = w.frame
        frame.origin = NSPoint(x: startOrigin.x + dx, y: startOrigin.y + dy)
        let settled = actions.dragProposed(frame, event.modifierFlags.contains(.option))
        w.setFrameOrigin(settled.origin)
    }

    override func mouseUp(with event: NSEvent) {
        guard let w = window else { return }
        if dragging { actions.dragEnded(w.frame) } else { actions.toggleFold() }
        dragging = false
    }
}

/// One thumbnail and name. A double-click opens; the single click and the drag are the grid's.
final class DrawerItemView: NSCollectionViewItem {
    static let identifier = NSUserInterfaceItemIdentifier("drawer.item")
    static let size = NSSize(width: 82, height: 88)

    private let icon = NSImageView()
    private let label = NSTextField(wrappingLabelWithString: "")
    private(set) var path = ""
    var onOpen: (() -> Void)?

    private final class Root: NSView {
        var onOpen: (() -> Void)?
        override func mouseDown(with event: NSEvent) {
            if event.clickCount == 2 { onOpen?() } else { super.mouseDown(with: event) }
        }
    }

    override func loadView() {
        let root = Root(frame: NSRect(origin: .zero, size: Self.size))
        root.wantsLayer = true
        root.layer?.cornerRadius = 6
        icon.frame = NSRect(x: 13, y: 30, width: DrawerThumbnails.side, height: DrawerThumbnails.side)
        icon.imageScaling = .scaleProportionallyUpOrDown
        label.frame = NSRect(x: 2, y: 0, width: Self.size.width - 4, height: 28)
        label.font = .systemFont(ofSize: 11)
        label.alignment = .center
        label.maximumNumberOfLines = 2
        label.textColor = NSColor.white.withAlphaComponent(0.9)
        label.cell?.truncatesLastVisibleLine = true
        root.addSubview(icon)
        root.addSubview(label)
        view = root
        (view as? Root)?.onOpen = { [weak self] in self?.onOpen?() }
    }

    override var isSelected: Bool { didSet { restyle() } }

    private func restyle() {
        view.layer?.backgroundColor = isSelected ? NSColor.controlAccentColor.withAlphaComponent(0.4).cgColor : NSColor.clear.cgColor
    }

    func configure(_ entry: DrawerEntry, showAllExtensions: Bool, scale: CGFloat) {
        path = entry.path
        let hidden = (try? URL(fileURLWithPath: entry.path).resourceValues(forKeys: [.hasHiddenExtensionKey]))?.hasHiddenExtension ?? false
        label.stringValue = DrawerDisplay.name(entry.name, isFolder: entry.isFolder,
                                               showAllExtensions: showAllExtensions, hiddenExtension: hidden)
        let thumbs = DrawerThumbnails.shared
        icon.image = thumbs.cached(entry.path) ?? thumbs.placeholder(entry.path)
        let wanted = entry.path
        thumbs.request(entry.path, scale: scale) { [weak self] image in
            if self?.path == wanted { self?.icon.image = image }
        }
        restyle()
    }
}

/// The grid: keys, the context menu, Quick Look and the drag out all start here.
final class DrawerCollectionView: NSCollectionView, QLPreviewPanelDataSource, QLPreviewPanelDelegate {
    var selectedPaths: () -> [String] = { [] }
    var onKey: (DrawerKeyAction) -> Void = { _ in }
    var menuProvider: (NSEvent) -> NSMenu? = { _ in nil }

    override var acceptsFirstResponder: Bool { true }
    override func menu(for event: NSEvent) -> NSMenu? { menuProvider(event) }

    override func keyDown(with event: NSEvent) {
        if let a = DrawerKeyAction.of(keyCode: event.keyCode, command: event.modifierFlags.contains(.command),
                                      characters: event.charactersIgnoringModifiers) {
            onKey(a)
        } else {
            super.keyDown(with: event)
        }
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard event.modifierFlags.contains(.command),
              let a = DrawerKeyAction.of(keyCode: event.keyCode, command: true, characters: event.charactersIgnoringModifiers)
        else { return super.performKeyEquivalent(with: event) }
        onKey(a)
        return true
    }

    func toggleQuickLook() {
        guard let panel = QLPreviewPanel.shared() else { return }
        if QLPreviewPanel.sharedPreviewPanelExists() && panel.isVisible { panel.orderOut(nil) } else { panel.makeKeyAndOrderFront(nil) }
    }

    override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }
    override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        panel.dataSource = self
        panel.delegate = self
    }
    override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        panel.dataSource = nil
        panel.delegate = nil
    }

    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int { selectedPaths().count }
    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! {
        let paths = selectedPaths()
        guard paths.indices.contains(index) else { return nil }
        return URL(fileURLWithPath: paths[index]) as NSURL
    }
}

/// A drawer's whole panel: the plate (by look), the title bar and the grid. The service's
/// published changes reach it through `configure`; nothing in it runs on a timer.
final class DrawerPanelView: NSView, NSCollectionViewDataSource, NSCollectionViewDelegate {
    private let plate = NSView()
    private let blur = NSVisualEffectView()
    private let titleBar = DrawerTitleBar(frame: .zero)
    private let scroll = NSScrollView()
    private let grid = DrawerCollectionView()
    private let note = NSTextField(labelWithString: "")
    private(set) var state: DrawerPanelState?
    private var hovering = false
    let actions = DrawerPanelActions()

    /// The menu's last item. It opens Settings, Drawers.
    static let settingsItem = "Drawers Settings…"

    override var isFlipped: Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.cornerRadius = 12
        layer?.masksToBounds = true
        buildPlate()
        buildGrid()
        titleBar.actions = actions
        titleBar.menuProvider = { [weak self] in self?.drawerMenu(for: []) ?? NSMenu() }
        addSubview(titleBar)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    private func buildPlate() {
        blur.material = .hudWindow
        blur.blendingMode = .behindWindow
        blur.state = .active
        plate.wantsLayer = true
        addSubview(blur)
        addSubview(plate)
    }

    private func buildGrid() {
        let flow = NSCollectionViewFlowLayout()
        flow.itemSize = DrawerItemView.size
        flow.minimumInteritemSpacing = 2
        flow.minimumLineSpacing = 4
        flow.sectionInset = NSEdgeInsets(top: 4, left: 8, bottom: 8, right: 8)
        grid.collectionViewLayout = flow
        grid.register(DrawerItemView.self, forItemWithIdentifier: DrawerItemView.identifier)
        grid.dataSource = self
        grid.delegate = self
        grid.isSelectable = true
        grid.allowsMultipleSelection = true
        grid.backgroundColors = [.clear]
        grid.setDraggingSourceOperationMask([.move, .copy], forLocal: false)
        grid.setDraggingSourceOperationMask(.move, forLocal: true)
        grid.selectedPaths = { [weak self] in self?.selectedPaths() ?? [] }
        grid.onKey = { [weak self] in self?.perform($0) }
        grid.menuProvider = { [weak self] event in self?.menu(at: event) }
        scroll.documentView = grid
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        addSubview(scroll)
        note.font = .systemFont(ofSize: 12)
        note.textColor = .secondaryLabelColor
        note.alignment = .center
        note.isHidden = true
        addSubview(note)
    }

    func focusGrid() { window?.makeFirstResponder(grid) }

    // MARK: Layout and style

    override func layout() {
        super.layout()
        let b = bounds
        blur.frame = b
        plate.frame = b
        titleBar.frame = NSRect(x: 0, y: 0, width: b.width, height: DrawerPlacement.foldedHeight)
        scroll.frame = NSRect(x: 0, y: DrawerPlacement.foldedHeight, width: b.width, height: max(0, b.height - DrawerPlacement.foldedHeight))
        note.frame = NSRect(x: 8, y: DrawerPlacement.foldedHeight + 12, width: max(0, b.width - 16), height: 18)
    }

    private func restyle() {
        guard let s = state else { return }
        let v = DrawerLookValues.of(s.look, opacity: s.opacity, hovering: hovering)
        blur.isHidden = !v.usesBlur
        blur.alphaValue = v.blurAlpha
        plate.layer?.backgroundColor = DrawerSurface.color.withAlphaComponent(v.fillAlpha).cgColor
        layer?.borderWidth = v.borderAlpha > 0 ? 1 : 0
        layer?.borderColor = NSColor.white.withAlphaComponent(v.borderAlpha).cgColor
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        for a in trackingAreas { removeTrackingArea(a) }
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self))
    }

    override func mouseEntered(with event: NSEvent) { hovering = true; restyle() }
    override func mouseExited(with event: NSEvent) { hovering = false; restyle() }

    /// Takes the new state; redraws only what changed.
    func configure(_ new: DrawerPanelState) {
        let old = state
        state = new
        if old?.entries != new.entries || old?.showAllExtensions != new.showAllExtensions { grid.reloadData() }
        titleBar.title = DrawerTitle.text(name: new.name, count: new.entries.count)
        scroll.isHidden = new.folded
        let line = DrawerPanelNote.text(denied: new.desktopDenied, entries: new.entries.count)
        note.stringValue = line ?? ""
        note.isHidden = new.folded || line == nil
        if old?.look != new.look || old?.opacity != new.opacity { restyle() }
        if old == nil { restyle() }
        needsLayout = true
    }

    // MARK: Grid data

    func numberOfSections(in collectionView: NSCollectionView) -> Int { 1 }

    func collectionView(_ collectionView: NSCollectionView, numberOfItemsInSection section: Int) -> Int {
        state?.entries.count ?? 0
    }

    func collectionView(_ collectionView: NSCollectionView, itemForRepresentedObjectAt indexPath: IndexPath) -> NSCollectionViewItem {
        let item = collectionView.makeItem(withIdentifier: DrawerItemView.identifier, for: indexPath)
        guard let cell = item as? DrawerItemView, let s = state, s.entries.indices.contains(indexPath.item) else { return item }
        let entry = s.entries[indexPath.item]
        cell.configure(entry, showAllExtensions: s.showAllExtensions, scale: window?.backingScaleFactor ?? 2)
        cell.onOpen = { [weak self] in self?.actions.open([entry.path]) }
        return cell
    }

    /// Drag out: the file's URL, so the Finder or another app takes it as a file.
    func collectionView(_ collectionView: NSCollectionView, pasteboardWriterForItemAt indexPath: IndexPath) -> NSPasteboardWriting? {
        guard let s = state, s.entries.indices.contains(indexPath.item) else { return nil }
        return URL(fileURLWithPath: s.entries[indexPath.item].path) as NSURL
    }

    func selectedPaths() -> [String] {
        guard let s = state else { return [] }
        return grid.selectionIndexPaths.sorted().compactMap { s.entries.indices.contains($0.item) ? s.entries[$0.item].path : nil }
    }

    // MARK: Keys

    private func perform(_ action: DrawerKeyAction) {
        let paths = selectedPaths()
        guard !paths.isEmpty else { return }
        switch action {
        case .open: actions.open(paths)
        case .quickLook: grid.toggleQuickLook()
        case .reveal: actions.reveal(paths)
        case .trash: actions.trash(paths)
        }
    }

    // MARK: Drag in

    private func droppedPaths(_ info: NSDraggingInfo) -> [String] {
        let urls = info.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        return urls.map(\.path)
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        droppedPaths(sender).isEmpty ? [] : .move
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        droppedPaths(sender).isEmpty ? [] : .move
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let paths = droppedPaths(sender)
        guard !paths.isEmpty else { return false }
        actions.drop(paths)
        return true
    }

    // MARK: Menu

    private func menu(at event: NSEvent) -> NSMenu? {
        let point = grid.convert(event.locationInWindow, from: nil)
        if let ip = grid.indexPathForItem(at: point) {
            if !grid.selectionIndexPaths.contains(ip) { grid.selectionIndexPaths = [ip] }
            return drawerMenu(for: selectedPaths())
        }
        return drawerMenu(for: [])
    }

    /// Open, Quick Look, Show in Finder, Rename, Move to Trash for what is selected, then the drawer's own items.
    private func drawerMenu(for paths: [String]) -> NSMenu {
        let m = NSMenu()
        if !paths.isEmpty {
            m.addItem(DrawerMenuItem("Open") { [weak self] in self?.actions.open(paths) })
            m.addItem(DrawerMenuItem("Quick Look") { [weak self] in self?.grid.toggleQuickLook() })
            m.addItem(DrawerMenuItem("Show in Finder") { [weak self] in self?.actions.reveal(paths) })
            if paths.count == 1 { m.addItem(DrawerMenuItem("Rename") { [weak self] in self?.actions.renameItem(paths[0]) }) }
            m.addItem(DrawerMenuItem("Move to Trash") { [weak self] in self?.actions.trash(paths) })
            m.addItem(.separator())
        }
        addDrawerItems(to: m)
        return m
    }

    private func addDrawerItems(to m: NSMenu) {
        m.addItem(DrawerMenuItem("Rename drawer…") { [weak self] in self?.actions.renameDrawer() })
        m.addItem(DrawerMenuItem(state?.folded == true ? "Open" : "Fold") { [weak self] in self?.actions.toggleFold() })
        m.addItem(DrawerMenuItem("Hide") { [weak self] in self?.actions.hide() })
        let look = NSMenuItem(title: "Look", action: nil, keyEquivalent: "")
        look.submenu = lookMenu()
        m.addItem(look)
        m.addItem(DrawerMenuItem("Tidy drawers") { [weak self] in self?.actions.tidy() })
        m.addItem(DrawerMenuItem(DrawerPanelView.settingsItem) { [weak self] in self?.actions.openSettings() })
    }

    private func lookMenu() -> NSMenu {
        let sub = NSMenu()
        let own = state?.ownLook
        sub.addItem(DrawerMenuItem("Same as the other drawers", checked: own == nil) { [weak self] in self?.actions.setLook(nil) })
        sub.addItem(.separator())
        for l in DrawerLook.allCases {
            sub.addItem(DrawerMenuItem(DrawerLookValues.name(l), checked: own == l) { [weak self] in self?.actions.setLook(l) })
        }
        return sub
    }
}

/// The theme surface Solid draws at the opacity: the default theme's background, which the Desk's
/// own themes sit near. One place to change when drawers follow the chosen theme.
enum DrawerSurface {
    static var color: NSColor { NSColor(ThemeRegistry.default.palette.bg) }
}

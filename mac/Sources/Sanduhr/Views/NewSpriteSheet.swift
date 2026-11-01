import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// "New sprite from an image..." (spec 2.39.0 section 18): the picker and the sheet. The open panel is only the
/// source of bytes; the rules are `WidgetNewSprite`'s and the choices are `NewSpriteModel`'s. Nothing is sent.
@MainActor
enum NewSpritePicker {
    /// One PNG from an open panel, read at most one byte past the 2 MB limit; nil when cancelled or unreadable.
    static func pick() -> Data? {
        let panel = NSOpenPanel()
        panel.title = "Choose the image for a new sprite"
        panel.message = panel.title
        panel.allowedContentTypes = [.png]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        guard panel.runModal() == .OK, let url = panel.url,
              let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        return try? handle.read(upToCount: PackValidator.maxImageBytes + 1)
    }
}

struct NewSpriteSheet: View {
    @Bindable var model: NewSpriteModel
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("New sprite from an image").font(.headline)
            if let why = model.imageError {
                Text(why).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            } else {
                NewSpriteCellSize(model: model)
                NewSpriteActions(model: model)
                NewSpriteGrid(model: model)
                NewSpriteIdentity(model: model)
            }
            NewSpriteErrors(errors: model.errors)
            NewSpriteButtons(model: model, close: close)
        }
        .padding(16)
        .frame(minWidth: 520, idealWidth: 560)
        .onChange(of: model.made) { _, made in if made { close() } }
    }
}

private struct NewSpriteCellSize: View {
    @Bindable var model: NewSpriteModel

    var body: some View {
        HStack(spacing: 12) {
            Stepper("Cell width: \(model.cellWidth)", value: widthBinding,
                    in: WidgetNewSprite.cellMin...WidgetNewSprite.cellMax)
            Stepper("Cell height: \(model.cellHeight)", value: heightBinding,
                    in: WidgetNewSprite.cellMin...WidgetNewSprite.cellMax)
        }
        Text(summary).font(.caption)
            .foregroundStyle(model.gridFits ? Color.secondary : Color.orange)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var widthBinding: Binding<Int> {
        Binding(get: { model.cellWidth }, set: { model.setCell(width: $0, height: model.cellHeight) })
    }

    private var heightBinding: Binding<Int> {
        Binding(get: { model.cellHeight }, set: { model.setCell(width: model.cellWidth, height: $0) })
    }

    private var summary: String {
        let size = "The image is \(model.imageWidth)x\(model.imageHeight)."
        if model.gridFits { return "\(size) That is \(model.columns) x \(model.rows) = \(model.cellCount) cells, numbered across then down." }
        return "\(size) That isn't a whole number of \(model.cellWidth)x\(model.cellHeight) cells. Change the cell size."
    }
}

private struct NewSpriteActions: View {
    @Bindable var model: NewSpriteModel

    var body: some View {
        Picker("Tag cells as", selection: $model.selectedAction) {
            ForEach(WidgetNewSprite.actions, id: \.self) { Text($0).tag($0) }
        }
        .pickerStyle(.segmented)
        Text("Click a cell to tag it, click it again to untag it. Walk plays its cells in the order you click them.")
            .font(.caption).foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        let untagged = model.untagged
        if !untagged.isEmpty {
            Text("Untagged: " + untagged.map { "\($0.action) (\($0.note))" }.joined(separator: ", ") + ".")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The sheet drawn nearest-neighbour with the cell grid, each cell's number and its tags. One Canvas, so a sheet of
/// hundreds of cells costs one draw; a click maps back to the cell under it.
private struct NewSpriteGrid: View {
    let model: NewSpriteModel

    var body: some View {
        if let image = NSImage(data: model.png), model.cellCount > 0 {
            ScrollView([.horizontal, .vertical]) {
                Canvas { context, _ in draw(context, image) }
                    .frame(width: displayWidth, height: displayHeight)
                    .contentShape(Rectangle())
                    .onTapGesture { point in
                        model.tap(NewSpriteLayout.cell(at: point, zoom: zoom, model: model))
                    }
            }
            .frame(height: min(displayHeight, 280) + 2)
            .border(Color.secondary.opacity(0.4))
        }
    }

    private var zoom: CGFloat { NewSpriteLayout.zoom(forWidth: model.imageWidth) }
    private var displayWidth: CGFloat { CGFloat(model.imageWidth) * zoom }
    private var displayHeight: CGFloat { CGFloat(model.imageHeight) * zoom }

    private func draw(_ context: GraphicsContext, _ image: NSImage) {
        let whole = CGRect(x: 0, y: 0, width: displayWidth, height: displayHeight)
        context.fill(Path(whole), with: .color(.gray.opacity(0.25)))
        context.draw(context.resolve(Image(nsImage: image).interpolation(.none)), in: whole)
        for cell in 0..<model.cellCount {
            let rect = NewSpriteLayout.rect(of: cell, zoom: zoom, model: model)
            let tagged = model.cells(of: model.selectedAction).contains(cell)
            if tagged { context.fill(Path(rect), with: .color(.accentColor.opacity(0.35))) }
            context.stroke(Path(rect), with: .color(tagged ? .accentColor : .white.opacity(0.7)), lineWidth: tagged ? 2 : 1)
            drawLabel(context, cell, rect)
        }
    }

    /// The cell's number, with its place in the selected action's order and the actions it is tagged to.
    private func drawLabel(_ context: GraphicsContext, _ cell: Int, _ rect: CGRect) {
        guard rect.width >= 24, rect.height >= 18 else { return }
        var label = "\(cell + 1)"
        if let place = model.order(of: cell, in: model.selectedAction) { label += "  #\(place)" }
        context.draw(Text(label).font(.system(size: 10, weight: .semibold)).foregroundColor(.white),
                     at: CGPoint(x: rect.minX + 3, y: rect.minY + 2), anchor: .topLeading)
        let others = model.actions(of: cell).map { String($0.prefix(1)).uppercased() }.joined()
        if !others.isEmpty {
            context.draw(Text(others).font(.system(size: 9, design: .monospaced)).foregroundColor(.yellow),
                         at: CGPoint(x: rect.maxX - 3, y: rect.maxY - 2), anchor: .bottomTrailing)
        }
    }
}

/// How the sheet's pixels map to the drawing: a whole-number zoom so the pixels stay crisp.
enum NewSpriteLayout {
    static func zoom(forWidth width: Int) -> CGFloat {
        CGFloat(max(1, min(8, 440 / max(width, 1))))
    }

    @MainActor
    static func rect(of cell: Int, zoom: CGFloat, model: NewSpriteModel) -> CGRect {
        let col = cell % max(model.columns, 1), row = cell / max(model.columns, 1)
        let w = CGFloat(model.cellWidth) * zoom, h = CGFloat(model.cellHeight) * zoom
        return CGRect(x: CGFloat(col) * w, y: CGFloat(row) * h, width: w, height: h)
    }

    /// The cell under `point`, or -1 outside the whole cells.
    @MainActor
    static func cell(at point: CGPoint, zoom: CGFloat, model: NewSpriteModel) -> Int {
        let w = CGFloat(model.cellWidth) * zoom, h = CGFloat(model.cellHeight) * zoom
        guard point.x >= 0, point.y >= 0 else { return -1 }
        let col = Int(point.x / w), row = Int(point.y / h)
        guard col < model.columns, row < model.rows else { return -1 }
        return row * model.columns + col
    }
}

private struct NewSpriteIdentity: View {
    @Bindable var model: NewSpriteModel

    var body: some View {
        HStack(spacing: 12) {
            TextField("Name", text: $model.name, prompt: Text("My sprite"))
            Stepper("Scale: \(model.scale)", value: $model.scale, in: WidgetNewSprite.scaleRange)
                .fixedSize()
        }
    }
}

private struct NewSpriteErrors: View {
    let errors: [String]

    var body: some View {
        ForEach(Array(errors.enumerated()), id: \.offset) { _, line in
            Text(line).font(.caption).foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

private struct NewSpriteButtons: View {
    let model: NewSpriteModel
    let close: () -> Void

    var body: some View {
        HStack {
            Text("The image stays on this Mac.").font(.caption).foregroundStyle(.secondary)
            Spacer()
            Button("Cancel", role: .cancel, action: close).keyboardShortcut(.cancelAction)
            Button("Make") { model.make() }
                .keyboardShortcut(.defaultAction)
                .disabled(model.imageError != nil)
        }
    }
}

import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// "Use my own art..." on a sprite with stages (spec 2.43.0 section 24): a column per stage, each with its current
/// first cell and a Choose... button. The open panel is only the source of bytes; the rules are `OwnArtStagesModel`'s
/// and `WidgetOwnArt`'s. Nothing is sent.
@MainActor
enum OwnArtStagePicker {
    /// One PNG from an open panel, read at most one byte past 2 MB; nil when cancelled or unreadable.
    static func pick(stageName: String) -> Data? {
        let panel = NSOpenPanel()
        panel.title = "Your own art for \(stageName)"
        panel.message = panel.title
        panel.allowedContentTypes = [.png]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        guard panel.runModal() == .OK, let url = panel.url, let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        return try? handle.read(upToCount: PackValidator.maxImageBytes + 1)
    }
}

struct OwnArtStagesSheet: View {
    @Bindable var model: OwnArtStagesModel
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Use my own art").font(.headline)
            Text("Choose a PNG sheet for any stage. A stage you leave alone keeps its art.")
                .font(.caption).foregroundStyle(.secondary)
            ScrollView(.horizontal) {
                HStack(alignment: .top, spacing: 12) {
                    ForEach(model.columns) { column in
                        OwnArtStageColumn(model: model, column: column)
                    }
                }
                .padding(.vertical, 4)
            }
            HStack {
                Spacer()
                Button("Cancel") { close() }.keyboardShortcut(.cancelAction)
                Button("Apply") {
                    model.apply()
                    close()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(!model.canApply)
                .accessibilityLabel("Apply own art")
            }
        }
        .padding(16)
        .frame(minWidth: 360, idealWidth: 560)
    }
}

private struct OwnArtStageColumn: View {
    let model: OwnArtStagesModel
    let column: OwnArtStagesModel.Column

    var body: some View {
        VStack(alignment: .center, spacing: 6) {
            Text(column.name).font(.subheadline.weight(.semibold))
            Text(column.growthText).font(.caption).foregroundStyle(.secondary)
            OwnArtCellPreview(data: column.preview ?? column.current)
            if let why = column.error {
                Text(why).font(.caption).foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack {
                Button("Choose\u{2026}") {
                    if let data = OwnArtStagePicker.pick(stageName: column.name) { model.pick(column.stage.id, data) }
                }
                .accessibilityLabel("Choose art for \(column.name)")
                if column.picked != nil {
                    Button("Clear") { model.clear(column.stage.id) }
                        .accessibilityLabel("Clear art for \(column.name)")
                }
            }
        }
        .frame(width: 160)
    }
}

private struct OwnArtCellPreview: View {
    let data: Data?

    var body: some View {
        Group {
            if let data, let image = NSImage(data: data) {
                Image(nsImage: image).interpolation(.none).resizable().scaledToFit()
            } else {
                Image(systemName: "photo").foregroundStyle(.secondary)
            }
        }
        .frame(width: 64, height: 64)
        .background(Color.secondary.opacity(0.12))
        .accessibilityHidden(true)
    }
}

import AppKit
import Observation
import SwiftUI

/// The review and result bars' state beyond the service's own: whether Edit is open, what it
/// changed, and the result being shown. The pending review itself is the service's.
@MainActor
@Observable
final class DrawerBarModel {
    var editing = false
    var edit = DrawerReviewEdit()
    var result: DrawerResultModel?

    func resetEdit() {
        editing = false
        edit = DrawerReviewEdit()
    }
}

/// What the bars' buttons do.
struct DrawerBarActions {
    var apply: () -> Void
    var dismiss: () -> Void
    var undo: () -> Void
    var done: () -> Void
}

/// The bar at the top of the Desk: the review while a sort waits, else the last result.
struct DrawerBarView: View {
    let service: DrawerService
    let bar: DrawerBarModel
    let actions: DrawerBarActions

    var body: some View {
        content
            .frame(width: DrawerBarMetrics.width)
    }

    @ViewBuilder
    private var content: some View {
        if let review = service.pending {
            DrawerReviewBar(model: DrawerReviewModel(review: review, existing: service.settings.drawers, edit: bar.edit),
                            bar: bar, actions: actions)
        } else if let result = bar.result {
            DrawerResultBar(model: result, actions: actions)
        }
    }
}

enum DrawerBarMetrics {
    static let width: CGFloat = 460
    static let editHeight: CGFloat = 260
}

private struct DrawerBarBackground: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(12)
            .background(RoundedRectangle(cornerRadius: 10).fill(.regularMaterial))
            .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.white.opacity(0.15)))
            .accessibilityElement(children: .contain)
    }
}

struct DrawerReviewBar: View {
    let model: DrawerReviewModel
    @Bindable var bar: DrawerBarModel
    let actions: DrawerBarActions

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(model.title).font(.headline)
            if bar.editing {
                DrawerEditList(model: model, bar: bar)
            } else {
                DrawerSummaryLines(model: model)
            }
            DrawerReviewButtons(model: model, bar: bar, actions: actions)
        }
        .modifier(DrawerBarBackground())
    }
}

private struct DrawerSummaryLines: View {
    let model: DrawerReviewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            ForEach(model.summary, id: \.id) { line in
                Text(line.text).font(.callout).foregroundStyle(.secondary).lineLimit(2)
            }
            Text("Files no rule matches stay on your Desktop.").font(.caption).foregroundStyle(.secondary)
        }
    }
}

private struct DrawerReviewButtons: View {
    let model: DrawerReviewModel
    @Bindable var bar: DrawerBarModel
    let actions: DrawerBarActions

    var body: some View {
        HStack {
            Button("Apply", action: actions.apply)
                .keyboardShortcut(.defaultAction)
                .disabled(!model.canApply)
            Button(bar.editing ? "Done editing" : "Edit") { bar.editing.toggle() }
            Button("Dismiss") { actions.dismiss() }
            Spacer()
        }
    }
}

/// Edit: a tick per file, and a name field for each new drawer.
private struct DrawerEditList: View {
    let model: DrawerReviewModel
    @Bindable var bar: DrawerBarModel

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 6) {
                ForEach(model.lines, id: \.id) { line in
                    DrawerEditSection(line: line, bar: bar)
                }
            }
        }
        .frame(maxHeight: DrawerBarMetrics.editHeight)
    }
}

private struct DrawerEditSection: View {
    let line: DrawerReviewLineView
    @Bindable var bar: DrawerBarModel

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            header
            ForEach(line.files, id: \.source) { file in
                Toggle(file.name, isOn: tick(file.source)).toggleStyle(.checkbox).lineLimit(1)
            }
        }
    }

    @ViewBuilder
    private var header: some View {
        if line.isNew {
            TextField("Drawer name", text: name).textFieldStyle(.roundedBorder)
            if let e = line.nameError { Text(e).font(.caption).foregroundStyle(.red) }
        } else {
            Text(line.name).font(.callout.weight(.semibold))
        }
    }

    private var name: Binding<String> {
        Binding(get: { bar.edit.renames[line.id] ?? line.name }, set: { bar.edit.renames[line.id] = $0 })
    }

    private func tick(_ source: String) -> Binding<Bool> {
        Binding(get: { !bar.edit.unticked.contains(source) }, set: { _ in bar.edit.toggle(source) })
    }
}

typealias DrawerReviewLineView = DrawerReviewModel.Line

struct DrawerResultBar: View {
    let model: DrawerResultModel
    let actions: DrawerBarActions

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(model.line).font(.callout).lineLimit(4).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 8)
            if model.canUndo { Button("Undo", action: actions.undo) }
            Button("Done", action: actions.done).keyboardShortcut(.defaultAction)
        }
        .modifier(DrawerBarBackground())
    }
}

/// The bar's window: a non-activating panel at the Desk's level, top center of the Desk's screen,
/// that takes the key so the Edit name fields can type.
final class DrawerBarWindow: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: DrawerBarMetrics.width, height: 120),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isReleasedWhenClosed = false
        level = DeskClickLayer.level
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        hidesOnDeactivate = false
    }
}

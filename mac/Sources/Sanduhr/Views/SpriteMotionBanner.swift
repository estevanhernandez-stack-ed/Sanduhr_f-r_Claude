import SwiftUI

/// "Claude suggests a motion" at the top of Settings, Widgets while a motion waits (spec 2.39.0 section 19), modelled on
/// `WidgetSuggestionBanner`: the heading, the sprite's name, Claude's note and what changes; then Preview on the Desk
/// (End preview while it shows), Apply and Decline. Nothing is written or drawn until the user presses a button.
struct SpriteMotionBanner: View {
    let model: SpriteMotionReviewModel
    let previewNote: String?
    let preview: () -> Void
    let endPreview: () -> Void
    let apply: () -> Void
    let decline: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Image(systemName: "sparkles").foregroundStyle(.tint)
                Text(model.heading).font(.headline)
            }
            SpriteMotionDetails(model: model)
            if let text = model.previewHint ?? previewNote {
                Text(text).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            SpriteMotionButtons(model: model, preview: preview, endPreview: endPreview, apply: apply, decline: decline)
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.accentColor.opacity(0.25)))
        .accessibilityElement(children: .contain)
    }
}

private struct SpriteMotionDetails: View {
    let model: SpriteMotionReviewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(model.name).font(.callout.weight(.semibold))
            if let note = model.note {
                Text(note).font(.callout).foregroundStyle(.secondary).lineLimit(4)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(model.changesTitle).font(.caption.weight(.semibold)).padding(.top, 2)
            ForEach(Array(model.changes.enumerated()), id: \.offset) { _, line in
                Text(line).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

private struct SpriteMotionButtons: View {
    let model: SpriteMotionReviewModel
    let preview: () -> Void
    let endPreview: () -> Void
    let apply: () -> Void
    let decline: () -> Void

    var body: some View {
        HStack {
            Button(SpriteMotionReviewCopy.decline, action: decline)
            Spacer()
            if model.previewing {
                Button(SpriteMotionReviewCopy.endPreview, action: endPreview)
            } else {
                Button(SpriteMotionReviewCopy.previewOnDesk, action: preview).disabled(model.deskOff)
            }
            Button(SpriteMotionReviewCopy.apply, action: apply).buttonStyle(.borderedProminent)
        }
    }
}

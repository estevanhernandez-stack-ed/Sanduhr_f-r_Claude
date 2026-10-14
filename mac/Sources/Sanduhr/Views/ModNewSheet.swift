import AppKit
import SwiftUI

/// Item 75, slice E: New Mod, from the Catalog. The idea in a sentence, then Copy Prompt puts the
/// Build with Claude prompt on the clipboard. Writes nothing but the clipboard.
struct NewModSheet: View {
    let close: () -> Void
    @State private var idea = ""
    @State private var showPrompt = false
    @State private var copied = false

    private var prompt: String { ModPrompt.text(idea: idea) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("New Mod").font(.title3.weight(.semibold))
            Text("Say what the mod should do in a sentence. Copy Prompt puts a ready prompt on the clipboard: paste it into Claude Code or Claude Desktop, and Claude builds the mod in that session, where you can watch it as it's written.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            TextField("A band that shows my CI runs", text: $idea, axis: .vertical)
                .lineLimit(2...4)
                .textFieldStyle(.roundedBorder)
                .accessibilityLabel("The mod's idea")
                .onChange(of: idea) { copied = false }
            if ModPrompt.idea(idea).count >= ModPrompt.maxIdea {
                Text("The prompt keeps the first \(ModPrompt.maxIdea) characters.")
                    .font(.caption).foregroundStyle(.orange)
            }
            DisclosureGroup("The prompt", isExpanded: $showPrompt) {
                ScrollView {
                    Text(prompt)
                        .font(.caption.monospaced())
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(8)
                }
                .frame(height: 220)
                .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.secondary.opacity(0.08)))
            }
            .font(.callout)
            Text("The prompt names no folder on this Mac and carries none of your settings. Nothing is installed from here: the mod runs only in the session that builds it until you add it to a folder.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                if copied {
                    Label("Copied. Paste it into a Claude Code session.", systemImage: "checkmark.circle.fill")
                        .font(.caption).foregroundStyle(.green)
                }
                Spacer()
                Button("Done") { close() }.keyboardShortcut(.cancelAction)
                Button("Copy Prompt") { copy() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 520)
    }

    private func copy() {
        let board = NSPasteboard.general
        board.clearContents()
        board.setString(prompt, forType: .string)
        copied = true
    }
}

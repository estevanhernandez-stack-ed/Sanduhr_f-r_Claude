import SwiftUI

/// A confirm that asks for a name typed out (Keep and Try on a high-risk mod). A system alert
/// can't color its text, so this sheet stands in for one: the name to type in blue where the eye
/// lands first, the field with the name as its placeholder, and the actions, each enabled once
/// the name matches exactly.
struct TypedNameConfirm: View {
    struct Action: Identifiable {
        let title: String
        let run: () -> Void

        var id: String { title }
    }

    let title: String
    /// What typing allows, as in "To keep it, type …".
    let verb: String
    let name: String
    let message: String
    let actions: [Action]
    let cancel: () -> Void

    @State private var typed = ""
    @FocusState private var focused: Bool

    private var matches: Bool { typed == name }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.headline)
            Text("To \(verb) it, type \(Text(name).foregroundStyle(Color.blue).bold()) below.")
                .fixedSize(horizontal: false, vertical: true)
            Text(message)
                .font(.callout).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            TextField(name, text: $typed)
                .textFieldStyle(.roundedBorder)
                .focused($focused)
                .onSubmit { if matches, actions.count == 1 { actions[0].run() } }
                .accessibilityLabel("Type \(name) to \(verb) it")
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { cancel() }
                    .keyboardShortcut(.cancelAction)
                ForEach(actions) { action in
                    Button(action.title) { action.run() }
                        .disabled(!matches)
                }
            }
        }
        .padding(20)
        .frame(width: 440)
        .onAppear { focused = true }
    }
}

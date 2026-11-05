import AppKit
import SwiftUI

/// Settings, Drawers (docs/mac-drawers-setup-spec.md, section 8): the switch, the Drawers folder
/// and Sort, the rules, the folders to look at, the look and Tidy, recent runs and the uninstall
/// note. The page holds no logic: `DrawersSettingsModel` does it through `DrawerService`.
struct DrawersSettings: View {
    @State private var model = DrawersSettingsModel(service: .shared)

    /// The spec's uninstall note, word for word.
    static let uninstallNote = "Before you remove Sanduhr, turn Drawers off to put your files back. Otherwise they stay in the Drawers folder on your Desktop."

    var body: some View {
        Form {
            DrawersSwitchSection(model: model)
            DrawersRulesSection(model: model)
            DrawersViewSection(model: model)
            DrawersLookSection(model: model)
            DrawersRunsSection(model: model)
            Section {
                Text(Self.uninstallNote)
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .formStyle(.grouped)
    }
}

/// Opens the system's folder picker. Nothing is read until a folder is chosen.
private enum DrawersFolderPicker {
    @MainActor
    static func choose(message: String, prompt: String) -> String? {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        panel.message = message
        panel.prompt = prompt
        return panel.runModal() == .OK ? panel.url?.path : nil
    }
}

/// A line of small grey text under a control.
private struct DrawersNote: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }
}

private struct DrawersSwitchSection: View {
    let model: DrawersSettingsModel

    var body: some View {
        Section {
            Toggle("Drawers on the Desk", isOn: Binding(get: { model.enabled }, set: { model.setEnabled($0) }))
                .settingsAnchor(SettingsAnchor.drawersSwitch)
            if let question = model.offQuestion {
                Text(question).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Put Them Back") { model.confirmOff() }
                    Button("Cancel") { model.cancelOff() }
                    Spacer()
                }
            }
            if model.desktopDenied {
                Text(DrawersSettingsModel.deniedLine).font(.callout.weight(.semibold)).foregroundStyle(.orange)
                DrawersNote(DrawersSettingsModel.deniedDetail)
                HStack {
                    Button(DrawersSettingsModel.deniedButton) { model.openFilesAndFolders() }
                    Spacer()
                }
            }
            DrawersNote("Sorts the top of your Desktop into folders you can see as drawers on the Desk. Nothing moves until you approve a review, and every move can be undone.")
            LabeledContent("Drawers folder", value: model.rootPath)
            HStack {
                if model.canChooseRoot {
                    Button("Choose…") { choose() }
                    Button("Use the Default") { model.useDefaultRoot() }
                }
                Button("Sort my desktop now") { model.sort() }.disabled(!model.enabled)
                Spacer()
            }
            if !model.canChooseRoot { DrawersNote("The folder can't change while drawers exist. Turn Drawers off first.") }
            if let message = model.message { Text(message).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true) }
        }
    }

    private func choose() {
        if let path = DrawersFolderPicker.choose(message: "Choose the folder that holds your drawers.", prompt: "Choose") {
            model.chooseRoot(path)
        }
    }
}

private struct DrawersRulesSection: View {
    let model: DrawersSettingsModel

    var body: some View {
        Section("Sorting rules") {
            ForEach(model.rules) { row in DrawersRuleRowView(model: model, row: row) }
            DrawersDaysRow(model: model)
                .settingsAnchor(SettingsAnchor.drawersRules)
            DrawersAddRule(model: model)
            DrawersNote("Rules run top to bottom and the first match wins. What no rule takes stays on the Desktop.")
        }
    }
}

private struct DrawersRuleRowView: View {
    let model: DrawersSettingsModel
    let row: DrawerRuleRow

    var body: some View {
        HStack {
            Toggle(row.title, isOn: Binding(get: { row.on }, set: { model.setRule(row.id, on: $0) }))
            Spacer()
            Button { model.moveRule(row.id, by: -1) } label: { Image(systemName: "chevron.up") }
                .disabled(!row.canMoveUp).help("Move up").accessibilityLabel("Move \(row.title) up")
            Button { model.moveRule(row.id, by: 1) } label: { Image(systemName: "chevron.down") }
                .disabled(!row.canMoveDown).help("Move down").accessibilityLabel("Move \(row.title) down")
            if row.isCustom {
                Button { model.removeRule(row.id) } label: { Image(systemName: "trash") }
                    .help("Remove").accessibilityLabel("Remove \(row.title)")
            }
        }
        .buttonStyle(.borderless)
    }
}

private struct DrawersDaysRow: View {
    let model: DrawersSettingsModel

    var body: some View {
        let days = Binding(get: { model.olderDays }, set: { model.setOlderDays($0) })
        LabeledContent("Older files, days") {
            HStack {
                TextField("Days", value: days, format: .number).frame(width: 64).multilineTextAlignment(.trailing)
                Stepper("Days", value: days, in: DrawerRules.daysRange)
            }
            // In a Form a field's title shows beside it and wrapped ("Day s"); VoiceOver still reads it.
            .labelsHidden()
        }
    }
}

private struct DrawersAddRule: View {
    let model: DrawersSettingsModel
    @State private var startsWith = false
    @State private var text = ""
    @State private var drawer = ""

    var body: some View {
        HStack {
            Picker("Name", selection: $startsWith) {
                Text("contains").tag(false)
                Text("starts with").tag(true)
            }
            .fixedSize()
            // Titles hidden for the same wrap as the days row; the prompts say what goes in.
            TextField("Words", text: $text, prompt: Text("invoice")).labelsHidden().frame(minWidth: 90)
            TextField("Drawer", text: $drawer, prompt: Text("Drawer name")).labelsHidden().frame(minWidth: 110)
            Button("Add Rule") {
                if model.addCustomRule(startsWith: startsWith, text: text, drawer: drawer) == nil { text = ""; drawer = "" }
            }
            .disabled(text.isEmpty || drawer.isEmpty)
        }
    }
}

private struct DrawersViewSection: View {
    let model: DrawersSettingsModel

    var body: some View {
        Section("Folders to look at") {
            ForEach(model.viewDrawers, id: \.id) { d in
                LabeledContent {
                    Button("Remove") { model.removeViewDrawer(d.id) }
                } label: {
                    VStack(alignment: .leading) {
                        Text(d.name)
                        Text(d.home).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            Button("Add a Folder…") {
                if let p = DrawersFolderPicker.choose(message: "Choose a folder to show as a drawer. Sanduhr never moves its files.", prompt: "Add") {
                    model.addViewDrawer(p)
                }
            }
            .settingsAnchor(SettingsAnchor.drawersView)
            ForEach(model.hiddenDrawers, id: \.id) { d in
                LabeledContent("\(d.name) is hidden") { Button("Show") { model.show(d.id) } }
            }
        }
    }
}

private struct DrawersLookSection: View {
    let model: DrawersSettingsModel

    var body: some View {
        Section("Look") {
            Picker("Look", selection: Binding(get: { model.settings.look }, set: { model.setLook($0) })) {
                ForEach(DrawerLook.allCases, id: \.self) { Text($0.rawValue.capitalized).tag($0) }
            }
            .pickerStyle(.segmented)
            .settingsAnchor(SettingsAnchor.drawersLook)
            LabeledContent("Opacity") {
                HStack {
                    Slider(value: Binding(get: { Double(model.settings.opacity) }, set: { model.setOpacity(Int($0)) }),
                           in: Double(DrawerSettings.opacityRange.lowerBound)...Double(DrawerSettings.opacityRange.upperBound))
                    Text("\(model.settings.opacity)%").monospacedDigit().frame(width: 44, alignment: .trailing)
                }
            }
            Picker("Tidy corner", selection: Binding(get: { model.settings.tidyCorner }, set: { model.setCorner($0) })) {
                Text("Top left").tag(DrawerCorner.topLeft)
                Text("Top right").tag(DrawerCorner.topRight)
                Text("Bottom left").tag(DrawerCorner.bottomLeft)
                Text("Bottom right").tag(DrawerCorner.bottomRight)
            }
            Picker("Tidy direction", selection: Binding(get: { model.settings.tidyFlow }, set: { model.setFlow($0) })) {
                Text("Column").tag(DrawerFlow.column)
                Text("Row").tag(DrawerFlow.row)
            }
            Button("Tidy") { model.tidy() }.disabled(!model.enabled)
        }
    }
}

private struct DrawersRunsSection: View {
    let model: DrawersSettingsModel

    var body: some View {
        Section {
            let runs = model.runs
            if runs.isEmpty { DrawersNote("No runs yet. Sort my desktop now starts one.") }
            ForEach(runs) { run in
                LabeledContent {
                    Button("Undo") { model.undo(run.id) }
                } label: {
                    VStack(alignment: .leading) {
                        Text(run.title)
                        Text(run.at, format: .dateTime.month().day().hour().minute()).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
        } header: {
            Text("Recent runs").settingsAnchor(SettingsAnchor.drawersRuns)
        }
    }
}

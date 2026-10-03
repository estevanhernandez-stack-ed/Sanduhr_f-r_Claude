import SwiftUI
import AppKit
import ServiceManagement

/// The Desk sections of the Settings window (SettingsWindow.swift) and its General section.
/// Every Desk control writes the same com.626labs.sanduhr.desk defaults the desktop reads, so
/// changes show on the desktop as you make them.

// MARK: - Layout

struct DeskLayoutSection: View {
    @AppStorage("layout", store: .desk) private var layout = "message:tl clock:bl claude:bl meetings:bl"
    @AppStorage("left", store: .desk) private var left = 52.0
    @AppStorage("right", store: .desk) private var right = 52.0
    @AppStorage("top", store: .desk) private var top = 40.0
    @AppStorage("bottom", store: .desk) private var bottom = 60.0

    static let slots: [(key: String, name: String)] = [
        ("tl", "Top left"), ("tr", "Top right"), ("bl", "Bottom left"), ("br", "Bottom right"), ("", "Hidden"),
    ]

    var body: some View {
        Form {
            Section("Where each piece sits") {
                ForEach(DeskLayout.widgets, id: \.key) { w in
                    Picker(w.name, selection: slotBinding(w.key)) {
                        ForEach(Self.slots, id: \.key) { Text($0.name).tag($0.key) }
                    }
                }
                Text("Pieces in the same corner stack in this order. The top and bottom of a side share a column, so they never overlap.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Margins") {
                slider("Left", $left, 0...300)
                slider("Right", $right, 0...300)
                slider("Top (below the menu bar)", $top, 0...300)
                slider("Bottom", $bottom, 0...300)
            }
        }
        .formStyle(.grouped)
    }

    private func slotBinding(_ widget: String) -> Binding<String> {
        Binding(
            get: { DeskLayout.parse(layout)[widget] ?? "" },
            set: { layout = DeskLayout.placing(widget, in: $0, layout: layout) })
    }
}

/// The layout string the Layout section edits ("message:tl clock:bl claude:bl meetings:bl"), kept
/// apart from the view so it tests without AppKit. DeskView reads the same string.
enum DeskLayout {
    static let widgets: [(key: String, name: String)] = [
        ("message", "Message"), ("clock", "Clock and date"), ("claude", "Claude line"),
        ("meters", "Claude meters (bars)"), ("meetings", "Meetings"),
    ]

    /// Widget to slot. Words without exactly one colon are skipped; a repeated widget keeps its last slot.
    static func parse(_ s: String) -> [String: String] {
        var out: [String: String] = [:]
        for item in s.split(separator: " ") {
            let bits = item.split(separator: ":").map(String.init)
            if bits.count == 2 { out[bits[0]] = bits[1] }
        }
        return out
    }

    /// The layout with `widget` moved to `slot` ("" hides it), rewritten in the canonical widget
    /// order, which is also the stacking order within a corner.
    static func placing(_ widget: String, in slot: String, layout: String) -> String {
        var map = parse(layout)
        map[widget] = slot.isEmpty ? nil : slot
        return widgets.compactMap { w in map[w.key].map { "\(w.key):\($0)" } }.joined(separator: " ")
    }
}

// MARK: - Look

struct DeskLookSection: View {
    @AppStorage("font", store: .desk) private var font = ""
    @AppStorage("messageFont", store: .desk) private var messageFont = ""
    @AppStorage("timeSize", store: .desk) private var timeSize = 112.0
    @AppStorage("messageSize", store: .desk) private var messageSize = 84.0
    @AppStorage("messageColor", store: .desk) private var messageColor = "9ad7ff"
    @AppStorage("inkColor", store: .desk) private var inkColor = "ffffff"
    @AppStorage("inkShadow", store: .desk) private var inkShadow = true
    @AppStorage("notchTextColor", store: .desk) private var notchTextColor = "ffffff"
    @State private var families: [String] = []

    static let presets: [(name: String, value: String)] = [
        ("Ice gradient", "8f5bd6,3a63e0,33fdff"),
        ("Ice gradient, exact", "531b93,012089,00fdff"),
        ("Sky", "9ad7ff"),
        ("Marquee", "ffd08a"),
        ("White", "ffffff"),
    ]

    var body: some View {
        Form {
            Section("Fonts") {
                Picker("Desk font", selection: $font) {
                    Text("System").tag("")
                    Divider()
                    ForEach(families, id: \.self) { Text($0).tag($0) }
                }
                Picker("Message font", selection: $messageFont) {
                    Text("Same as Desk").tag("")
                    Divider()
                    ForEach(families, id: \.self) { Text($0).tag($0) }
                }
            }
            Section("Sizes") {
                slider("Clock", $timeSize, 48...220)
                slider("Message", $messageSize, 24...180)
            }
            Section("Colors (one hex, or several with commas for a gradient)") {
                ColorRow(title: "Message", value: $messageColor)
                ColorRow(title: "Clock, date, meetings, Claude", value: $inkColor)
                Toggle("Drop shadow under the clock text", isOn: $inkShadow)
                ColorRow(title: "Notch text", value: $notchTextColor)
            }
        }
        .formStyle(.grouped)
        .onAppear {
            families = NSFontManager.shared.availableFontFamilies.sorted {
                $0.localizedCaseInsensitiveCompare($1) == .orderedAscending
            }
            if !font.isEmpty, !families.contains(font) { families.insert(font, at: 0) }
        }
    }
}

private struct ColorRow: View {
    let title: String
    @Binding var value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                Spacer()
                Picker("", selection: $value) {
                    ForEach(DeskLookSection.presets, id: \.value) { Text($0.name).tag($0.value) }
                    if !DeskLookSection.presets.contains(where: { $0.value == value }) {
                        Text("Custom").tag(value)
                    }
                }
                .labelsHidden()
                .frame(width: 170)
            }
            HStack {
                TextField("hex", text: $value)
                    .font(.system(.body, design: .monospaced))
                Swatch(value: value).frame(width: 90, height: 18)
            }
        }
    }
}

private struct Swatch: View {
    let value: String
    var body: some View {
        let colors = value.split(separator: ",").map { Color.hex($0.trimmingCharacters(in: .whitespaces)) }
        RoundedRectangle(cornerRadius: 5)
            .fill(LinearGradient(colors: colors.count > 1 ? colors : [colors.first ?? .white, colors.first ?? .white],
                                 startPoint: .leading, endPoint: .trailing))
    }
}

// MARK: - Message

struct DeskMessageSection: View {
    var model: DeskModel
    @AppStorage("message", store: .desk) private var pinned = ""
    @AppStorage("messageRotate", store: .desk) private var rotate = "daily"
    @State private var text = ""
    @State private var saved = true

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("One line per message. \"Mon: text\" only on Mondays, \"10-31: text\" only on that date, # for notes.")
                .font(.caption).foregroundStyle(.secondary)
            TextEditor(text: $text)
                .font(.system(size: 12, design: .monospaced))
                .frame(minHeight: 170)
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(.quaternary))
                .onChange(of: text) { _, _ in saved = false }
            HStack {
                Button("Save") { save() }
                    .keyboardShortcut("s", modifiers: .command)
                    .disabled(saved)
                Text(saved ? "Today: \(MessageEngine.current() ?? "nothing")" : "Unsaved")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Spacer()
            }
            Picker("Change", selection: $rotate) {
                Text("Once a day").tag("daily")
                Text("Every hour").tag("hourly")
            }
            .pickerStyle(.segmented)
            TextField("Pin one line instead (leave empty to use the list)", text: $pinned)
                .onChange(of: pinned) { _, _ in model.message = MessageEngine.current() }
        }
        .padding(.top, 8)
        .onAppear {
            MessageEngine.ensureFile()
            text = (try? String(contentsOf: MessageEngine.fileURL, encoding: .utf8)) ?? ""
            saved = true
        }
        .onChange(of: rotate) { _, _ in model.message = MessageEngine.current() }
    }

    private func save() {
        try? text.write(to: MessageEngine.fileURL, atomically: true, encoding: .utf8)
        model.message = MessageEngine.current()
        saved = true
    }
}

// MARK: - Notch

struct DeskNotchSection: View {
    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchChin", store: .desk) private var chin = 26.0
    @AppStorage("notchText", store: .desk) private var wingText = true
    @AppStorage("notchChinText", store: .desk) private var chinText = false
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var left = NotchContent.Place.left.fallback
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var right = NotchContent.Place.right.fallback
    @AppStorage(NotchContent.Place.strip.key, store: .desk) private var strip = NotchContent.Place.strip.fallback
    @AppStorage(CameraLightController.enabledKey, store: .desk) private var cameraLight = false
    @AppStorage(CameraLightController.brightnessKey, store: .desk) private var lightBrightness = CameraLightController.defaultBrightness
    @AppStorage(CameraLightController.sizeKey, store: .desk) private var lightSize = CameraLightController.defaultSize

    var body: some View {
        Form {
            Section {
                Toggle("Extend the camera notch", isOn: $enabled)
                Text("Widens the notch into one black island while Desk is on. Click it to open these settings. Screens without a notch are left alone.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Text") {
                Toggle("Text beside the camera", isOn: $wingText)
                contentPicker("Left wing", $left).disabled(!wingText)
                contentPicker("Right wing", $right).disabled(!wingText)
                Toggle("Text under the camera too (desktop only)", isOn: $chinText)
                    .disabled(chin == 0)
                contentPicker("Under the camera", $strip).disabled(!chinText || chin == 0)
                Text("Nothing leaves that part plain black. A wing grows to fit its text.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            .disabled(!enabled)
            Section("Size") {
                slider("Extra width each side", $wings, 0...180)
                slider("Extra height below (0 = none)", $chin, 0...56)
            }
            .disabled(!enabled)
            Section("Camera light") {
                Toggle("Light up for the camera", isOn: $cameraLight)
                    .onChange(of: cameraLight) { _, _ in CameraLightController.shared.apply() }
                HStack {
                    Text("Brightness")
                    Slider(value: $lightBrightness, in: CameraLightLayout.brightnessRange)
                    Text("\(Int((lightBrightness * 100).rounded()))%")
                        .font(.system(.body, design: .monospaced))
                        .frame(width: 48, alignment: .trailing)
                }
                slider("Reach below the menu bar", $lightSize, CameraLightLayout.sizeRange)
                Text("While any app uses a camera, a soft white light around the notch lights your face, above every app. It ends when the camera stops, with or without Desk or the island. Tools, Camera Light shows it by hand. Screens without a notch get it at the top center.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func contentPicker(_ title: String, _ selection: Binding<NotchContent>) -> some View {
        Picker(title, selection: selection) {
            ForEach(NotchContent.allCases) { Text($0.label).tag($0) }
        }
    }
}

// MARK: - General

/// Which surfaces show, open at login, the shortcuts. The Widget switch mirrors panelHidden,
/// which AppDelegate writes whenever the widget shows or hides.
struct GeneralSection: View {
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskEnabled = false
    @AppStorage(DeskController.notchKey, store: .desk) private var notch = false
    @AppStorage(AppDelegate.panelHiddenKey) private var panelHidden = false
    @AppStorage("menuIcon", store: .desk) private var menuIcon = false
    @AppStorage("showMeetings", store: .desk) private var showMeetings = true
    @AppStorage("showClaude", store: .desk) private var showClaude = true
    @AppStorage(DeskController.hotKeysKey, store: .desk) private var hotKeys = true
    @State private var atLogin = SMAppService.mainApp.status == .enabled

    var body: some View {
        Form {
            Section("Surfaces") {
                Toggle("Desk: clock, meters, meetings and the message on the desktop", isOn: $deskEnabled)
                    .onChange(of: deskEnabled) { _, _ in DeskController.shared.apply() }
                Toggle("Notch: the island around the camera (needs Desk)", isOn: $notch)
                Toggle("Widget: the floating window with the tools", isOn: Binding(
                    get: { !panelHidden },
                    set: { show in
                        let app = NSApp.delegate as? AppDelegate
                        if show { app?.showPanel() } else { app?.hidePanel() }
                    }))
                Text("Sanduhr keeps fetching and alerting with every surface off. Every setting stays here, and Option+S opens this window while Desk is on.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Toggle("Open Sanduhr at login", isOn: $atLogin)
                    .onChange(of: atLogin) { _, on in
                        do { if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() } }
                        catch { atLogin = SMAppService.mainApp.status == .enabled }
                    }
                Toggle("Desk menu in the menu bar (meetings, join, settings)", isOn: $menuIcon)
                    .onChange(of: menuIcon) { _, on in DeskController.shared.setMenuIcon(on) }
            }
            Section("Calendar and Claude") {
                Toggle("Read today's meetings", isOn: $showMeetings)
                    .onChange(of: showMeetings) { _, on in
                        let desk = DeskController.shared
                        if on, desk.running { desk.model.requestCalendar() }
                        if !on { desk.model.meetings = [] }
                    }
                Toggle("Show the Claude meters on the desktop", isOn: $showClaude)
            }
            Section("Shortcuts") {
                Toggle("Option+J joins the next meeting, Option+S opens these settings", isOn: $hotKeys)
                    .onChange(of: hotKeys) { _, _ in DeskController.shared.applyHotKeys() }
                Text("Work in every app while Desk is on. While they are on, Option+J and Option+S no longer type ∆ and ß.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                HStack {
                    Button("Quit Sanduhr") { NSApp.terminate(nil) }
                    Spacer()
                    Text("Sanduhr \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Shared

private func slider(_ title: String, _ value: Binding<Double>, _ range: ClosedRange<Double>) -> some View {
    HStack {
        Text(title)
        Slider(value: value, in: range, step: 1)
        Text("\(Int(value.wrappedValue))")
            .font(.system(.body, design: .monospaced))
            .frame(width: 40, alignment: .trailing)
    }
}

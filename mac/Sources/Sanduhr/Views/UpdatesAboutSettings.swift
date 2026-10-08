import AppKit
import SwiftUI

/// Settings, Sanduhr, Updates: the installed version, Sparkle's last check and Check for Updates…, and
/// Sparkle's own two switches (the menus' Check for Updates… uses the same updater).
struct UpdatesSection: View {
    var updates: UpdaterSettings
    private let info = AppInfo.current

    var body: some View {
        Form {
            Section {
                LabeledContent("Installed", value: info.versionAndBuild)
                    .settingsAnchor(SettingsAnchor.check)
                LabeledContent("Last checked", value: UpdateCheckText.lastChecked(updates.lastCheck))
                HStack {
                    Button(SettingsNames.checkForUpdates) { updates.checkNow() }
                        .disabled(!updates.canCheck)
                    Spacer()
                    Link("Release notes for this version",
                         destination: AboutLinks.releaseNotes(version: info.version))
                }
            }
            Section("Automatic updates") {
                Toggle("Check for updates automatically", isOn: Binding(
                    get: { updates.checksAutomatically },
                    set: { updates.setChecksAutomatically($0) }))
                    .settingsAnchor(SettingsAnchor.automatic)
                Toggle("Download and install updates automatically", isOn: Binding(
                    get: { updates.downloadsAutomatically },
                    set: { updates.setDownloadsAutomatically($0) }))
                    .disabled(!updates.checksAutomatically || !updates.allowsAutomaticDownloads)
                Text("Sanduhr checks once a day. With automatic install on, an update downloads in the background and installs when Sanduhr quits; otherwise Sanduhr asks first. Check for Updates… in any Sanduhr menu uses the same settings.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        // The last-check date posts no change notice; read it again whenever the page shows.
        .onAppear { updates.sync() }
    }
}

/// Settings, Sanduhr, About: who made it, which version, what's new, where to read more.
struct AboutSection: View {
    private let info = AppInfo.current

    var body: some View {
        Form {
            Section {
                HStack(alignment: .center, spacing: 16) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable()
                        .frame(width: 64, height: 64)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(AppInfo.name).font(.title2.bold())
                        Text(info.versionLine).foregroundStyle(.secondary)
                        Text(AppInfo.tagline)
                    }
                }
                .padding(.vertical, 4)
                .settingsAnchor(SettingsAnchor.about)
                Text(AppInfo.independence)
                    .font(.callout).foregroundStyle(.secondary)
                // Item 57: the release highlights, any time, whatever Don't show after updates says.
                // Item 61: the welcome tour, any time, with the current settings.
                HStack {
                    Button("What's New…") { WhatsNewWindowController.shared.show() }
                        .help("What arrived in this version and the ones before it")
                    Button("Take the Tour…") { WelcomeTourWindowController.shared.show() }
                        .help("The welcome tour: the meters, the Desk, the menu bar, accounts and Claude Code")
                }
            }
            Section("Links") {
                HStack {
                    ForEach(AboutLinks.all(version: info.version)) { link in
                        Button(link.title) { NSWorkspace.shared.open(link.url) }
                            .help(link.url.absoluteString)
                    }
                }
                .settingsAnchor(SettingsAnchor.links)
            }
            Section {
                Link("Updates by Sparkle", destination: AboutLinks.sparkle)
                    .settingsAnchor(SettingsAnchor.notices)
                Link("Now playing uses mediaremote-adapter by Jonas van den Berg (BSD-3-Clause)",
                     destination: AboutLinks.mediaRemoteAdapter)
                Text(AppInfo.fontCredit)
                if let notices = AboutLinks.thirdPartyNotices() {
                    Button("Third-Party Notices") { NSWorkspace.shared.open(notices) }
                        .help("The licenses of Sparkle, mediaremote-adapter, EsteFont Pro and EsteFont 26")
                }
                if !info.copyright.isEmpty {
                    Text(info.copyright).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
        .formStyle(.grouped)
    }
}

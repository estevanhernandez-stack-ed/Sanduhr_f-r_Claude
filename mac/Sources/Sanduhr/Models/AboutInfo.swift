import Foundation

/// What Settings, About and Updates say about the installed app, read from an Info.plist
/// dictionary so tests can hand in their own.
struct AppInfo: Equatable {
    let version: String
    let build: String
    let copyright: String

    init(info: [String: Any]) {
        version = info["CFBundleShortVersionString"] as? String ?? "unknown"
        build = info["CFBundleVersion"] as? String ?? "unknown"
        copyright = info["NSHumanReadableCopyright"] as? String ?? ""
    }

    static var current: AppInfo { AppInfo(info: Bundle.main.infoDictionary ?? [:]) }

    /// "2.3.0 (build 6)".
    var versionAndBuild: String { "\(version) (build \(build))" }
    /// "Version 2.3.0 (build 6)".
    var versionLine: String { "Version \(versionAndBuild)" }

    static let name = "Sanduhr für Claude"
    static let tagline = "Pacing for your Claude subscription: the widget, Desk and the notch."
    /// Store and trademark requirement: on every surface that names Claude.
    static let independence = "Independent third-party tool. Not affiliated with Anthropic. Requires an active Claude Pro / Team / Enterprise subscription."
}

/// A link button on Settings, About.
struct AboutLink: Equatable, Identifiable {
    let title: String
    let url: URL
    var id: String { title }
}

/// The pages Settings, About and Updates link to.
enum AboutLinks {
    static let repo = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude"
    static let website = URL(string: "https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/")!
    static let github = URL(string: repo)!
    static let releases = URL(string: repo + "/releases")!
    static let privacy = URL(string: repo + "/blob/main/docs/PRIVACY.md")!
    /// The MIT license file at the repo root.
    static let license = URL(string: repo + "/blob/main/LICENSE")!
    static let sparkle = URL(string: "https://sparkle-project.org")!

    /// The release page of a Mac version (tagged `v<version>-mac`); the releases list when the
    /// version is unknown or would not make a URL.
    static func releaseNotes(version: String) -> URL {
        let v = version.trimmingCharacters(in: .whitespaces)
        guard !v.isEmpty, v != "unknown",
              v.allSatisfy({ $0.isLetter || $0.isNumber || $0 == "." || $0 == "-" }),
              let url = URL(string: repo + "/releases/tag/v" + v + "-mac") else { return releases }
        return url
    }

    /// About's link buttons, in order.
    static func all(version: String) -> [AboutLink] {
        [
            AboutLink(title: "Website", url: website),
            AboutLink(title: "GitHub", url: github),
            AboutLink(title: "Release Notes", url: releaseNotes(version: version)),
            AboutLink(title: "Privacy", url: privacy),
            AboutLink(title: "License", url: license),
        ]
    }
}

/// Settings, Updates: the last check time, or "Never".
enum UpdateCheckText {
    static func lastChecked(_ date: Date?) -> String {
        guard let date else { return "Never" }
        return date.formatted(date: .abbreviated, time: .shortened)
    }
}

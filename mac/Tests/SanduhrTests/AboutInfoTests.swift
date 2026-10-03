import Foundation
import Testing
@testable import Sanduhr

/// Settings, About and Updates: the link set, the version-specific release notes, the version
/// lines read from an Info.plist dictionary.
@Suite("About links")
struct AboutLinksTests {
    let repo = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude"

    @Test func linkButtonsInOrder() {
        let links = AboutLinks.all(version: "2.3.0")
        #expect(links.map(\.title) == ["Website", "GitHub", "Release Notes", "Privacy", "License"])
        let urls = links.map(\.url.absoluteString)
        #expect(urls == [
            "https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/",
            repo,
            repo + "/releases/tag/v2.3.0-mac",
            repo + "/blob/main/docs/PRIVACY.md",
            repo + "/blob/main/LICENSE",
        ])
    }

    @Test func releaseNotesFollowTheVersion() {
        #expect(AboutLinks.releaseNotes(version: "2.4.1").absoluteString == repo + "/releases/tag/v2.4.1-mac")
        #expect(AboutLinks.releaseNotes(version: " 2.4.0-beta1 ").absoluteString == repo + "/releases/tag/v2.4.0-beta1-mac")
    }

    @Test func unknownVersionFallsBackToTheReleasesList() {
        for version in ["", "unknown", "2.3 0", "2.3/0"] {
            #expect(AboutLinks.releaseNotes(version: version).absoluteString == repo + "/releases")
        }
    }

    @Test func sparkleCredit() {
        #expect(AboutLinks.sparkle.absoluteString == "https://sparkle-project.org")
    }
}

@Suite("App info")
struct AppInfoTests {
    @Test func versionLinesFromInfoPlist() {
        let info = AppInfo(info: ["CFBundleShortVersionString": "2.3.0", "CFBundleVersion": "6",
                                  "NSHumanReadableCopyright": "MIT License. 626Labs."])
        #expect(info.versionAndBuild == "2.3.0 (build 6)")
        #expect(info.versionLine == "Version 2.3.0 (build 6)")
        #expect(info.copyright == "MIT License. 626Labs.")
    }

    @Test func missingKeys() {
        let info = AppInfo(info: [:])
        #expect(info.versionLine == "Version unknown (build unknown)")
        #expect(info.copyright.isEmpty)
        #expect(AboutLinks.releaseNotes(version: info.version) == AboutLinks.releases)
    }

    @Test func independenceLine() {
        #expect(AppInfo.independence.contains("Not affiliated with Anthropic"))
        #expect(AppInfo.independence.hasPrefix("Independent third-party tool."))
    }

    @Test func neverCheckedSaysNever() {
        #expect(UpdateCheckText.lastChecked(nil) == "Never")
        #expect(UpdateCheckText.lastChecked(Date(timeIntervalSince1970: 0)) != "Never")
    }
}

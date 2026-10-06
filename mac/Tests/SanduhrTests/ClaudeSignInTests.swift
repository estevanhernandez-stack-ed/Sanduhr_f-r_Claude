import Foundation
import Testing
@testable import Sanduhr

/// The embedded sign-in's decisions (item 62): when a session counts as captured, which hosts are
/// claude.ai's, when the Google notice shows, and which clicked links leave the window. Made-up
/// values only.
@Suite("Embedded sign-in")
struct ClaudeSignInTests {
    private func cookie(_ name: String, _ value: String, _ domain: String = ".claude.ai") -> ClaudeSignIn.Cookie {
        .init(name: name, value: value, domain: domain)
    }

    @Test func noSessionUntilTheKeyArrives() {
        #expect(ClaudeSignIn.capture([]) == nil)
        #expect(ClaudeSignIn.capture([cookie("cf_clearance", "cf-1"), cookie("anthropic-device-id", "d")]) == nil)
        #expect(ClaudeSignIn.capture([cookie("sessionKey", "   ")]) == nil)
    }

    @Test func theKeyAndClearanceAreCaptured() {
        let got = ClaudeSignIn.capture([cookie("sessionKey", " sk-ant-sid01-test "), cookie("cf_clearance", "cf-1")])
        #expect(got == .init(sessionKey: "sk-ant-sid01-test", cfClearance: "cf-1"))
        #expect(ClaudeSignIn.capture([cookie("sessionKey", "k")])?.cfClearance == nil)
    }

    @Test func onlyClaudeCookiesCount() {
        #expect(ClaudeSignIn.capture([cookie("sessionKey", "k", ".example.com")]) == nil)
        #expect(ClaudeSignIn.capture([cookie("sessionKey", "k", "notclaude.ai")]) == nil)
        #expect(ClaudeSignIn.capture([cookie("sessionKey", "k", "claude.ai")])?.sessionKey == "k")
    }

    @Test func theFirstNonEmptyDuplicateWins() {
        let got = ClaudeSignIn.capture([cookie("sessionKey", "", "claude.ai"), cookie("sessionKey", "real")])
        #expect(got?.sessionKey == "real")
    }

    @Test func claudeHosts() {
        #expect(ClaudeSignIn.isClaudeHost("claude.ai"))
        #expect(ClaudeSignIn.isClaudeHost(".claude.ai"))
        #expect(ClaudeSignIn.isClaudeHost("api.CLAUDE.ai"))
        #expect(!ClaudeSignIn.isClaudeHost("claude.ai.example.com"))
        #expect(!ClaudeSignIn.isClaudeHost("fakeclaude.ai"))
        #expect(!ClaudeSignIn.isClaudeHost(nil))
        #expect(!ClaudeSignIn.isClaudeHost(""))
    }

    @Test func theGoogleNoticeShowsOnGooglesSignInOnly() {
        #expect(ClaudeSignIn.isGoogleSignIn(URL(string: "https://accounts.google.com/v3/signin/identifier")))
        #expect(ClaudeSignIn.isGoogleSignIn(URL(string: "https://accounts.youtube.com/accounts/SetSID")))
        #expect(!ClaudeSignIn.isGoogleSignIn(URL(string: "https://claude.ai/login")))
        #expect(!ClaudeSignIn.isGoogleSignIn(URL(string: "https://www.google.com/search")))
        #expect(!ClaudeSignIn.isGoogleSignIn(nil))
    }

    @Test func clickedLinksStayOnlyOnSignInHosts() {
        for s in ["https://claude.ai/login", "https://console.anthropic.com/x", "https://accounts.google.com/o",
                  "https://challenges.cloudflare.com/c", "https://appleid.apple.com/auth", "about:blank"] {
            #expect(ClaudeSignIn.staysInWindow(URL(string: s)), "\(s)")
        }
        for s in ["https://docs.example.com/help", "http://claude.ai/login", "mailto:support@example.com"] {
            #expect(!ClaudeSignIn.staysInWindow(URL(string: s)), "\(s)")
        }
    }

    @Test func theWindowPresentsItselfAsSafari() {
        #expect(ClaudeSignIn.applicationName(safariVersion: "26.6.2") == "Version/26.6.2 Safari/605.1.15")
        #expect(ClaudeSignIn.applicationName(safariVersion: nil) == "Version/18.0 Safari/605.1.15")
        #expect(ClaudeSignIn.applicationName(safariVersion: " ") == "Version/18.0 Safari/605.1.15")
        #expect(ClaudeSignIn.applicationName(safariVersion: "26 beta") == "Version/18.0 Safari/605.1.15")
    }
}

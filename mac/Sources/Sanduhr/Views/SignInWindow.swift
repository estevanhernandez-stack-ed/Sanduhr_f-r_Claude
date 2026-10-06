import AppKit
import SwiftUI
import WebKit

/// How a sign-in window ended.
enum SignInResult: Equatable {
    /// claude.ai set a session: save it to the account and drop it.
    case signedIn(ClaudeSignIn.Captured)
    /// The person chose to paste a key instead.
    case pasteInstead
    /// Closed without signing in.
    case cancelled
}

/// The embedded "Sign in to Claude" window (item 62), Windows' SignInWindow on WebKit.
///
/// claude.ai's real login (email code, Google, Apple) in a `WKWebView` whose cookie store is
/// non-persistent: it starts empty and is gone when the window closes, so nothing from the web
/// view stays on this Mac and the only thing kept is the captured key, which the caller saves to
/// the account's Keychain slot. The view presents itself as the Safari it is (a Chrome user agent
/// failed Cloudflare's human check), so its `cf_clearance` is bound to Safari and is not kept. No
/// key, cookie value or page content is logged.
///
/// Google refuses sign-in inside an app's web view; when the view lands on Google's sign-in, a
/// notice offers the way through: back to the sign-in choices, then Continue with email and the
/// emailed code (a Claude account made with Google signs in by email too). Paste stays as the
/// way out at the bottom of the window.
@MainActor
final class SignInWindowController: NSObject, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate {
    static let shared = SignInWindowController()

    private var window: NSWindow?
    private var webView: WKWebView?
    private var poll: Timer?
    private var loadTimer: Timer?
    private var continuation: CheckedContinuation<SignInResult, Never>?
    private let model = SignInModel()
    private var firstLoadDone = false

    /// The window is up.
    var isOpen: Bool { window != nil }

    /// Shows the window and waits for it to end. `account` names the account in the title
    /// ("Sign in to Claude: Work"); nil for a first or new account. One window at a time: a second
    /// call brings the open one forward and returns `.cancelled`.
    func run(account: String? = nil) async -> SignInResult {
        if let window {
            NSApp.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
            return .cancelled
        }
        return await withCheckedContinuation { c in
            continuation = c
            open(account: account)
        }
    }

    private func open(account: String?) {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        config.applicationNameForUserAgent = ClaudeSignIn.applicationName(
            safariVersion: ClaudeSignIn.installedSafariVersion())
        let web = WKWebView(frame: .zero, configuration: config)
        web.navigationDelegate = self
        web.uiDelegate = self
        web.setAccessibilityLabel("claude.ai sign-in")
        webView = web

        model.reset()
        firstLoadDone = false
        let chrome = SignInChrome(model: model, webView: web,
                                  onBack: { [weak self] in self?.backToChoices() },
                                  onPaste: { [weak self] in self?.finish(.pasteInstead) },
                                  onRetry: { [weak self] in self?.retry() })
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 500, height: 720),
                         styleMask: [.titled, .closable, .resizable, .miniaturizable],
                         backing: .buffered, defer: false)
        w.title = account.map { "Sign in to Claude: \($0)" } ?? "Sign in to Claude"
        w.isReleasedWhenClosed = false
        w.minSize = NSSize(width: 420, height: 560)
        w.contentView = NSHostingView(rootView: chrome)
        w.delegate = self
        w.center()
        window = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)

        load()
    }

    private func load() {
        webView?.load(URLRequest(url: ClaudeSignIn.loginURL))
        poll?.invalidate()
        poll = Timer.scheduledTimer(withTimeInterval: ClaudeSignIn.pollInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkCookies() }
        }
        loadTimer?.invalidate()
        loadTimer = Timer.scheduledTimer(withTimeInterval: ClaudeSignIn.loadTimeout, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, !self.firstLoadDone else { return }
                self.model.error = "claude.ai didn't load in time. Check your connection and try again."
            }
        }
    }

    private func retry() {
        model.error = nil
        model.loading = true
        load()
    }

    private func backToChoices() {
        webView?.load(URLRequest(url: ClaudeSignIn.loginURL))
    }

    /// Looks in the window's cookie store for a session. Runs on every navigation and the poll.
    private func checkCookies() {
        guard let store = webView?.configuration.websiteDataStore.httpCookieStore else { return }
        store.getAllCookies { [weak self] cookies in
            let mapped = cookies.map { ClaudeSignIn.Cookie(name: $0.name, value: $0.value, domain: $0.domain) }
            guard let captured = ClaudeSignIn.capture(mapped) else { return }
            // The key only: this window's cf_clearance is bound to Safari, not the API's agent.
            let key = ClaudeSignIn.Captured(sessionKey: captured.sessionKey, cfClearance: nil)
            MainActor.assumeIsolated { self?.finish(.signedIn(key)) }
        }
    }

    /// Ends the sign-in once: hands the result back, stops every timer, drops the web view (and
    /// with it the non-persistent cookie store) and closes the window.
    private func finish(_ result: SignInResult) {
        guard let c = continuation else { return }
        continuation = nil
        poll?.invalidate(); poll = nil
        loadTimer?.invalidate(); loadTimer = nil
        webView?.stopLoading()
        webView?.navigationDelegate = nil
        webView?.uiDelegate = nil
        webView = nil
        let w = window
        window = nil
        w?.delegate = nil
        w?.close()
        c.resume(returning: result)
    }

    // MARK: NSWindowDelegate

    func windowWillClose(_ notification: Notification) {
        finish(.cancelled)
    }

    // MARK: WKNavigationDelegate

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        if !firstLoadDone {
            firstLoadDone = true
            model.loading = false
        }
        pageChanged(webView.url)
    }

    func webView(_ webView: WKWebView, didCommit navigation: WKNavigation!) {
        pageChanged(webView.url)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!,
                 withError error: Error) {
        guard !firstLoadDone, (error as NSError).code != NSURLErrorCancelled else { return }
        model.loading = false
        model.error = "claude.ai didn't load. Check your connection and try again."
    }

    func webView(_ webView: WKWebView,
                 decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
        // A link the person clicks to somewhere other than a sign-in host (docs, a help article)
        // opens in the browser; redirects and frames always load, so the login flow never breaks.
        if navigationAction.navigationType == .linkActivated,
           navigationAction.targetFrame?.isMainFrame ?? true,
           let url = navigationAction.request.url, !ClaudeSignIn.staysInWindow(url) {
            NSWorkspace.shared.open(url)
            return .cancel
        }
        return .allow
    }

    private func pageChanged(_ url: URL?) {
        model.onGoogle = ClaudeSignIn.isGoogleSignIn(url)
        checkCookies()
    }

    // MARK: WKUIDelegate

    /// A sign-in popup (`window.open`) loads in this window instead of dead-ending.
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url { webView.load(URLRequest(url: url)) }
        return nil
    }
}

/// What the window's own parts show around the web view.
@MainActor
@Observable
final class SignInModel {
    var loading = true
    var error: String?
    var onGoogle = false

    func reset() {
        loading = true
        error = nil
        onGoogle = false
    }
}

/// The window: the Google notice above the page, the page, and the paste way out below.
private struct SignInChrome: View {
    var model: SignInModel
    let webView: WKWebView
    var onBack: () -> Void
    var onPaste: () -> Void
    var onRetry: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            if model.onGoogle { googleNotice }
            ZStack {
                WebViewHost(webView: webView)
                    .opacity(model.error == nil ? 1 : 0)
                if let error = model.error {
                    errorPanel(error)
                } else if model.loading {
                    ProgressView("Loading claude.ai…")
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(.background)
                }
            }
            Divider()
            HStack(spacing: 8) {
                Text("Sanduhr keeps only the session key, in your Keychain.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Paste a Key Instead", action: onPaste)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
        }
    }

    private var googleNotice: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Google sign-in doesn't work inside apps")
                .font(.callout.weight(.semibold))
            Text("Go back and choose **Continue with email**, using the email of your Google account. Claude sends a code; type it here.")
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Button("Back to Sign-In Choices", action: onBack)
                    .keyboardShortcut(.defaultAction)
                Button("Paste a Key Instead", action: onPaste)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.accentColor.opacity(0.12))
        .accessibilityElement(children: .contain)
    }

    private func errorPanel(_ message: String) -> some View {
        VStack(spacing: 12) {
            Text(message)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Button("Try Again", action: onRetry)
                    .keyboardShortcut(.defaultAction)
                Button("Paste a Key Instead", action: onPaste)
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

private struct WebViewHost: NSViewRepresentable {
    let webView: WKWebView
    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ nsView: WKWebView, context: Context) {}
}

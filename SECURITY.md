# Security Policy

## No data ever comes back to 626 Labs

Before anything else: **Sanduhr does not phone home.** There is no telemetry, no
analytics, no crash reporter, no usage metrics, no "anonymous diagnostics"
toggle buried in a settings pane. 626 Labs has no server, no pipeline, and no
way to receive data from the app even if we wanted one.

The app makes exactly one kind of outbound network request: HTTPS to
`claude.ai`, using **your own session cookie**, to read **your own** usage
numbers from the same endpoints the claude.ai settings page already calls.

Your session cookie is stored in your platform's native secure credential
store (Windows Credential Manager or macOS Keychain). It never touches a
plaintext file on disk, never goes over the network to anyone except
`claude.ai`, and is wiped when you uninstall.

## Reporting a vulnerability

If you find a security issue, please **do not** open a public GitHub issue.

**Preferred channel — GitHub private vulnerability reporting:**
[github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/security/advisories/new](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/security/advisories/new)

This creates a private advisory visible only to repository maintainers.

**What to include:**

- A description of the issue and why you think it's a security problem
- Reproduction steps or a proof-of-concept if you have one
- The affected build / version (Mac DMG version, Windows installer version, or `sanduhr.py` commit hash)
- Your OS and version

**What to expect back:**

- Initial acknowledgement within **5 business days**
- A fix or an explanation (with timeline) within **30 days** for confirmed issues
- Credit in the release notes for the fix, if you want it (anonymous is fine too)

## In scope

Security reports against:

- The Sanduhr desktop app — macOS (SwiftUI), Windows (PySide6), or the root `sanduhr.py`
- How the app stores, handles, or transmits your `sessionKey` cookie
- The auto-update mechanism (Sparkle on macOS, MSIX on Windows)
- The installer / uninstaller on either platform
- Any theme-loading path (user-authored JSON themes)

## Out of scope

- **claude.ai itself.** Report those to Anthropic via
  [anthropic.com/security](https://www.anthropic.com/security).
- Issues in your operating system, your browser, or how you obtained your
  session cookie.
- Issues in third-party dependencies without a demonstrated exploitation path
  against Sanduhr specifically.
- Social-engineering attacks that require the user to paste attacker-controlled
  content into Sanduhr's credential field.

## Trademark note

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively
to describe the integration. Sanduhr für Claude is an independent third-party
tool and is not affiliated with, endorsed by, or sponsored by Anthropic.

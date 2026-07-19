# Email-code sign-in guidance for Google users (design)

Approved direction 2026-07-19 ("Now we just need to explain that clearly to Google users"),
from the usage-API audit's option C. **Owner-verified live the same night:** a Google-created
claude.ai account accepted "Continue with email" with zero unlink steps, the code arrived by
email, and the code entry rendered and worked inside Sanduhr's own sign-in WebView2 — no
second device required. The flow is first-party claude.ai end to end; Google's
embedded-webview OAuth block never fires because Google is never contacted.

## Why

Google-account users currently dead-end at the Google-OAuth bounce and get routed to the
manual sessionKey paste — the clunkiest flow in the app (DevTools, cookie hunt). The verified
email-code path makes sign-in a normal in-window experience for them. The audit's research
(adversarially verified) closed every alternative: cookie recovery is infostealer territory,
UA spoofing violates Google ToS, loopback OAuth requires being Anthropic, passkeys die before
the challenge. Email-code is the whole answer, and it already works — this wave just says so,
clearly, at the right moments.

## 1. The Google-bounce banner becomes the email-code walkthrough

`SignInWindow.xaml` `GoogleNotice` (~lines 58-72) + code-behind:

- Headline: `Google sign-in won't load here — use email login instead`
- Body copy (replaces the current single sentence): `Google blocks sign-in inside embedded
  windows. Instead, go back and choose "Continue with email" with your Gmail address —
  Anthropic emails you a sign-in code. Type that code right here and you're in. Same
  account, nothing changes.` (Owner copy direction 2026-07-19: say it's email login with an
  Anthropic-provided code from the email — users should know the code comes from Anthropic,
  not from Sanduhr.)
- Primary button: `Back to Claude sign-in` → new handler `OnGoogleNoticeBackClick` that
  navigates the WebView to `https://claude.ai/login` (the chooser where "Continue with email"
  lives). The banner then auto-hides via the existing `UpdateOAuthNotice` host check.
- Secondary, link-styled: `paste a session key by hand instead` → the existing
  `OnGoogleNoticePasteClick` (`SignInResult.UseManual`) — the paste path stays reachable,
  demoted from primary button to last-resort link.
- The current copy's "or a passkey" suggestion is dropped: unverified path; the banner
  recommends only what the owner proved live.

## 2. Proactive line in the sign-in intro

The header copy block (~lines 38-47) gains one sentence so Google users never hit the wall at
all: `Signed up with Google? Use email login instead — choose "Continue with email" and enter
the sign-in code Anthropic emails you. It works right here.` Same secondary ink as the
surrounding helper text.

## Unchanged

- Bounce detection mechanics (`UpdateOAuthNotice` host check) — works, stays.
- `ManualKeyWindow` — already neutral power-user framing, no Google references.
- `SignInCoordinator`, capture, storage — zero data-flow changes. PRIVACY.md untouched.

## Rider for the next release cut (not this wave's code)

Store listing "WHAT YOU NEED": the Google parenthetical updates from the session-key-paste
walkthrough to the email-code path. Reviewer letter: no disclosure-surface change (sign-in
data flow identical).

## Testing

App-layer copy + one navigation handler — no unit tests by design; Core untouched so the
suite is a pure regression gate. Smoke: (1) reach a Google auth host in the sign-in window →
banner shows the walkthrough; "Back to Claude sign-in" lands on the login chooser and the
banner hides; (2) full email-code sign-in completes in-window on a Google-created account
(owner re-run of tonight's live check on the built UI); (3) the paste link still opens the
manual key window.

## Effort

XS. One XAML banner + one intro sentence + one navigation handler.

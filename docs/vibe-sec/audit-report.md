# Vibe Sec — audit report

> Generated 2026-10-17T04:58:15.538Z

**Tier:** public-facing (OWASP ASVS L2)
**Pass bar:** 70%
**Weighted score:** 80%
**Gate:** PASS
**Tools of record:** in-house, npm-audit

## Band 1 — action needed now

_Critical and High findings in concerns that are in scope at this tier._

- **CRITICAL** **tinypool@<=2.1.1 — GHSA-5gmw-xhrv-c9v3** _(via npm-audit)_
  Tinypool: Prototype Pollution gadget in worker options leads to Remote Code Execution
- **CRITICAL** **vitest@0.0.95 - 4.1.10 — GHSA-82fw-gwwq-j7x9** _(via npm-audit)_
  Vitest: Path Traversal / Arbitrary File Read via @vitest/mocker Redirect Mock
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:468`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:492`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:493`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1473`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1473`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1476`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1476`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1476`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1493`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1510`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigProposalTests.o:265`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigProposalTests.o:854`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ModMarketplaceTests.o:388`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ModMarketplaceTests.o:1042`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:17`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:73`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:81`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:150`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigProposalTests.swift:50`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ModMarketplaceTests.swift:171`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `windows-dotnet/tests/Sanduhr.Tests/ConfigMaskTests.cs:92`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `windows-dotnet/tools/Sanduhr.RenderHarness/Program.cs:382`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
- **HIGH** **Possible secret: AST_PROCESS_ENV_OVERWRITE** — `vscode/src/merge.test.ts:396`
  Assigning a hardcoded secret onto process.env defeats env-var hygiene. Set it in your environment, not in source, and rotate it.

## Band 2 — tier-appropriate, worth reading

_Medium and Low findings at this tier, plus the OWASP 2021 → 2025 reclassifications worth knowing._

- **MEDIUM** **@vitest/mocker@2.1.0 - 4.1.10 — GHSA-82fw-gwwq-j7x9** _(via npm-audit)_
  Vitest: Path Traversal / Arbitrary File Read via @vitest/mocker Redirect Mock
- **MEDIUM** **Possible secret: GENERIC_SECRET_ASSIGN** — `mac/Tests/SanduhrTests/VaultIngesterTests.swift:662`
  If this is a real credential, rotate it. Move to env vars.
- **MEDIUM** **Possible secret: HIGH_ENTROPY_ASSIGN** — `mac/scripts/export-release-secrets.sh:15`
  High-entropy value assigned to a secret-shaped name. If this is a real credential, move it to an env var and rotate it. If it's a hash or random ID, ignore.
- **MEDIUM** **Possible secret: GENERIC_SECRET_ASSIGN** — `windows-dotnet/tests/Sanduhr.Tests/CredentialManagerInteropTests.cs:37`
  If this is a real credential, rotate it. Move to env vars.
- **MEDIUM** **softprops/action-gh-release not SHA-pinned** — `.github/workflows/windows-dotnet-release.yml:62`
  softprops/action-gh-release@v2 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **License scan not performed (node_modules absent)**
  License scan requires installed node_modules — no node_modules directory was found under this root, so per-package license fields could not be read. This is NOT a clean pass: unchecked, not safe. Run npm/pnpm install, then re-run.
- **LOW** **Rate limiting: custom-rate-limit-detected-not-verified** — `vscode/src/recorder.ts:188`
  A project-local (in-house) rate-limit or budget guard is called here. Detected, not verified — confirm the counter lives in a shared store (not process memory), expires, and fails closed for the public tier. Verification is a human read of the helper, not a scan.
- **LOW** **Workflow missing permissions block** — `.github/workflows/docs-check.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/docs-check.yml:17`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/setup-node not SHA-pinned** — `.github/workflows/docs-check.yml:21`
  actions/setup-node@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **Workflow missing permissions block** — `.github/workflows/mac-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/mac-ci.yml:38`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/cache not SHA-pinned** — `.github/workflows/mac-ci.yml:45`
  actions/cache@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/setup-node not SHA-pinned** — `.github/workflows/mac-ci.yml:54`
  actions/setup-node@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/mac-release.yml:35`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/upload-artifact not SHA-pinned** — `.github/workflows/mac-release.yml:150`
  actions/upload-artifact@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **Workflow missing permissions block** — `.github/workflows/vscode-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/vscode-ci.yml:21`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/setup-node not SHA-pinned** — `.github/workflows/vscode-ci.yml:22`
  actions/setup-node@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **Workflow missing permissions block** — `.github/workflows/windows-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/windows-ci.yml:21`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/setup-python not SHA-pinned** — `.github/workflows/windows-ci.yml:24`
  actions/setup-python@v5 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **Workflow missing permissions block** — `.github/workflows/windows-dotnet-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/windows-dotnet-ci.yml:22`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/setup-dotnet not SHA-pinned** — `.github/workflows/windows-dotnet-ci.yml:23`
  actions/setup-dotnet@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/upload-artifact not SHA-pinned** — `.github/workflows/windows-dotnet-ci.yml:34`
  actions/upload-artifact@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **Workflow missing permissions block** — `.github/workflows/windows-dotnet-release.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/windows-dotnet-release.yml:25`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **actions/setup-dotnet not SHA-pinned** — `.github/workflows/windows-dotnet-release.yml:44`
  actions/setup-dotnet@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
- **LOW** **Floating pin: @types/node@20.x** — `package.json`
  @types/node uses a floating pin (20.x) — the resolved version drifts between installs. Pin to an explicit version.

## Band 3 — if you graduate

_Graduating to customer-facing-saas raises the bar on concerns already in scope._

Nothing tier-inappropriate surfaced — every finding is in scope now.

## Band 4 — tools that catch what the baseline misses

_Pattern #13 complements, led by your detected context. Run these on a future pass when you're not mid-fatigue._

- **Socket.dev (Firewall Free)** (supply-chain) — Catches the attack-window novelty (post-XZ / Shai-Hulud) the lockfile baseline can't — install-time behavioral analysis of new dependency versions.
- **gitleaks + GitHub Push Protection** (secret-detection) — Catches secrets at commit time — push protection blocks the leak before it lands, which a post-hoc scan can't.
- **Dependabot / Renovate** (dependency-cve) — Keeps SCA continuous — opens fix PRs automatically instead of waiting for the next manual audit.

## By OWASP category

_One finding is rendered under each OWASP category it carries (2021 + 2025). It's one finding wearing all its tags — counted once in the score, shown under every applicable category here._

### A04 — Insecure Design

- **LOW** **Rate limiting: custom-rate-limit-detected-not-verified** — `vscode/src/recorder.ts:188`
  A project-local (in-house) rate-limit or budget guard is called here. Detected, not verified — confirm the counter lives in a shared store (not process memory), expires, and fails closed for the public tier. Verification is a human read of the helper, not a scan.
  > also tagged as: secondary: owasp-survey; primary concern: rate-limiting

### A06 — Vulnerable & Outdated Components

- **CRITICAL** **tinypool@<=2.1.1 — GHSA-5gmw-xhrv-c9v3** _(via npm-audit)_
  Tinypool: Prototype Pollution gadget in worker options leads to Remote Code Execution
  > also tagged as: primary concern: dependency-cve
- **CRITICAL** **vitest@0.0.95 - 4.1.10 — GHSA-82fw-gwwq-j7x9** _(via npm-audit)_
  Vitest: Path Traversal / Arbitrary File Read via @vitest/mocker Redirect Mock
  > also tagged as: primary concern: dependency-cve
- **MEDIUM** **@vitest/mocker@2.1.0 - 4.1.10 — GHSA-82fw-gwwq-j7x9** _(via npm-audit)_
  Vitest: Path Traversal / Arbitrary File Read via @vitest/mocker Redirect Mock
  > also tagged as: primary concern: dependency-cve

### A07 — Identification & Authentication Failures

- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:468`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:492`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:493`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:498`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1473`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1473`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1476`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1476`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1476`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1493`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigPackTests.o:1510`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigProposalTests.o:265`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ConfigProposalTests.o:854`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ModMarketplaceTests.o:388`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/.build/out/Intermediates.noindex/Sanduhr.build/Debug/SanduhrTests-p.build/Objects-normal/arm64/ModMarketplaceTests.o:1042`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:17`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:73`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:81`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigPackTests.swift:150`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ConfigProposalTests.swift:50`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `mac/Tests/SanduhrTests/ModMarketplaceTests.swift:171`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `windows-dotnet/tests/Sanduhr.Tests/ConfigMaskTests.cs:92`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **CRITICAL** **Possible secret: OPENAI_API_KEY** — `windows-dotnet/tools/Sanduhr.RenderHarness/Program.cs:382`
  Revoke at platform.openai.com/api-keys. Billing runs on these.
  > also tagged as: primary concern: secret-detection
- **HIGH** **Possible secret: AST_PROCESS_ENV_OVERWRITE** — `vscode/src/merge.test.ts:396`
  Assigning a hardcoded secret onto process.env defeats env-var hygiene. Set it in your environment, not in source, and rotate it.
  > also tagged as: primary concern: secret-detection
- **MEDIUM** **Possible secret: GENERIC_SECRET_ASSIGN** — `mac/Tests/SanduhrTests/VaultIngesterTests.swift:662`
  If this is a real credential, rotate it. Move to env vars.
  > also tagged as: primary concern: secret-detection
- **MEDIUM** **Possible secret: HIGH_ENTROPY_ASSIGN** — `mac/scripts/export-release-secrets.sh:15`
  High-entropy value assigned to a secret-shaped name. If this is a real credential, move it to an env var and rotate it. If it's a hash or random ID, ignore.
  > also tagged as: primary concern: secret-detection
- **MEDIUM** **Possible secret: GENERIC_SECRET_ASSIGN** — `windows-dotnet/tests/Sanduhr.Tests/CredentialManagerInteropTests.cs:37`
  If this is a real credential, rotate it. Move to env vars.
  > also tagged as: primary concern: secret-detection

### A08 — Software & Data Integrity Failures

- **MEDIUM** **softprops/action-gh-release not SHA-pinned** — `.github/workflows/windows-dotnet-release.yml:62`
  softprops/action-gh-release@v2 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Workflow missing permissions block** — `.github/workflows/docs-check.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/docs-check.yml:17`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/setup-node not SHA-pinned** — `.github/workflows/docs-check.yml:21`
  actions/setup-node@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Workflow missing permissions block** — `.github/workflows/mac-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/mac-ci.yml:38`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/cache not SHA-pinned** — `.github/workflows/mac-ci.yml:45`
  actions/cache@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/setup-node not SHA-pinned** — `.github/workflows/mac-ci.yml:54`
  actions/setup-node@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/mac-release.yml:35`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/upload-artifact not SHA-pinned** — `.github/workflows/mac-release.yml:150`
  actions/upload-artifact@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Workflow missing permissions block** — `.github/workflows/vscode-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/vscode-ci.yml:21`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/setup-node not SHA-pinned** — `.github/workflows/vscode-ci.yml:22`
  actions/setup-node@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Workflow missing permissions block** — `.github/workflows/windows-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/windows-ci.yml:21`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/setup-python not SHA-pinned** — `.github/workflows/windows-ci.yml:24`
  actions/setup-python@v5 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Workflow missing permissions block** — `.github/workflows/windows-dotnet-ci.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/windows-dotnet-ci.yml:22`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/setup-dotnet not SHA-pinned** — `.github/workflows/windows-dotnet-ci.yml:23`
  actions/setup-dotnet@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/upload-artifact not SHA-pinned** — `.github/workflows/windows-dotnet-ci.yml:34`
  actions/upload-artifact@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Workflow missing permissions block** — `.github/workflows/windows-dotnet-release.yml:1`
  No top-level permissions: block — the workflow runs with the repo default token scope. Add `permissions: contents: read` and widen per-job as needed.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/checkout not SHA-pinned** — `.github/workflows/windows-dotnet-release.yml:25`
  actions/checkout@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **actions/setup-dotnet not SHA-pinned** — `.github/workflows/windows-dotnet-release.yml:44`
  actions/setup-dotnet@v4 is pinned by floating, not SHA. A tag is mutable — pin to the full commit SHA to defeat tag-rewrite supply-chain attacks.
  > also tagged as: primary concern: supply-chain
- **LOW** **Floating pin: @types/node@20.x** — `package.json`
  @types/node uses a floating pin (20.x) — the resolved version drifts between installs. Pin to an explicit version.
  > also tagged as: primary concern: supply-chain


## Coverage by concern (what each detector could see)

- **secret-detection**: 28561 files scanned (in-house, deferred=false); history: 46 hits; 77664 ms
- **dependency-cve**: root=vscode; kind=library; osv=none; npm audit ran=true; notChecked=false. Swift (Package.swift: Sparkle) and the Python stdlib server have no CVE source here.
- **supply-chain**: workflows: 23 action findings; JS lockfile present=true manager=npm integrity=true floating=1; typosquats=0; depConfusion=1; postinstall=0; sbom={"present":false,"file":null,"format":"unknown"}
- **config-posture**: headers={"present":{},"missing":["content-security-policy","strict-transport-security","x-content-type-options","x-frame-options","referrer-policy","permissions-policy"],"cspReportOnly":false,"hasDeprecatedXssHeader":false}; headerFindings=6; cors=0; cookies=0; firebaseRules=0; cve202529927={"vulnerable":false,"detectedVersion":null,"recommendedFix":null,"detail":null}
- **crypto-pii**: walks JS/TS only: 64 such files in the repo (Swift and Python sources are not read by this detector); primitives=0 jwt=0 clientKeyLeaks=0 piiInLogs=0 piiInventory=0
- **auth-model**: walks JS/TS only: 64 such files in the repo (Swift and Python sources are not read by this detector); routes=0 admin=0 idor=0 session=0 platform=unknown
- **owasp-survey**: walks JS/TS only: 64 such files in the repo (Swift and Python sources are not read by this detector); survey=0 ssrf=0 dynamicCode=0
- **rate-limiting**: walks JS/TS only: 64 such files in the repo (Swift and Python sources are not read by this detector); hasRoutes=false libraryAbsent=false llmRoutes=false platform={"platform":"unknown","nativeRateLimitConfigured":false,"signals":[]}
- **license-compliance**: root=vscode; node_modules=false; model=unknown (); packages=0
- **data-posture**: applicable=false; checks=; signals=none

## Notes

- vibe-test handshake: degraded (absent) — self-classifying tier, no coverage signal
- tier: public-facing: 1 strong + 1 medium signal(s) (public distribution: Homebrew cask (docs/distribution/sanduhr.rb) + Sparkle appcast (docs/appcast.xml) + GitHub releases, Microsoft Store listing for the Windows app (docs/store/), stores the user's own claude.ai credential locally (Keychain) — no server-side user data)
- auth-model and rate-limiting: NOT APPLICABLE — no HTTP route surface detected (desktop app; sanduhr_mcp.py speaks stdio and is network-free by contract). Excluded from the score denominator; not a pass.
- data-posture: NOT APPLICABLE — no persistence layer detected (checked package.json dependencies, framework config files, and source imports). The concern is excluded from the score denominator: nothing to protect is not the same as a pass.

Generated by vibe-sec v0.10.1 (in-house detectors; no gitleaks, osv-scanner, semgrep or trufflehog on PATH).

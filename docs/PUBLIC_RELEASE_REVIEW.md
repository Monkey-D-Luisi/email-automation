# Public release preparation — 2026-10-30

**Verdict: NOT READY.** Finalization upgrades the release template to stable n8n **2.45.6**, with verified bundled **Node 26.12.1**, and all application/workflow compatibility tests pass. However, the image dependency scan still reports the identified Brotli CVE in an operating-system library and additional high-severity dependency advisories. The repository owner explicitly accepts public disclosure of existing author/committer email metadata; that privacy decision is **closed**. No history rewrite, anonymization, replacement repository, merge, push, or visibility change was performed.

## Scope and evidence

- Repository: `Monkey-D-Luisi/email-automation`, branch `feature/outlook-graph-integration`, starting HEAD `25b7ad3a20708ed12ceeb7d4533918fa6ab0a2af`.
- Reviewed all 216 originally tracked files, including source, generated migrations, fixtures, examples, four n8n workflow exports, docs, the frontend lockfile, CI, and license. There are no tracked screenshots, mailbox dumps, or credential exports.
- Compared local and remote branch tips: five branches, 18 reachable commits, no tags. The clone is not shallow. Gitleaks 8.30.1 scanned all branch history with full redaction and found no leaks. A separate scan of all 426 local Git blobs, including unreachable objects, also found no leaks.
- A supplementary privacy check reviewed all 412 unique reachable historical blobs, commit identities, and relevant ignored state. Known local secret/configuration values were compared in memory against Git content without printing them; none matched. Secret scanners are pattern-based, so this result is evidence of the reviewed scope, not a guarantee that every possible secret format is recognized.
- Reviewed `.env`, ignore rules, 36 ignored Playwright diagnostic files, and four temporary Outlook workflow files. The temporary workflow source included a configured personal mailbox identifier. It was local-only; no value is reproduced here. n8n-managed credential references are identifiers, not OAuth secret values.
- Reviewed all four accessible GitHub Actions runs and their eight job logs. No provider secret, private key, or message payload exposure was identified. All four runs returned empty artifact lists. The latest existing [run at `25b7ad3`](https://github.com/Monkey-D-Luisi/email-automation/actions/runs/39769257426) passed both original jobs; it does not validate the pending release changes.
- Reviewed NuGet/npm vulnerability reports and package license metadata. Optional runtime advisories were checked separately against upstream sources; `npm audit` for `web/` does not audit the n8n container.

The completed Codex source scan is retained outside Git and records the original snapshot. Its one low-severity finding is addressed by the included mapper changes. That source security audit was not repeated during finalization. The new work is limited to runtime upgrading, isolated compatibility checks, complete test suites, dependency/secret rechecks, and release documentation.

## Security and privacy findings

| ID | Severity | Evidence | Status and action |
| --- | --- | --- | --- |
| S1 | Critical scanner severity for the OS package; exposure depends on the consumer | Current image pinned in `docker-compose.yml`: n8n 2.45.6, Node 26.12.1, Alpine `brotli-libs` 1.2.0-r1. | **Partially resolved; image blocker remains.** Node itself is patched, but Trivy still reports CVE-2026-111497 against the separate OS library; Alpine lists 1.2.1-r0 as fixed. A version-only Node check is insufficient to declare the entire image patched. No exploitation was attempted or observed. |
| S2 | Medium upstream severity; conditional credential disclosure | First-party n8n advisory GHSA-prmc-fg87-r4gh. | **Resolved in the release template.** Stable n8n 2.45.6 is on the patched 2.45 line (the advisory specifies 2.45.5 or later). Image version and digest were verified. This does not clear separate transitive-library advisories. |
| S3 | Low availability risk in optional workflows | `integrations/n8n/scripts/gmail-message-mapper.js`, `outlook-message-mapper.js`, and both embedded ingestion workflow copies. | **Fixed in the working tree.** Raw HTML previously reached regex conversion before the text-size check. Both mappers now quarantine raw HTML above 40,000 characters before conversion and retain the converted-text cap. Regression tests cover the boundary, oversized well-formed/malformed HTML, and text limits. |
| P1 | Accepted disclosure; closed | Existing Git author/committer metadata. | **Closed by the owner on 2026-10-30.** Public disclosure is explicitly accepted. Existing history and attribution are preserved. No anonymization, history rewrite, or replacement repository is required or authorized. |
| P2 | Low, local-only privacy exposure | Ignored `.playwright-mcp/` diagnostics and temporary `smartinbox-outlook-once-*/workflow-source.json`. | **Cleaned up reversibly.** Moved 36 diagnostic files and the two reviewed temporary export directories outside the repository into local private quarantine. Four temporary files were retained; the other directory was empty. No files were deleted. The quarantine location was delivered separately to the operator. |

S1 is supported by the [Node.js October 2026 security release](https://nodejs.org/en/blog/vulnerability/october-2026-security-releases), which identifies patched releases including Node 26.12.1 and 22.23.4. S2 is supported by the [n8n maintainer advisory](https://github.com/n8n-io/n8n/security/advisories/GHSA-prmc-fg87-r4gh). Both describe prerequisites and impact beyond merely having an old version.

The OS-library finding is independently listed in [Alpine 3.24's security database](https://secdb.alpinelinux.org/v3.24/main.json). The official Docker Hub and `docker.n8n.io` registries both returned the same 2.45.6 image digest, so changing registry does not remove it.

**No genuine credential exposure was established.** There is therefore no evidence-backed revocation/rotation request from this review. If a credential is later shown to have reached an untrusted redirect target, revoke or rotate that credential at its issuer and replace the matching managed credential. Do not treat deleting a file as revocation.

The local `.env`, .NET User Secrets, existing databases, n8n encrypted credentials, instance encryption key, Docker volumes, and live workflow configuration were preserved. n8n execution records and backups can contain bodies and draft snapshots; they remain sensitive local runtime state and were not exported or inspected for mailbox content.

## Changes completed

1. Added pre-conversion HTML bounds in both source mappers and synchronized both inactive workflow templates. Existing plain-text preference, payloads, identity mapping, and body limits remain intact. HTML markup itself now counts toward a documented raw-input cap, so previously accepted large-markup/small-visible-text messages are quarantined.
2. Added a regression test for both mapper boundaries and another for committed default-safe API/integration settings. Updated reviewer-session assertions to verify AI drafting and external exports stay disabled in the base test host.
3. Isolated API test hosts from developer User Secrets with the `Testing` environment. A single configuration dictionary now supplies both early minimal-host startup and final test configuration, with all temporary process environment values restored. Processing hosts explicitly select the substituted test provider. This fixes three failures reproduced when inherited fake-provider settings differed from a test provider's identity.
4. Restricted `NuGet.Config` to public nuget.org, clearing inherited package feeds. No private package source or original-developer account is needed to restore the solution.
5. Matched Compose's fresh database-name fallback to the documented `smartinbox_dev`; the init script still creates `smartinbox_test`. This changes only fresh initialization when no database name is supplied; it does not migrate or rename existing databases.
6. Made `EmailDrafting:Enabled=false` explicit in committed settings. Added offline setup instructions with fresh credentials and either User Secrets or environment variables; fake classification is presented before optional billable AI use.
7. Pinned GitHub Actions by commit SHA, disabled persisted checkout credentials, added job timeouts, and added a Gitleaks full-fetched-history job. Its scanner version and Linux archive SHA-256 are pinned; output is fully redacted. Workflow permissions remain `contents: read` with no deployment or publication job.
8. Expanded ignore rules for local secret files, HAR/log files, and local workflow export directories. Added `SECURITY.md` and `THIRD_PARTY_NOTICES.md` with 84 resolved NuGet package entries and 60 npm lockfile entries, including optional platform packages.
9. Updated README, setup, handoff, roadmap, Outlook setup, and n8n guidance with accurate validation status and runtime blockers. Corrected the optional Docker-host API launch example to use `--no-launch-profile`; the command was not run against the existing integration. Documented Gmail's activation gate and n8n execution-data privacy.

10. Upgraded the optional image to stable n8n **2.45.6**, pinned by digest. Verified bundled Node **26.12.1**, completed four inactive imports and eight isolated synthetic executions, and scanned the exported image with Trivy. The existing configured n8n instance and its volume were preserved.

DDD, Onion/Clean Architecture, CQRS, SOLID boundaries, and feature-first organization remain in place. No new application dependency, public API contract, database migration, send operation, or broad refactor was added.

## Reproducibility and validation results

The complete .NET and frontend suites below were rerun on **2026-10-30**. Runtime compatibility used `n8nio/n8n:2.45.6@sha256:45edf89655fd66dd55ea9336c1791ad14777599fdbf6c5aec6db583ede99a40b`; upstream identifies [2.45.6 as stable](https://github.com/n8n-io/n8n/releases/tag/n8n%402.45.6). The previously completed source audit was reused.

| Check | Result |
| --- | --- |
| .NET restore / Release build | Passed, zero warnings and zero errors; public NuGet source only. |
| .NET tests | **125/125 passed**: Domain 17, Application 51, Infrastructure 38, API 19; no skipped tests. |
| Empty-database migrations | All six migrations applied successfully to a new disposable PostgreSQL 17 development database. EF's initial history-table probe logged a missing-table command before creating the empty schema; migration execution completed successfully. |
| EF pending-model check | Passed; no model changes pending. |
| Clean source reproduction | Passed from a new local Git clone plus the pending release diff, without `.env` or prior build output. Used fresh generated credentials, a separate ephemeral PostgreSQL container with new development/test databases, Production API configuration to avoid User Secrets, and fake classification. Tests used the isolated test database and the built API returned healthy. The disposable API/container were stopped and removed. |
| Frontend/workflow tests | **25/25 passed** on checksum-verified portable Node.js **22.23.4**. Two new tests; existing parity and no-send checks also pass. |
| Frontend production build | Passed on Node 22.23.4. No browser walkthrough was repeated. |
| n8n version/runtime | New network-disabled containers reported n8n **2.45.6** and Node **26.12.1**. |
| n8n workflow compatibility | **4/4 inactive imports and 8/8 synthetic CLI executions passed**, with a new volume and mock API on an internal Docker network without Internet egress. |
| Container dependency scan | Trivy 0.76.1 completed against the exact image archive: **1 critical, 30 high, 51 medium, 30 low** package findings. These are scanner findings, not 112 demonstrated exploitation paths. CVE-2026-111497 remains present in `brotli-libs`; relevant transport-library findings remain unresolved. |
| npm audit | Zero reported vulnerabilities in the frontend dependency graph. |
| NuGet audit | Zero vulnerable direct/transitive packages reported by nuget.org. This does not cover .NET runtime, container OS packages, or n8n. |
| Gitleaks 8.30.1 | Zero history findings; zero findings across all 426 local Git blobs; zero findings in reviewed local diagnostics/temp exports. |
| Final public-file scan | Zero Gitleaks findings across all 219 current tracked/untracked release files; all relative Markdown links resolve. Local ignored runtime state was excluded from that publishable-file set. |
| GitHub Actions configuration | Actionlint 1.7.12 passed. Scanner archive checksum verified against the official release checksums. The new workflow has not run on GitHub because nothing was pushed. |
| Existing hosted CI | Latest original-HEAD run passed backend/frontend jobs. Four runs/eight logs reviewed; no accessible artifacts. |

The clean-copy check reused this Windows machine's installed .NET/Docker tools and dependency caches. It is not a clean operating-system or uncached-install certification. Initial frontend checks used the machine's older Node 22.8.0; the final frontend checks were repeated using patched portable Node without changing the installed runtime.

### Isolated n8n execution evidence

- Used a new n8n volume and mock API on a Docker `--internal` network. No host API/database, original n8n data, OAuth credential, or real provider was used. Temporary containers, network, and validation volume were removed afterward.
- Imported all four release workflow templates as inactive. Only the temporary copies received a synthetic Bearer Auth credential and manual-trigger-first node ordering for CLI execution. Workflow graphs and node parameters were preserved.
- Gmail ingestion produced `202`, `200`, `503`, then `200` on retry. Outlook ingestion produced `202`, `202`, `200` for two synthetic identities and one duplicate. Message correlation remained intact.
- Each provider's `fake` mode completed one synthetic draft ID, `fake-ambiguous` reported `ProviderOutcomeUnknown`, and `disabled` made no claim. All eight executions succeeded, and all four imported workflows remained inactive.
- Before/after comparison confirmed that the existing configured container ID, image ID, start time, restart count, and mounts were identical. Its original image, credentials, and volume were deliberately preserved.

### Remaining image dependency evidence

The critical OS finding is `brotli-libs` **1.2.0-r1**, fixed in **1.2.1-r0**. The image also contains `libcurl` **8.22.0-r0**, with a high-severity advisory fixed in **8.23.0-r0**. Among the JavaScript findings, `follow-redirects` **1.16.0** has header/redirect disclosure advisories fixed in **1.16.1**, and installed `undici` **6.29.0 / 7.29.1** has transport advisories with patched versions listed by the scanner. These warrant resolution or source-backed applicability decisions before an unqualified publication-ready verdict.

The scan also reports dependencies of optional n8n nodes and editor features. Their presence does not establish exploitability through Smart Inbox's supplied workflows; this finalization did not expand into another source security audit or apply unsupported vendor-library overrides. Raw dependency reports remain outside Git and contain no mailbox data.

## Provider validation boundary

The operator reports a successful real Outlook ingestion test on **2026-10-29** using one authorized synthetic email. Graph retrieved it; Smart Inbox returned `202`; PostgreSQL persisted one message; the **fake classifier** produced a classification requiring review and one `PendingReview` work item; identical replay returned `200` without duplication. Outlook workflows were left unpublished and disabled.

That evidence is documented rather than re-executed. External Gmail/Outlook draft creation remains **not live-validated**. OpenAI adapters were tested with fake responses; no model-quality or live API validation is claimed. The release work did not access real email, activate provider workflows, call a paid model, create an external draft, or send email.

## Licensing and public packaging

The original MIT license and its author attribution remain unchanged. Package metadata includes MIT, Apache-2.0, BSD, PostgreSQL, ISC, 0BSD, and CC-BY-4.0 licenses. The optional Sharp/libvips platform packages include LGPL-3.0-or-later terms. n8n is an optional separately downloaded runtime under its own Sustainable Use License and additional enterprise terms; Smart Inbox's MIT license does not cover it. See [third-party notices](../THIRD_PARTY_NOTICES.md) for versioned attribution and distribution obligations.

Only the optional n8n image was upgraded; application NuGet/npm versions are unchanged. No binary release, Docker image distribution, source-license relicensing, or GitHub repository-setting change was performed. Container OS and discovered Node package metadata were scanned separately; the clean frontend/NuGet reports do not clear the n8n findings.

## Remaining release blocker and publication gate

1. **Resolve the remaining image dependencies.** n8n 2.45.6 is the current stable 2.x release and fixes the identified first-party n8n issue and bundled Node version, but its published image retains the OS Brotli CVE and relevant transitive transport advisories. A supported patched image, or a narrowly maintained/retested image with evidence-backed dependency applicability decisions, is needed to close this blocker. The current image is retained as a tested candidate; it is not described as vulnerability-free.
2. **Privacy is closed.** The owner explicitly accepts the existing email metadata. Preserve history and do not request anonymization or another privacy decision.
3. **Publication remains gated.** The intended complete release branch is `feature/outlook-graph-integration`, including all earlier release improvements. The exact candidate commit is supplied in the delivery message. Once the blocker is resolved, final publication can push the reviewed revision, confirm GitHub Actions, select this branch as the public default, and change visibility only after explicit authorization. GitHub's older Phase 1 `main` is unchanged.

External-draft live validation, paid AI evaluation, and production hosting are not required for an honestly labeled offline portfolio demo and remain outside this task. The completed real Outlook ingestion test was not repeated. Only the remaining container dependency findings prevent a publication-ready verdict; privacy and workflow compatibility do not.

## Prioritized next code-improvement phase

| Priority | Task | Reason and bounded next step |
| --- | --- | --- |
| P1 | Make Gmail's live-read boundary explicit in code | Gmail ingestion is gated by workflow activation and credential assignment, while its mode variable gates export. Add a deliberate ingestion gate and runtime mailbox-identity verification with offline workflow regressions before supporting ongoing Gmail use. |
| P1 | Review optional-runtime maintenance checks | After resolving the current blocker, automate advisory/container checks and a safe dependency update process; keep fake import/execution checks independent of OAuth and live mail. |
| P2 | Make HTML normalization predictably cheap within its cap | Raw input is now bounded, but regex conversion still has expensive malformed-input cases. Measure representative/adversarial capped inputs and replace only the demonstrated slow operations; preserve inert output and mapper/template parity. |
| P2 | Remove provider-specific naming from genuinely shared export concepts | Gmail-named domain/persistence types now serve Outlook too. Rename only the shared concepts, with provider-isolation and existing-schema compatibility tests; avoid a new abstraction hierarchy. |
| P2 | Strengthen failure/recovery and incremental ingestion | Add focused coverage for provider success followed by callback/persistence failure. Design durable Graph delta checkpoints before scaling beyond the small fixed-cutoff test folder; do not promise exactly-once external creates. |
| Later | Define the boundary for any real-data or hosted use | Establish identity, rate limits, retention/deletion, encryption, backups, and operational ownership only if that becomes an explicit product goal. The current demonstration remains synthetic and local. |

## Audit tooling record

Gitleaks 8.30.1, Actionlint 1.7.12, portable Node 22.23.4, and Trivy 0.76.1 were obtained from official releases and checksum-verified. The completed independent source review and its canonical artifacts were reused, not rerun or modified. The final image/dependency and compatibility checks are separate validation evidence.

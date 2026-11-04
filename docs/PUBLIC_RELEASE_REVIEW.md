# Public source release resolution — 2026-10-30

**Publication follow-up — 2026-11-04:** `main` includes the approved candidate `4231ff295a81ddb3bb1ed56369995d5afa309b59`. [Hosted candidate CI](https://github.com/Monkey-D-Luisi/email-automation/actions/runs/41798412266) passed backend, frontend, and Gitleaks jobs; see [main CI](https://github.com/Monkey-D-Luisi/email-automation/actions/workflows/ci.yml?query=branch%3Amain) for subsequent validation. The dated assessment below records the completed pre-publication review and was not repeated.

**Verdict: READY FOR PUBLIC SOURCE RELEASE — Option B.** The optional n8n runtime is suspended and absent from every supplied runnable deployment configuration. The core offline API, PostgreSQL, frontend, fake classification, human review, and internal drafts remain supported. Four inactive workflow templates and their tests remain available as source examples.

This completes the bounded container resolution on top of candidate `7c72933de710a27461c5fa51ed82e347cb55e46d`, on `feature/outlook-graph-integration`. The earlier source/history/privacy review, builds, and provider evidence are reused. No new full security audit was initiated. The owner's acceptance of existing Git email metadata is closed; history and attribution are preserved.

## Why a derived n8n image was not selected

The evaluated official image was:

```text
n8nio/n8n:2.45.6@sha256:45edf89655fd66dd55ea9336c1791ad14777599fdbf6c5aec6db583ede99a40b
```

Disposable inspection confirmed n8n **2.45.6**, Node **26.12.1**, and Docker Hardened Images Alpine **3.24**. The APK database and repository metadata exist, but **apk is absent**, including the usual absolute paths. BusyBox/wget are present; their presence does not provide a supported package upgrade mechanism.

The database records DHI `brotli-libs 1.2.0-r1` and `libcurl 8.22.0-r0`; their shared libraries are present under `/usr/lib`. The fixes reported by Alpine/Trivy are **1.2.1-r0** and **8.23.0-r0**, respectively. Node reports Brotli 1.2.0 in its runtime metadata; that string alone neither proves nor clears a Node backport. The separately installed OS library remains an unresolved finding.

The [versioned upstream Dockerfile](https://github.com/n8n-io/n8n/blob/n8n%402.45.6/docker/images/n8n/Dockerfile) uses a pinned vendor base and compiled native modules. Adding a package manager or transplanting libraries from a different Alpine build would require compatibility and vendor-base maintenance beyond a minimal supported patch. Changing registry returned the same image digest. **No derived image was built, no binary was replaced, and no running container was modified.** Option B avoids shipping that unresolved runtime.

## Shipping configuration and changes

- Removed the n8n service, image, integration profile, port, runtime environment, and credential-volume declaration from `docker-compose.yml`. No alternative override or Dockerfile restores it.
- Removed optional-runtime variables from `.env.example`. Default API integrations and AI drafting remain disabled.
- Pinned the current official PostgreSQL 17 image by digest in Compose and CI. The application schema, routes, migrations, NuGet/npm dependencies, and architecture are unchanged.
- Added a deployment regression test that rejects n8n runtime provisioning. Preserved all four workflow JSON files byte-for-byte.
- Updated README, development setup, architecture, Outlook reference, roadmap, handoff, security guidance, and third-party notices to describe the suspended runtime consistently.
- Recorded all unresolved n8n findings and all selected PostgreSQL image findings in the ledgers below, with dependency paths, available fixes, and applicability. No advisory suppression or ignore file was added.

The selected official image is:

```text
postgres:17-alpine@sha256:266d3c257594e195b309ae9d1805e54c788f1d7b375f56bf6c367aac6c917e7b
```

It reports **PostgreSQL 17.11**, Alpine **3.24.3**, and `gosu 1.19`. Its local linux/amd64 image ID is `sha256:ab20e934b27ba3ff927c1b4c9ff8cf6c32f315f743efe68dd3441182a87fb085`. Compose exposes PostgreSQL only on loopback; the API and frontend run from source with the documented local tools.

The previously configured n8n container retains its original ID, image ID, start time, restart count, and mounts. Its credentials and volumes were neither imported into the disposable tests nor modified. Removing a Compose declaration does not delete or patch that separate installation; do not use orphan/volume cleanup as part of release setup.

## Trivy before and after

Trivy **0.76.1** scanned exact Docker image archives using the local vulnerability database on 2026-10-30. Counts below are raw package findings, before applicability triage.

| Artifact | Critical | High | Medium | Low | Unknown | Shipping disposition |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Evaluated n8n 2.45.6 | 1 | 30 | 51 | 30 | 0 | Suspended; no image provisioned or recommended. Findings remain unresolved in that image. |
| Selected PostgreSQL 17.11 | 1 | 25 | 31 | 3 | 1 | Zero OS findings. All 61 findings are Go components recorded in `usr/local/bin/gosu`; binary applicability is explained below. |

There is no patched n8n “after” image to scan: **the runtime was removed from release scope, not fixed or declared clean**. The remaining recommended image was scanned separately. The latest PostgreSQL pull also removes the three OS findings present in the previously cached PostgreSQL image.

### Suspended n8n findings

The complete [112-row n8n findings ledger](security/n8n-2.45.6-residual-findings.csv) preserves scanner severity, advisory, installed/fixed versions, actual container dependency paths, and residual applicability. The critical finding is `CVE-2026-111497` in OS `brotli-libs`. High transport findings include `libcurl`, `follow-redirects`, and `undici`, with patched versions recorded per row. Additional editor and optional-node dependencies are present in the image; Smart Inbox template reachability was not assumed either way.

Those findings are excluded from the shipping configuration by removing the runtime. They have not been suppressed, patched by unsupported overrides, or individually cleared through another vendor-wide review. The suspended image must not be described as safe for production.

### PostgreSQL Go-helper applicability

The [61-row PostgreSQL findings ledger](security/postgres-17.11-image-findings.csv) retains every raw finding, including the critical and high rows. Most are version-only findings against Go **1.24.6**, rather than vulnerable code present in the helper.

An **unfiltered govulncheck 1.9.0 binary-symbol scan** of the exact extracted `gosu` binary used `https://vuln.go.dev` (database modified 2026-10-27T21:16:18Z). It reported 71 module/package findings but only three affected-symbol matches, all for one medium advisory:

- `CVE-2026-61801 / GO-2026-6686`, `github.com/moby/sys/user v0.1.0`, fixed in **v0.4.1**.
- Present symbols: `GetExecUser`, `GetExecUserPath`, and `ParseGroupFilter`.
- A malformed local user/group database can exhaust resources during identity resolution. The standard entrypoint invokes `gosu postgres`; the image's `/etc/passwd` and `/etc/group` are root-owned 0644 and not writable by the postgres user. No remote database or mailbox input path to those files was established. This is a documented residual local-input risk, awaiting an upstream helper update.

The raw critical `CVE-2025-68121 / GO-2026-4337` concerns **TLS session resumption**. Its scan trace contains only the standard-library module/version; the vulnerable TLS package/functions are absent from this binary. None of the 25 raw high findings has an affected-symbol match. The [gosu main source](https://github.com/tianon/gosu/blob/1.19/main.go) and [identity-switch source](https://github.com/tianon/gosu/blob/1.19/setup-user.go) implement local identity lookup and process execution, without the affected TLS, certificate, HTTP, URL, mail, XML, or template services. The Windows-only findings do not apply to the Linux image. The newer `crypto/x509` advisory also concerns certificate code absent from the helper; the Go database date is recorded rather than assumed current for every Trivy advisory.

This narrowing follows [govulncheck's documented binary analysis](https://pkg.go.dev/golang.org/x/vuln/cmd/govulncheck); binary symbol presence is not proof of call-site reachability. Here the actual parser symbols remain recorded as affected. No filters or upstream exclusion script were used. The Go/TLS findings are **not applicable to the shipped helper**, rather than “fixed”; the raw scan is deliberately not presented as zero vulnerabilities. Re-evaluate this disposition when changing the image/helper.

## Validation and preserved evidence

| Check | Result and scope |
| --- | --- |
| Existing .NET build and complete suite | Reused candidate evidence: Release build, zero warnings/errors, **125/125 tests**, all six migrations, and no pending EF model changes. No application code changed in this resolution. |
| Existing frontend build | Reused successful production build on patched portable Node **22.23.4**. Application source and npm lockfile are unchanged. |
| Deployment/frontend/workflow regressions | **26/26 passed**, including the new suspended-runtime assertion, mapper parity, inactive templates, and no-send checks. |
| Selected PostgreSQL digest | Disposable fresh database: **38/38 Infrastructure and 19/19 API tests passed** against the exact selected image. Temporary test container/data were removed. |
| Compose configuration | Resolves to PostgreSQL alone, with the pinned digest, loopback binding, and PostgreSQL volume only. Checked with synthetic configuration, without printing local secrets. |
| Workflow compatibility | Prior **4/4 inactive imports and 8/8 isolated synthetic executions** preserved. Workflow JSON hashes are unchanged. No n8n image was modified or shipped, so these executions were not repeated. |
| Dependency reports | Prior clean application NuGet/npm reports reused; package manifests/lockfiles are unchanged. Trivy and Go-helper evidence above cover the selected runtime artifact. |
| Secrets and CI | Prior completed Gitleaks/source review reused. Gitleaks found **zero leaks across the 17 changed public files** with full redaction. Actionlint, whitespace, relative Markdown links, and CSV structure checks passed. GitHub-hosted CI remains unrun for unpushed changes. |
| Existing configured n8n | Before/after container identity, image, start/restart state, mounts, and workflow file hashes match. No provider access, live workflow activation, external draft, or paid AI request occurred. |

The original isolated workflow runs used a new volume, an internal Docker network without Internet egress, and a mock API. They used no original n8n credentials or host API/database. This evidence is retained without repeating the completed investigation.

The operator's separately authorized **real Outlook ingestion test passed on 2026-10-29** using one synthetic message: Graph retrieval, API `202`, persistence, **fake classification** requiring human review, one `PendingReview` item, and identical replay `200` without duplication. Workflows were left unpublished and disabled. **External Gmail/Outlook draft creation has not been live-validated.** AI adapters have fake-response coverage; no live model-quality claim is made.

## Earlier public-release improvements retained

Candidate `7c72933` already includes bounded HTML handling in both mappers/templates, isolated test-host configuration without developer User Secrets, public NuGet feeds, explicit disabled drafting/integrations, fresh-database setup, expanded ignore rules, pinned Actions and Gitleaks CI, MIT licensing, and third-party attribution. All remain included on the complete Outlook branch.

The earlier review found no genuine credential exposure in the reviewed source/history, accessible CI logs/artifacts, or reviewed local diagnostics. No credential value is reproduced and no new evidence-backed rotation is required. Local-only diagnostics and temporary workflow exports were moved outside the repository into private quarantine. The low-severity unbounded pre-conversion HTML finding was fixed in the candidate with regression tests. Existing Git email metadata disclosure is owner-accepted.

That completed review's detailed evidence remains in this report's version at `7c72933`; it was not rerun. Local ignored configuration, databases, secrets, and n8n data remain outside the public source package. See [security guidance](../SECURITY.md) and [third-party notices](../THIRD_PARTY_NOTICES.md).

## Release scope and next work

There is **no remaining blocker for this public source-release scope**: no known applicable critical vulnerability in the recommended image, every raw high has an explicit binary applicability disposition, the local parser risk is documented, and n8n provisioning is unavailable. This is an offline portfolio demonstration, not a claim of production readiness or a vulnerability-free vendor dependency graph.

The final delivery identifies the exact release commit. Publication, push, merge, default-branch selection, and repository visibility changes remain separately authorized operations; none were performed here.

Prioritized follow-up, outside this release:

1. Select a supported patched n8n image before restoring runtime provisioning; repeat four inactive imports/eight isolated synthetic executions for that image.
2. Before ongoing Gmail use, add a deliberate live-ingestion gate and mailbox-identity verification.
3. Add focused external-draft failure/reconciliation coverage and durable Outlook incremental checkpoints before scaling provider integration.
4. Measure capped malformed-HTML normalization and improve only demonstrated slow paths; keep mapper/template parity.

No additional production-hosting, paid-model, live-draft, or privacy gate is imposed on this source release.

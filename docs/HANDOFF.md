# Handoff — Smart Inbox Automation

## Current repository status

The private repository is [Monkey-D-Luisi/email-automation](https://github.com/Monkey-D-Luisi/email-automation). The Outlook integration is developed on `feature/outlook-graph-integration`, based on the complete Phase 1–4 `feature/gmail-n8n-integration` branch. `main` remains the Phase 1 baseline; `feature/ai-email-classification` and `feature/human-review-and-drafts` preserve the Phase 2 and Phase 3 branch tips. The branch histories are preserved; no feature branch was merged into `main`.

The project has no production deployment or live-provider acceptance. The complete setup for a second Windows computer is in [DEVELOPMENT_SETUP.md](DEVELOPMENT_SETUP.md). Repository publication does not copy User Secrets, OAuth credentials, n8n encrypted credentials, Docker volumes, local databases, or personal environment variables.

## Historical Phase 3 implementation

Phase 3 was implemented and verified locally on `feature/human-review-and-drafts`, based on `40e5c1a` (`Fix Phase 2 classification review gaps`). The targeted acceptance fixes for provider-call authorization, unsaved fact verification, exact-revision draft approval, and frontend CI are included in that branch history. Phase 1/2 ingestion, classification, leases, retries, and original API contracts remain in place.

The review workflow uses a separate `WorkItem` aggregate. Successful classification creates the item and its initial audit event transactionally; the new migration also backfills existing processed messages. Human changes never overwrite the original classification. Verified facts, reviewer notes, versioned internal drafts, decisions, and append-only audit summaries are persisted independently. A local configured reviewer authenticates with an expiring cookie session and antiforgery protection. Approval does not send an email or create an external draft.

The `web/` Next.js 16.4.2 App Router panel supports reviewer sign-in, inbox filters and pagination, detail inspection, fact review, corrections, notes, draft revision/edit/approval/rejection, and audit history. Optional AI draft generation is disabled by default. The web server proxies same-origin API requests; service and provider secrets are not included in the client bundle.

## Verification

- `dotnet restore SmartInbox.sln`: passed.
- `dotnet build SmartInbox.sln --configuration Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test SmartInbox.sln --configuration Release --no-build --no-restore`: **101/101 passed** — Domain 10, Application 49, Infrastructure 30, API 12. Tests used fake providers and synthetic messages; PostgreSQL integration tests ran against local PostgreSQL 17 in `smartinbox_test` with isolated schemas.
- Work-item PostgreSQL coverage includes one-to-one uniqueness, concurrent duplicate initialization, atomic review/audit persistence, stale-version rejection, fresh-context draft revision persistence, and migration backfill of preexisting processed messages.
- API acceptance test `Reviewer_can_correct_verify_draft_approve_resolve_and_inspect_audit` exercised ingestion through a fake classifier, reviewer correction, fact verification, draft creation/edit/approval, resolution, and history. It also confirmed a retried stale approval conflicts without duplicate approval history.
- `dotnet ef migrations has-pending-model-changes`: passed; no pending model changes.
- `dotnet list SmartInbox.sln package --vulnerable --include-transitive`: no vulnerable NuGet packages reported by configured sources.
- In `web/`, `npm ci` installed 29 packages and found 0 vulnerabilities; `npm test`: **4/4 passed** (Node emitted its experimental type-stripping warning); `npm run build`: passed; `npm audit`: 0 vulnerabilities.
- `.github/workflows/ci.yml` has a Node 22 job for `npm ci`, frontend tests, production build, and `npm audit`. At the Phase 3 checkpoint, this workflow had not yet been run by GitHub.
- No live OpenAI calls, outbound email actions, or production/deployment checks were performed.

## Hosted CI

The CI workflow runs on pushes to `main` and `feature/**`, and on pull requests. It uses an isolated PostgreSQL service, fake provider implementations, and synthetic fixtures; it requires no production secrets and does not contact Gmail or OpenAI.

The hosted run for the Phase 4 code and test fix at `d6037b6` [passed both jobs](https://github.com/Monkey-D-Luisi/email-automation/actions/runs/39752702956): Release build, all .NET/PostgreSQL tests, NuGet audit, frontend and n8n fixture tests, Next.js production build, and npm audit. The initial run at `6745f92` failed because minimal-host API startup read the connection string before the test's late in-memory configuration was available. The test fixture now supplies its isolated connection string while creating each host and restores the previous process environment; the API suite passed without User Secrets and the hosted rerun passed. View subsequent runs in [GitHub Actions](https://github.com/Monkey-D-Luisi/email-automation/actions/workflows/ci.yml).

## Publication preparation verification — 2026-10-22

- Local `dotnet restore SmartInbox.sln` and Release build passed; build reported zero warnings and errors.
- PostgreSQL-backed `dotnet test SmartInbox.sln --configuration Release --no-build --no-restore` passed **118/118**: Domain 17, Application 51, Infrastructure 37, API 13. Tests used local PostgreSQL 17 in `smartinbox_test` and isolated test schemas.
- EF reported no pending model changes. The NuGet vulnerability audit reported no vulnerable packages.
- Frontend and n8n workflow tests passed **16/16**; the Next.js production build passed; `npm audit` reported zero vulnerabilities.
- Gitleaks 8.30.1 scanned all published branch history with full redaction and found no leaks.
- These are local results. Hosted GitHub Actions results are linked above and were verified separately.

## Fresh clone verification — 2026-10-22

- Cloned `feature/gmail-n8n-integration` from the private GitHub repository at `d6037b6` into a new temporary directory. The clone had no project `.env`, `.NET` User Secrets, or existing build output.
- Created a new random local `.env` and an isolated PostgreSQL 17 container, database volume, and test schemas. Applied all EF migrations to the empty development database.
- `dotnet restore`, `dotnet tool restore`, Release build, and all **118/118** PostgreSQL-backed .NET tests passed. EF reported no pending model changes and the NuGet audit found no vulnerable packages.
- `npm ci`, frontend and n8n workflow tests (**16/16**), Next.js production build, and `npm audit` passed with zero vulnerabilities.
- This verified the clone and documented commands on this Windows host. It reused the installed .NET, Node.js 22, Docker, and dependency caches, so it is not a clean-machine verification.

The Phase 3 baseline before this follow-up was 98 .NET tests (Domain 10, Application 46, Infrastructure 30, API 12) and 2 frontend tests. This follow-up adds three backend theory cases and two frontend regression cases, bringing the verified totals to 101 .NET tests and 4 frontend tests.

## Live local browser walkthrough — 2026-10-21

- Started PostgreSQL with the documented Compose setup and applied migrations to isolated schema `phase3_walkthrough_1792587462456_dd192y` inside `smartinbox_dev`. Started the API on `127.0.0.1:5168` and Next.js on `127.0.0.1:5169`.
- Windows returned `EACCES` when Next.js tried ports 3000 and 3001; neither port had a listener. The documented `npm run dev` command could not bind on 3000, so the local Next CLI was started directly on port 5169. The normal port remains 3000 in the project scripts.
- Configured the local `local-reviewer` identity in User Secrets for the walkthrough because no reviewer password was configured. The password was generated locally and was not written to the repository.
- Ingested two synthetic messages and processed both through a temporary fake analyzer. Their stored metrics reported provider `fake`, model `walkthrough-fake`. No OpenAI or other provider call was made.
- The inbox displayed both records with the fake provider's expected category and priority and a `Needs attention` indicator. For the invoice item, the original AI classification remained `SalesInquiry` / `Normal`; the effective review values changed to `BillingDocument` / `High` and later to `Urgent` during concurrency testing. Reviewer notes and missing-information entries saved successfully. The invoice-number fact was verified and the order-reference fact was rejected, each with an audit entry.
- Manually created draft revision 1, edited it into revision 2, and approved revision 2. Editing again created revision 3 with state `Draft`; the audit still recorded revision 2 as approved and revision 3 as unapproved. On the support item, manual draft revision 1 was rejected with feedback `Needs a human reply after device verification.` The UI showed `Rejected · v1`, the reviewer, feedback, and a `Draft Rejected` audit entry.
- Restarted both the API and Next.js. The reviewer session remained valid, and the invoice audit history, revision decisions, support draft rejection, and its feedback loaded from PostgreSQL after restart.
- Logout returned `204` and redirected to sign-in. Unauthenticated reviewer-session and work-item requests returned `401`. A review mutation without an antiforgery header returned `400` (`Request verification failed`) and did not change the saved version. Session expiry was simulated by expiring the browser's HttpOnly reviewer cookie; the UI redirected to `/login` and the session endpoint returned `401`. The configured one-hour ticket lifetime was not allowed to elapse in real time.
- For browser concurrency, one tab loaded version 8 while another saved version 9. The stale tab received `409`, showed the conflict message and reload action, and disabled edits. A separately authenticated local HTTP session also attempted a version-9 update after the current version had advanced to 10; it received `409`. A fresh read remained version 10 with category `BillingDocument` and priority `High`.
- At a 390×844 viewport, the inbox and work-item details stacked vertically and the document had no horizontal overflow (375 px document width and client width). The browser request list contained only localhost resources; no Gmail, Outlook, n8n, or external provider endpoints were contacted, and no email or external draft was created.
- The initial page load exposed a missing favicon (`404`); added `web/src/app/icon.svg`, and its generated `/icon.svg` URL returned `200`. The other observed non-2xx results were the intentional `401`, `400`, and `409` security/concurrency cases. A transient Next.js development HMR WebSocket refusal appeared around server restart/context switching; subsequent page loads and local requests succeeded.

## Targeted acceptance follow-up — 2026-10-21

- Reused the two synthetic messages in `phase3_walkthrough_1792587462456_dd192y`; their stored classification metrics identify provider `fake`, model `walkthrough-fake`. Started the API on `127.0.0.1:5168` and the Next.js dev server on `127.0.0.1:5169` with PostgreSQL connected to that schema. Email processing and AI draft generation were disabled for this run.
- Signed in as the configured local reviewer. The inbox showed both messages with the expected effective category and priority plus `Needs attention`. The invoice detail showed its original AI values (`SalesInquiry` / `Normal`) separately from reviewed values (`BillingDocument` / `High`).
- On the support item, changed the visible unverified `ProductOrService` value from `MODEL-7` to `MODEL-8` without saving. The browser showed “Save the correction before verifying this fact.” and disabled Verify. Clicking Correct persisted `MODEL-8` while leaving the fact `Unverified`; Verify then became available. Explicit verification changed it to `Verified` and added a `FactVerified` history entry. PostgreSQL confirmed the saved value and status.
- On the invoice item, edited the visible body of Draft revision 3 without saving. Approval was disabled and the UI showed “Unsaved changes are not part of the saved revision. Save a new revision before approval.” Saving created Draft revision 4; the visible text matched its persisted subject/body, so approval became available. Approving it produced `Approved · v4` and a `DraftApproved` history entry. PostgreSQL confirmed revision 4 is Approved and its body is the exact saved text.
- The browser network list contained only localhost requests. The two console errors were the expected initial unauthenticated `/reviewer/session` `401` responses before login; all subsequent API requests succeeded, with no other failed requests or browser warnings. No Gmail, Outlook, n8n, AI-provider, or external email endpoint was contacted; no external email or draft was created.
- At a 390×844 viewport, `document.documentElement.scrollWidth` was 375 px, so the page had no horizontal overflow.
- The synthetic walkthrough changes remain in that local schema for inspection: the support `ProductOrService` fact is `MODEL-8` / `Verified`, and invoice draft revision 4 is `Approved`.

## Local setup

Follow [README.md](../README.md) for PostgreSQL, User Secrets, migration application, reviewer password configuration, backend startup, and `web/` startup. The worker remains disabled by default. New messages will not appear in the review queue until they are successfully classified; no AI request is required by the automated end-to-end test.

PostgreSQL integration tests require `SMARTINBOX_TEST_CONNECTION_STRING` to point to a localhost database ending in `_test`. Each fixture creates and drops an isolated schema. The API acceptance test substitutes a fake classifier and uses the same local database fixture.

## Phase 4 Gmail and n8n — 2026-10-22

- Added a single-configured-mailbox Gmail ingestion endpoint protected by its own service token. The API, not n8n payload data, supplies the source and mailbox identity. Existing PostgreSQL message identity and replay semantics remain authoritative.
- Added a PostgreSQL-backed export intent for the exact current approved draft revision, recipient, and configured mailbox. Claims are row-locked and leased; callbacks are attempt-scoped; duplicate requests/callbacks are safe. An expired or uncertain Gmail attempt requires manual reconciliation. No automatic retry follows a possibly successful Gmail create call.
- Added a separate reviewer action and UI status/reconciliation controls. A reviewer explicitly confirms the existing recipient and requests an unsent external Gmail draft. The action is hidden when disabled or ineligible.
- Added inactive, credential-free n8n workflow exports and an optional `n8nio/n8n:2.44.5` Compose service. The fake provider always returns an ambiguous `Unknown` classification that requires human review. Fake Gmail export records only a synthetic draft ID.
- Fixed ingestion batch handling so every Gmail message keeps its own provider ID and correlation through API responses and bounded HTTP/network retries. Three-item regression coverage now mixes accepted, duplicate, and retryable outcomes; attempt counts remain bounded at three.
- Moved Smart Inbox service tokens from n8n process environment variables to separately managed n8n **Bearer Auth** credentials. The workflow exports contain credential types but no credential IDs or values. The guide documents n8n encryption-key, volume backup, workflow-editor, and environment-access trade-offs.
- A configured mailbox ID is compared to the Gmail OAuth account by the operator during setup; the MVP does not verify OAuth account identity at runtime. `gmail.compose` itself allows sending, although the workflow contains only Gmail Draft/Create. Gmail reply threading is unsupported.

### Verification

- Release build: passed with zero warnings and errors.
- PostgreSQL-backed `dotnet test SmartInbox.sln -c Release --no-restore`: 115/115 passed (Domain 17, Application 51, Infrastructure 34, API 13). This includes export uniqueness, concurrent claims, duplicate callbacks, expiry/manual reconciliation, pending-export cancellation on edit, and Phase 3-to-Phase 4 migration replay.
- `dotnet ef migrations has-pending-model-changes`: passed. NuGet package audit reported no known vulnerable packages.
- Frontend and n8n fixture tests: 15/15 passed. Next.js production build passed; `npm audit` reported zero vulnerabilities.
- Imported both workflow exports into actual local `n8nio/n8n:2.44.5` and executed the manual synthetic paths with the n8n CLI. For this isolated CLI run, a temporary import reordered only the trigger-node array so `execute --id` selected the manual synthetic trigger; the checked-in workflow graph and node parameters were unchanged. Both workflow executions completed successfully.
- Ingestion ran against the Smart Inbox API and a unique schema in the local PostgreSQL 17 `smartinbox_test` database. The three independent synthetic messages produced API statuses `202` accepted, `200` duplicate, and `503` followed by `202` after the bounded retry. PostgreSQL showed three distinct processed inbound records and three separate `PendingReview` work items.
- The approved-draft reviewer flow created an export intent, then the outbound workflow ran in `fake-ambiguous` mode. The API claim and failure callback completed; PostgreSQL showed `NeedsReconciliation`, `ProviderOutcomeUnknown`, attempt count 1, and a null Gmail draft ID. The ambiguous branch did not call Gmail.
- No live Gmail OAuth, live message polling, Gmail draft creation, email send, OpenAI call, hosted CI, push, or deployment was performed. The local run verifies workflow execution and API/database behavior, but not Google OAuth/account identity or live provider behavior.

This remains a single-reviewer localhost demo using synthetic data. There is no retention/deletion scheduler, multi-user identity administration, rate limiting, or production TLS deployment. Email bodies and audit history persist in PostgreSQL until the operator removes them. Do not use real customer data or expose the service publicly.

### PostgreSQL callback tracking fix and cross-platform verification — 2026-10-22

- Root cause: `CreateOrGetAsync` tracked an existing `GmailDraftExportRecord`. A later `SELECT ... FOR UPDATE` on that same scoped context could resolve to the tracked identity without refreshing its status. The losing callback therefore saw stale `Pending` state and returned `Stale` after the winning callback had persisted `Succeeded`.
- Changed the existing-export lookup to no-tracking. Both locked work-item and export reads now explicitly reload their tracked entities after acquiring the row lock. Claim and manual-retry validation also read the draft and current revision through fresh no-tracking queries while holding the work-item lock. Row locks, domain transitions, schema, API contracts, and export safety rules are unchanged.
- Added deterministic PostgreSQL coverage that starts the losing repository with a tracked `Pending` export, runs both repositories as the winner in separate cases, and verifies the duplicate callback returns `Duplicate`. Added coverage that a tracked old draft graph cannot authorize a manual retry after a newer unapproved revision is saved. The existing concurrent-claim test remains enabled.
- Ran the full PostgreSQL-backed solution suite three consecutive times against local PostgreSQL 17 in `smartinbox_test`: **118/118 passed each run** — Domain 17, Application 51, Infrastructure 37, API 13.
- Linux Node 22.16.0 frontend/workflow suite: **16/16 passed**. Release solution build passed with 0 warnings and 0 errors; the Next.js production build passed.
- EF reported no pending model changes. The NuGet audit reported no vulnerable packages, and `npm audit --audit-level=moderate` reported 0 vulnerabilities.
- No Gmail OAuth, live provider call, external draft creation, email send, push, or deployment was performed.

## Outlook Graph integration — 2026-10-22

- Added a disabled-by-default personal Microsoft account integration. Outlook service tokens use separate ingestion and draft-export policies. The API derives source and mailbox identity from configuration; draft exports, claim queries, callbacks, audit actions, and uniqueness are provider-scoped while existing Gmail defaults and routes remain compatible.
- Added inactive, credential-free n8n workflow exports for custom test-folder ingestion and reviewer-approved draft creation. The read workflow verifies `/me`, requests text content and immutable message IDs, follows Graph pagination, and uses bounded read retries. The export verifies `/me`, calls only `POST /me/messages`, accepts only a confirmed draft response, and requires manual reconciliation after an uncertain outcome. The setup guide documents personal-account authority, delegated scopes, credential storage, and isolated test-folder configuration; it excludes `Mail.Send` and application permissions.
- Added Outlook provider selection, mailbox and recipient confirmation, migration support, fixtures, and API/UI/workflow coverage. Message bodies remain plain text in PostgreSQL. Configure distinct provider `SourceId` values and protect the local API secrets, n8n editor, credential volume, and encryption key.

### Verification

- `dotnet restore SmartInbox.sln` and the Release solution build passed; the build reported zero warnings and errors.
- PostgreSQL-backed `dotnet test SmartInbox.sln --configuration Release --no-restore` passed **125/125**: Domain 17, Application 51, Infrastructure 38, API 19.
- Frontend and n8n tests passed **23/23**; the Next.js production build passed; `npm audit` and the transitive NuGet vulnerability audit reported no vulnerabilities.
- EF reported no pending model changes, and `docker compose --profile integrations config -q` passed.
- Imported the checked-in Outlook workflows into local n8n 2.44.5 and verified they were inactive with no credential references. Disposable n8n workflow copies completed the synthetic ingestion and export paths using mocked API responses and the `fake` / `fake-ambiguous` modes; Graph nodes were not reached. The existing local n8n service was restored and its health endpoint returned 200.
- The completed Codex Security diff scan reviewed 45 code and workflow review items against base `80df27127a352e4fb42798c517c39b051e0ec32b` and reported no findings. Its immutable snapshot predates the provider-specific audit-summary correction, its PostgreSQL regression test, and the workflow test update that checks for personal Outlook-domain addresses without hardcoding one; these follow-ups were reviewed and covered by the final tests.
- No Microsoft OAuth, live Graph request, mailbox poll, external draft creation, email send, or deployment was performed. OAuth grants and mailbox identity remain unverified; hosted CI status is separate from these local results.

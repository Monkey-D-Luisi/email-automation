# Handoff — Smart Inbox Automation, Phase 3

## Status

Phase 3 is implemented and verified locally on `feature/human-review-and-drafts`, based on `40e5c1a` (`Fix Phase 2 classification review gaps`). The targeted acceptance fixes for provider-call authorization, unsaved fact verification, exact-revision draft approval, and frontend CI are included in the current local commit. It has not been pushed, deployed, or published. Phase 1/2 ingestion, classification, leases, retries, and original API contracts remain in place.

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
- `.github/workflows/ci.yml` now has a Node 22 job for `npm ci`, frontend tests, production build, and `npm audit`. The workflow was not run by GitHub because this change remains local.
- No live OpenAI calls, outbound email actions, or production/deployment checks were performed.

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

## Limits and Phase 4

This is a single-reviewer localhost demo using synthetic data. It has no retention/deletion scheduler, multi-user identity administration, rate limiting, or production TLS deployment. The one-hour cookie expiry was simulated in the browser rather than observed after waiting an hour; Next.js also required a local port override on this Windows host. Email bodies and audit history persist in PostgreSQL until the operator removes them; source deletion cascades to related review data. Do not use real customer data or expose the service publicly.

Phase 4 must introduce a distinct, authorized execution step for any external draft creation or sending. It will need recipient verification, least-privilege provider scopes, idempotency, delivery receipts, revocation, bounded retries, and audit linkage to the exact approved revision. Current approval alone must never execute an external action.

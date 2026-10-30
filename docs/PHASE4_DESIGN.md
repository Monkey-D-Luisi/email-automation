# Phase 4 integration design checkpoint

## Baseline

- Phase 3 is committed through `04a2e47` (`fix(review): guard draft generation and unsaved decisions`). The requested branch, `feature/gmail-n8n-integration`, starts from that commit.
- Baseline checks passed: .NET Release build with zero warnings, 101/101 .NET tests including PostgreSQL tests, 4/4 frontend tests, Next.js production build, and npm audit with zero vulnerabilities.
- The existing `.playwright-mcp/` diagnostics are pre-existing untracked local files and are being preserved.

## Decisions

1. **Integration boundary:** n8n polls Gmail and shapes messages, then calls a dedicated Smart Inbox Gmail-ingestion route. That route supplies the configured Gmail source and mailbox identity and invokes the existing inbound-message command, preserving its PostgreSQL idempotency and `202`/`200`/`409` behavior. n8n cannot choose a mailbox by editing its payload.
2. **Identity and credentials:** Gmail OAuth stays in n8n. The API has one configured mailbox identity for the MVP. Gmail ingestion and draft-export workers use separate service tokens; the existing generic message token and reviewer cookie remain separate. Browser code receives no integration token or Google token.
3. **Message mapping:** use the Gmail Trigger's full parsed message, preferring its plain-text body and using a non-executing HTML-to-text fallback. Map Gmail's provider `id` and `threadId` separately from the RFC `Message-ID` header. Do not download attachments; map available metadata only. Smart Inbox's existing request limits remain the final validation boundary.
4. **Export state and idempotency:** persist one PostgreSQL export intent per internal draft revision, configured mailbox, and normalized confirmed recipient. The states are `Pending`, `InProgress`, `Succeeded`, `Failed`, `NeedsReconciliation`, and `Cancelled`. PostgreSQL uniqueness and row-locked claims prevent duplicate jobs across n8n executions.
5. **Pinned authorization:** a reviewer request must include the current work-item version, current revision number, configured destination mailbox, and confirmed existing draft recipient. The API requires the reviewer to own an in-review item and the current revision to be approved. It snapshots subject/body and fingerprints the snapshot before the job can be claimed. Editing the draft cancels a pending export in the same PostgreSQL save; a claimed export keeps its immutable snapshot.
6. **Ambiguous outcomes:** claims are single-attempt leases. An expired in-progress claim becomes `NeedsReconciliation`; it is never automatically claimed again. A Gmail-node error is reported as ambiguous. A reviewer can record a located Gmail draft ID or explicitly confirm that no draft exists before requeueing. Duplicate callbacks for the same attempt and result are idempotent; stale or conflicting callbacks cannot overwrite state. Exactly-once Gmail creation is not promised.
7. **Service authentication:** distinct bearer-token schemes protect Gmail ingestion and export claim/callback routes. Reviewer-created intents and manual reconciliation remain cookie-authenticated and antiforgery-protected. The export worker receives only persisted snapshot data and cannot create intents or change review decisions.
8. **Persistence:** add an additive `gmail_draft_exports` table with an explicit EF mapping, unique idempotency index, status/check constraints, snapshot, fingerprint, reviewer, attempt/correlation, lease, safe failure classification, and provider draft ID. Existing tables, volumes, and Phase 3 data are unchanged.
9. **Offline validation:** use synthetic Gmail fixtures, fake classifier settings, and an explicit fake-draft adapter path in the outbound workflow. Keep workflows inactive after import and require separate authorization and OAuth setup for live use. Validate node parameters against the pinned n8n 2.45.6 release image and exercise workflow import/runtime without provider credentials. Gmail ingestion is gated by workflow activation; its mode variable selects only the export adapter.

## Scope limits

The MVP creates standalone Gmail drafts because the current persisted message contract does not retain the RFC headers needed to establish reply threading safely. It does not send mail, change labels/read state, download attachments, or call a live AI provider. OAuth scopes and the fact that `gmail.compose` can also send must be documented explicitly.

## Outlook Graph extension

- Outlook reuses the inbound message command and provider-scoped export state. Its dedicated API routes set the configured source and mailbox; n8n cannot select another mailbox in the message payload. Graph message IDs are the idempotency key. RFC `internetMessageId`, recipient emails, and `conversationId` are stored separately as metadata.
- Delegated OAuth uses the `consumers` authority for personal Microsoft accounts. Read and draft-create credentials are separate, the latter uses `Mail.ReadWrite`, and neither requests `Mail.Send`. Both live paths verify Graph `/me` against the locally configured mailbox.
- Ingestion polls only a configured custom test-folder ID after a fixed UTC cutoff and follows `@odata.nextLink` pages. It does not search Inbox or system folders, mutate messages, or download attachment bytes. The fixed cutoff repeats the small scope and relies on the database idempotency key; durable delta tracking is outside this test integration.
- Export creates a message with Graph `POST /me/messages`, validates `201`, ID, and `isDraft`, then reports an attempt-scoped callback. An uncertain outcome is never automatically retried; a reviewer must reconcile Drafts before any manual retry. Neither provider integration sends mail.

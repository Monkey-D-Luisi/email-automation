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
9. **Offline validation:** use synthetic Gmail fixtures, fake classifier settings, and an explicit fake-draft adapter path in the outbound workflow. Keep both workflows inactive after import and require a separate `live` mode plus user OAuth setup for Gmail. Validate node parameters against n8n 2.44.5 and exercise workflow import/runtime without Gmail credentials.

## Scope limits

The MVP creates standalone Gmail drafts because the current persisted message contract does not retain the RFC headers needed to establish reply threading safely. It does not send mail, change labels/read state, download attachments, or call a live AI provider. OAuth scopes and the fact that `gmail.compose` can also send must be documented explicitly.

# Roadmap

## Phase 1 — Email ingestion and persistence

Implemented: normalized message ingestion, PostgreSQL persistence, idempotency, protected versioned API, and real PostgreSQL concurrency coverage.

## Phase 2 — AI-assisted classification

Implemented locally: optional background classification for persisted messages, one-to-one result persistence, English/Spanish structured extraction, deterministic validation and review reasons, bounded retries, lease recovery, OpenAI Responses integration, and a 31-case synthetic evaluation harness using fixed fake candidates. The worker is disabled by default. No live model evaluation or customer email processing has been performed.

## Phase 3 — Human review and drafts

Implemented locally: transactional work-item creation and backfill, immutable original classifications, reviewer-owned corrections and fact verification, optimistic concurrency, append-only audit entries, and versioned response drafts with explicit approval or rejection. The single local reviewer signs in with a server-configured secret and cookie session. The Next.js 16.4.2 App Router panel provides the review queue and history. Optional AI draft proposals remain disabled by default and always require explicit reviewer action. Approval does not create an external draft or send email.

Current limits: one configured reviewer, localhost/synthetic data only, and no retention or deletion scheduler. The automated API acceptance flow uses a fake classifier and local PostgreSQL; a live local browser walkthrough is recorded in `docs/HANDOFF.md`. No real provider or hosted acceptance was performed.

## Phase 4 — Integrations

- Treat an approved internal draft revision as an input requiring a separate authorization and execution workflow; approval alone must never send it.
- Before Gmail/Outlook or n8n integration, define idempotency keys, least-privilege scopes, recipient verification, outbound content confirmation, retries, rate limits, delivery receipts, revocation, and audit linkage to the approved revision.
- Do not persist provider tokens without encryption, rotation, and secret-handling controls. Keep external draft creation and sending behind a separate, explicit Phase 4 action.

## Phase 5 — Safe operations

- Design mailbox-level authentication and authorization, retention and deletion, encryption, auditing, and monitoring.
- Add load tests, failure recovery, backups, and deployment controls.
- Review the threat model and compliance requirements before processing real data or exposing services publicly.

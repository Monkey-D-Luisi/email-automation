# Roadmap

## Phase 1 — Email ingestion and persistence

Implemented: normalized message ingestion, PostgreSQL persistence, idempotency, protected versioned API, and real PostgreSQL concurrency coverage.

## Phase 2 — AI-assisted classification

Implemented locally: optional background classification for persisted messages, one-to-one result persistence, English/Spanish structured extraction, deterministic validation and review reasons, bounded retries, lease recovery, OpenAI Responses integration, and a 31-case synthetic evaluation harness using fixed fake candidates. The worker is disabled by default. No live model evaluation or customer email processing has been performed.

## Phase 3 — Human review and drafts

- Add a review queue and draft/work item only after defining ownership and retention.
- Record reviewer identity, decisions, edits, and audit history.
- Keep sending outside the workflow until permissions and approval policy are agreed.

## Phase 4 — Integrations

- Add email connectors only with least-privilege permissions and verified webhook handling.
- Establish connector retries, rate limits, and traceability.
- Do not persist provider tokens without an encryption and rotation plan.

## Phase 5 — Safe operations

- Design mailbox-level authentication and authorization, retention and deletion, encryption, auditing, and monitoring.
- Add load tests, failure recovery, backups, and deployment controls.
- Review the threat model and compliance requirements before processing real data or exposing services publicly.

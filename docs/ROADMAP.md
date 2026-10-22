# Roadmap

Phases 1–4 are implemented in the private repository. The Outlook extension is developed on `feature/outlook-graph-integration` from the complete `feature/gmail-n8n-integration` Phase 4 baseline. `main` remains the Phase 1 baseline; `feature/ai-email-classification` and `feature/human-review-and-drafts` preserve the Phase 2 and Phase 3 branch tips. See [DEVELOPMENT_SETUP.md](DEVELOPMENT_SETUP.md) to clone the complete implementation.

## Phase 1 — Email ingestion and persistence

Implemented: normalized message ingestion, PostgreSQL persistence, idempotency, protected versioned API, and real PostgreSQL concurrency coverage.

## Phase 2 — AI-assisted classification

Implemented: optional background classification for persisted messages, one-to-one result persistence, English/Spanish structured extraction, deterministic validation and review reasons, bounded retries, lease recovery, OpenAI Responses integration, and a 31-case synthetic evaluation harness using fixed fake candidates. The worker is disabled by default. No live model evaluation or customer email processing has been performed.

## Phase 3 — Human review and drafts

Implemented: transactional work-item creation and backfill, immutable original classifications, reviewer-owned corrections and fact verification, optimistic concurrency, append-only audit entries, and versioned response drafts with explicit approval or rejection. The single local reviewer signs in with a server-configured secret and cookie session. The Next.js 16.4.2 App Router panel provides the review queue and history. Optional AI draft proposals remain disabled by default and always require explicit reviewer action. Approval does not create an external draft or send email.

Current limits: one configured reviewer, localhost/synthetic data only, and no retention or deletion scheduler. The automated API acceptance flow uses a fake classifier and PostgreSQL. A live local browser walkthrough is recorded in `docs/HANDOFF.md`; hosted CI results are recorded there separately. No real provider acceptance has been performed.

## Phase 4 — Integrations

Implemented: one-configured-mailbox Gmail and Outlook ingestion through n8n, shared provider-scoped export intents and leases, separate service tokens, reviewer-confirmed destination and recipient, and inactive draft-only workflows. Both live paths are disabled by default; no send operation exists. Outlook uses delegated Microsoft Graph OAuth, `/me` mailbox verification, a configured custom test folder and fixed cutoff, and manual reconciliation after ambiguous draft creation. The fixed cutoff re-reads the small test scope and has no durable delta checkpoint. Gmail compose and Outlook `Mail.ReadWrite` OAuth permissions allow broader provider operations than the workflows use. Offline fake and synthetic mapper paths are covered; no Microsoft OAuth or live Graph behavior has been verified. See [the n8n guide](../integrations/n8n/README.md) and [Outlook test setup](OUTLOOK_TEST_SETUP.md).

Remaining before real Outlook use: the user must complete Microsoft app registration, consent to the two delegated credentials, verify the test-folder ID and `/me` identity, and separately authorize a controlled one-message/one-draft test. No authentication, live mailbox polling, Graph call, or draft mutation has been performed. Do not use customer data or expose the local services publicly.

## Phase 5 — Safe operations

- Design mailbox-level authentication and authorization, retention and deletion, encryption, auditing, and monitoring.
- Add load tests, failure recovery, backups, and deployment controls.
- Review the threat model and compliance requirements before processing real data or exposing services publicly.

# Roadmap

Phases 1–4 and the Outlook extension are implemented on `main`, including the reviewed Outlook release. Existing feature branches preserve earlier phases. See [DEVELOPMENT_SETUP.md](DEVELOPMENT_SETUP.md) to clone the complete offline demo and [the release review](PUBLIC_RELEASE_REVIEW.md) for the prioritized next-phase work. Use pull requests targeting `main` for subsequent changes.

## Phase 1 — Email ingestion and persistence

Implemented: normalized message ingestion, PostgreSQL persistence, idempotency, protected versioned API, and real PostgreSQL concurrency coverage.

## Phase 2 — AI-assisted classification

Implemented: optional background classification for persisted messages, one-to-one result persistence, English/Spanish structured extraction, deterministic validation and review reasons, bounded retries, lease recovery, OpenAI Responses integration, and a 31-case synthetic evaluation harness using fixed fake candidates. The worker is disabled by default. No live model evaluation or customer email processing has been performed.

## Phase 3 — Human review and drafts

Implemented: transactional work-item creation and backfill, immutable original classifications, reviewer-owned corrections and fact verification, optimistic concurrency, append-only audit entries, and versioned response drafts with explicit approval or rejection. The single local reviewer signs in with a server-configured secret and cookie session. The Next.js 16.4.2 App Router panel provides the review queue and history. Optional AI draft proposals remain disabled by default and always require explicit reviewer action. Approval does not create an external draft or send email.

Current limits: one configured reviewer, localhost/synthetic data only, and no retention or deletion scheduler. The automated API acceptance flow uses a fake classifier and PostgreSQL. Browser and hosted-CI evidence is recorded in [HANDOFF.md](HANDOFF.md). Real-provider acceptance is limited to the synthetic Outlook ingestion test described below.

## Phase 4 — Integrations

The public source release retains Phase 4's contracts, inactive workflow templates, mappers, and tests. The optional n8n runtime is temporarily suspended and absent from Compose until a supported patched image is selected. The core offline demo remains available; see [the release resolution](PUBLIC_RELEASE_REVIEW.md).

Implemented: one-configured-mailbox Gmail and Outlook ingestion through n8n, provider-scoped export intents and leases, separate service tokens, reviewer-confirmed destination/recipient, and inactive draft-only workflows. Live paths are disabled by default; no send operation exists. Outlook verifies `/me`, reads a custom test folder after a fixed cutoff, and requires manual reconciliation after uncertain draft creation. There is no durable Graph delta checkpoint. OAuth permissions remain broader than the workflow operations. Offline fake and synthetic paths are covered. A real Graph ingestion test passed with one authorized synthetic message and a fake classifier on 2026-10-29; external draft creation remains **not live-validated**. See [the n8n guide](../integrations/n8n/README.md) and [Outlook test setup](OUTLOOK_TEST_SETUP.md).

The completed ingestion test does not authorize further mailbox access or external draft creation. Keep workflows inactive and obtain separate authorization for any future live validation. New developers create their own optional OAuth credentials; the offline demo does not require them. Do not use customer data or expose local services publicly.

## Phase 5 — Safe operations

- Design mailbox-level authentication and authorization, retention and deletion, encryption, auditing, and monitoring.
- Add load tests, failure recovery, backups, and deployment controls.
- Review the threat model and compliance requirements before processing real data or exposing services publicly.

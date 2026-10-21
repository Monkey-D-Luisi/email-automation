# Roadmap

The following phases are proposals and have not been implemented.

## Phase 2 — Assisted classification

- Define categories, structured fields, confidence criteria, and recoverable errors.
- Add a classification use case and provider contract when there is a concrete need.
- Test with a synthetic corpus and measure quality; do not automate actions from an unvalidated response.

## Phase 3 — Human review and drafts

- Add a review queue and a draft associated with each message.
- Allow proposals to be approved, edited, or discarded with identity and audit records.
- Keep sending outside the workflow until permissions and approval policy are agreed.

## Phase 4 — Integrations

- Add an n8n integration and email connectors with least-privilege permissions.
- Establish webhook verification, retries, rate limits, and traceability.
- Do not persist provider tokens without an encryption and rotation plan.

## Phase 5 — Safe operations

- Design mailbox-level authentication and authorization, retention and deletion, encryption, auditing, and monitoring.
- Add load tests, failure recovery, backups, and deployment controls.
- Review the threat model and compliance requirements before processing real data or exposing services publicly.

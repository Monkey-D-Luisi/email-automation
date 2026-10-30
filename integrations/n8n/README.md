# Gmail and Outlook workflow source

## Runtime temporarily unavailable

This public source release implements the safe source-only fallback. It supplies **no n8n container, Compose profile, recommended image, or runtime provisioning command**. The evaluated n8n 2.45.6 image retains unresolved OS and transitive dependency findings. Its hardened DHI runtime has no package manager for a minimal supported in-place build update. No derived image or binary replacement is provided.

Compose deploys PostgreSQL only. The core API, fake classifier, frontend, human review, and internal drafts run offline without n8n or provider accounts. See [development setup](../../docs/DEVELOPMENT_SETUP.md) and [release resolution](../../docs/PUBLIC_RELEASE_REVIEW.md).

The existing configured n8n instance, credentials, and Docker volumes were preserved. Suspending this repository's runtime configuration does not patch that separate installation. Do not remove its orphaned container or volume as part of source-release setup.

## Preserved templates

| File | Purpose |
| --- | --- |
| [gmail-ingestion.json](gmail-ingestion.json) | Synthetic/manual fixtures and the designed Gmail ingestion path, mapping provider identity to the authenticated API. |
| [gmail-approved-draft-export.json](gmail-approved-draft-export.json) | Reviewer-authorized snapshot claim, fake completion/ambiguity paths, and the designed unsent Gmail draft adapter. |
| [outlook-ingestion.json](outlook-ingestion.json) | Synthetic Graph fixtures, mailbox identity checks, custom-folder/cutoff polling design, and API mapping. |
| [outlook-approved-draft-export.json](outlook-approved-draft-export.json) | Provider-scoped claims and callbacks, fake outcomes, and the designed unsent Graph draft adapter. |

All four templates remain inactive and credential-free. Their source mappers, fixtures, and graph tests are retained unchanged. They are source examples, not a supported runtime deployment.

## Offline checks

From the repository root:

```powershell
Push-Location web
npm ci
npm test
Pop-Location
```

These Node tests inspect the workflow graphs and execute synthetic mapper/decision code without Docker, OAuth, n8n, mailbox access, or external drafts. A deployment regression also rejects reintroducing an n8n runtime in the public Compose configuration.

Both mappers bound raw HTML at 40,000 characters before conversion and preserve the converted-text limit. The tests cover identity, item pairing, retries, invalid inputs, source/template parity, inactive exports, and absence of send/destructive actions.

## Contract and authorization boundaries

- The API supplies each enabled integration's configured source and mailbox. Ingestion returns `202` for a new message, `200` for identical replay, and `409` for conflicting content.
- The fake classifier is offline and always requires human review. AI drafting is independently disabled; selecting fake classification does not make AI drafting fake.
- Internal draft approval does not create an external artifact. A separate reviewer intent binds the current approved revision, destination mailbox, and recipient. Provider-scoped workers can claim only its persisted snapshot.
- An uncertain provider create becomes `NeedsReconciliation`. No automatic create replay or exactly-once external-draft guarantee is provided.
- Template export modes default to `disabled`. Gmail's mode variable gates export, while Gmail ingestion is gated by workflow activation and credential assignment. Outlook's designed live path also checks mode, identity, folder, and cutoff.
- Gmail compose and Outlook `Mail.ReadWrite` permissions are broader than the templates' draft-only operations. Templates contain no send action. Runtime restoration must retain those explicit authorization boundaries.

[Architecture](../../docs/ARCHITECTURE.md) and [Outlook design/test reference](../../docs/OUTLOOK_TEST_SETUP.md) preserve the implementation details. OAuth instructions are reference material for future supported runtime selection, not instructions to enable an existing instance during this release.

## Preserved validation evidence

The prior evaluated image completed four inactive imports and eight isolated synthetic executions on 2026-10-30 using a new volume, internal Docker network, and mock API. That compatibility evidence remains valid for the unchanged templates; it does not clear the image's dependency findings. It was not rerun for the source-only fallback.

The operator's separately authorized real Outlook ingestion test passed on 2026-10-29: one synthetic message, Graph retrieval, API `202`, persistence, fake classification, one `PendingReview` item, and identical replay `200` without duplication. Workflows were left unpublished and disabled. No new mailbox access occurred. External Gmail/Outlook draft creation remains **not live-validated**.

## Restoring runtime support later

Select a supported patched image and record its immutable inputs, inspect all critical/high dependency findings with actual applicability evidence, then repeat inactive imports and synthetic execution in a fresh isolated environment without provider credentials. Only then deliberately restore deployment configuration and document its supported scope. Do not restore the suspended image through an override file, hidden profile, or unverified library replacement.

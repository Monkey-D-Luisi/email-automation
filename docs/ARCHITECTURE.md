# Architecture

## Layers and dependencies

- `SmartInbox.Domain`: the `InboundMessage` aggregate, status, transitions, attachment metadata, and domain limits. It depends only on .NET.
- `SmartInbox.Application`: commands, queries, FluentValidation validators, validation behavior, `TimeProvider`, and the `IInboundMessageRepository` contract.
- `SmartInbox.Infrastructure`: EF Core configurations, `AppDbContext`, PostgreSQL migrations, repository, and validated `[IoC]` scanning.
- `SmartInbox.API`: composition, v1 contracts, bearer token authentication, HTTP limits, `ProblemDetails`, and Minimal API endpoints.

Application does not depend on Infrastructure. The API is the composition root and references Application and Infrastructure. The domain has no dependency on HTTP or EF Core.

## Aggregate and fingerprint

`InboundMessage.Receive` creates the initial `Received` status, obtains `ImportedAt` from `TimeProvider`, and converts both timestamps to UTC. `Source` is trimmed and lowercased; `MailboxId`, `ExternalMessageId`, and `ExternalThreadId` preserve their casing because they are opaque external IDs. The body and subject are preserved without rewriting. Empty optional fields become `null`; MIME types are trimmed and lowercased.

The SHA-256 fingerprint is calculated from a deterministic JSON representation of `Source`, IDs, sender, subject, body, UTC `ReceivedAt`, and attachment metadata. It excludes the internal ID, status, and import time. Thus, a repeated message retains the same fingerprint when it arrives in a later run.

The status flow allows `Received -> Processing -> Processed/Failed` and `Failed -> Processing`. There is no processor, classification, draft, or action aggregate in this phase.

## Idempotency and transactions

PostgreSQL enforces the unique index `ux_inbound_messages_source_mailbox_external_message_id` on `(Source, MailboxId, ExternalMessageId)`. The application does not depend on a prior lookup.

1. The repository attempts to insert the message.
2. If PostgreSQL commits the write, the API returns `202 Accepted`.
3. If PostgreSQL returns `23505` for the idempotency index, the repository detaches the failed entity and queries the persisted record.
4. If the record exists and the fingerprint matches, the API returns its ID with `200 OK`.
5. If the record exists but the fingerprint differs, the application returns `409 Conflict`. The row is not updated.
6. If the collision is unrelated to the expected index or the record cannot be found after the collision, the error propagates to the exception handler.

The concurrency test uses two independent scopes and `AppDbContext` instances against PostgreSQL. Another test inserts two rows directly to verify that PostgreSQL's unique index rejects the second row.

## Persistence

The initial migration creates `inbound_messages`, status and fingerprint constraints, and idempotency and date indexes. Timestamps use `timestamp with time zone`; the repository writes only UTC `DateTimeOffset` values. Normalized attachment metadata is stored as `jsonb`; there is no column for file bytes.

Migrations are applied explicitly with `dotnet ef database update`. No environment runs implicit migrations when the API starts.

## API and security

- `POST /api/v1/messages`, `GET /api/v1/messages/{id}`, and `GET /api/v1/messages` require a bearer token configured outside the repository.
- The token must be at least 32 characters long; comparison uses SHA-256 hashes and a fixed-time comparison.
- `GET /health` does not require a token and checks PostgreSQL connectivity.
- HTTP request bodies are limited to 65,536 bytes. Character-limited fields and pagination are validated in Application.
- Unknown JSON properties are rejected so unsupported binary fields cannot be silently accepted.
- Validation errors, conflicts, missing records, and unexpected failures produce stable responses. Internal exceptions and exception messages are not returned to clients.
- Application logs record the type of an unexpected exception, not its payload or message. EF Core sensitive-data logging is not enabled.

The token does not provide user identity, mailbox-level authorization, rotation, auditing, or abuse controls. This remains a private local demo.

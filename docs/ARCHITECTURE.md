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

The status flow allows `Received -> Processing -> Processed/Failed` and `Failed -> Processing`. Processing state, retries, lease data, and the single current classification belong to `InboundMessage`; no separate processing aggregate is needed. Classification is a bounded value object containing explicit facts, missing information, warnings, uncertainty, and typed review reasons. `RequiresReview` is derived from those reasons.

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

## Classification and provider boundary

The API persists an inbound message before background processing can claim it. `InboundMessageBackgroundService` starts no worker loops unless `EmailProcessing:Enabled` is true. Its bounded workers create a fresh dependency injection scope for each poll, so each claim and result write uses a short-lived `AppDbContext`.

`IEmailAnalyzer` selects the provider through `IEmailAnalysisProviderFactory` and applies `EmailAnalysisPolicy` to the returned candidate. Domain and Application contain no OpenAI SDK types. The OpenAI adapter uses the official .NET SDK Responses API with strict Structured Outputs JSON Schema. Email subject and body are explicitly treated as untrusted input. Provider output is integration data; it is parsed, size-checked, enum-checked, and validated against source text before becoming a domain classification.

The policy rejects malformed candidates, maps unsupported category values to `Unknown`, and requires review for unknown, ambiguous, incomplete, contradictory, unsupported, or warned results. Extracted fact values must occur in the source message. Elevated priorities require an exact source quote containing supported urgency language; unsupported elevations are downgraded and marked for review. `Processed` means processing completed, not that a person approved the classification.

## Background work, leases, and retries

`ClaimNextForProcessingAsync` selects one eligible row with PostgreSQL `FOR UPDATE SKIP LOCKED`, updates its attempt count and lease owner/expiry, and commits before any provider call. A second worker skips the locked row. A crashed worker's message becomes eligible after lease expiry, subject to the configured attempt limit. When the final lease expires, the row is marked failed with `RetryLimitReached`.

Completion and failure writes lock the row and require the current lease owner. A stale worker therefore cannot replace a result after another worker has reclaimed the message. Classification and message status are written in one transaction, with a one-to-one primary key on `email_classifications.MessageId`. The provider call never runs inside a database transaction. Idempotent ingestion returns the existing message and does not enqueue a second successful classification.

Connectivity, rate-limit, and timeout errors can retry after a fixed configured delay, up to the attempt limit. Authentication, unsupported provider/model, invalid structured output, and other non-transient errors stop immediately. Cancellation requested by the host propagates. The provider client retry policy is disabled so the persisted worker retry count remains the spending bound. Available provider/model, duration, token usage, and configured-price estimate are stored on the message; the database keeps the latest available provider metrics and total attempt count, not a full event history. Cost stays unknown unless matching pricing is configured.

The worker provides at-least-once execution around the external provider call. A process crash after the provider completes but before the classification transaction commits can repeat that in-flight request after lease expiry. Once a result is committed as `Processed`, normal retries and duplicate ingestion cannot claim it again.

The worker defaults to disabled. When enabled, startup validates the provider identity, required OpenAI key, model ID, output limit, processing limits, and pricing configuration. The OpenAI key is not included in repository configuration. Message endpoints remain protected by service authentication; no unauthenticated route can trigger provider spending.

## API and security

- `POST /api/v1/messages`, `GET /api/v1/messages/{id}`, and `GET /api/v1/messages` require a bearer token configured outside the repository.
- The token must be at least 32 characters long; comparison uses SHA-256 hashes and a fixed-time comparison.
- `GET /health` does not require a token and checks PostgreSQL connectivity.
- HTTP request bodies are limited to 65,536 bytes. Character-limited fields and pagination are validated in Application.
- Unknown JSON properties are rejected so unsupported binary fields cannot be silently accepted.
- Validation errors, conflicts, missing records, and unexpected failures produce stable responses. Internal exceptions and exception messages are not returned to clients.
- Application logs record the type of an unexpected exception, not its payload or message. EF Core sensitive-data logging is not enabled.
- Provider logs record outcome and bounded telemetry but omit email content, sender addresses, prompts, output bodies, API keys, and raw provider exception messages.

The token does not provide user identity, mailbox-level authorization, rotation, auditing, or abuse controls. Email bodies are stored as plain text. This remains a private local demo using synthetic content.

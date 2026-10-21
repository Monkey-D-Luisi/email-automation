# Handoff — Smart Inbox Automation, Phase 2

## Status

Phase 2 adds optional AI-assisted classification to the existing local application. It keeps the Phase 1 ingestion API and PostgreSQL idempotency behavior, adds a bounded domain classification value, an application validation policy, an OpenAI Responses provider, and an opt-in PostgreSQL-backed background worker. No email actions, frontend, external mailbox connectors, or additional AI providers are implemented.

The intended local branch is `feature/ai-email-classification`. This work has not been pushed and no pull request has been opened. Synthetic data and fake provider responses were used for automated verification. No billable OpenAI requests were made; model/API integration has not been live-verified with an account.

## Verification

- `dotnet restore SmartInbox.sln`: succeeded; all projects were up to date.
- `dotnet build SmartInbox.sln --configuration Release --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test SmartInbox.sln --configuration Release --no-build --no-restore`: **69/69 passed** — Domain 6, Application 31, Infrastructure 24, API 8. PostgreSQL integration tests ran against local PostgreSQL 17 using isolated schemas.
- `dotnet list SmartInbox.sln package --vulnerable --include-transitive`: no vulnerable packages reported by the configured NuGet sources.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.
- GitHub-hosted CI and live provider requests: not run.

Integration tests require `SMARTINBOX_TEST_CONNECTION_STRING` to point to a localhost PostgreSQL database whose name ends in `_test`. Each test fixture uses its own isolated schema and drops that schema at fixture shutdown. Tests use fake provider responses and do not require credentials.

## Local setup and controlled provider evaluation

Start PostgreSQL and configure the database and service token as described in [README.md](../README.md). Apply migrations explicitly before starting the API:

```powershell
dotnet tool restore
dotnet ef database update --project 'src/SmartInbox.Infrastructure/SmartInbox.Infrastructure.csproj' --startup-project 'src/SmartInbox.API/SmartInbox.API.csproj'
```

The worker defaults to disabled and the API can ingest and retrieve messages without an OpenAI credential. For a future controlled live evaluation, first confirm the provider and model account availability, then put the key in User Secrets or the process environment, set `EmailAnalysis:Provider=openai`, select the model under `OpenAI:Model`, review the bounded retry/concurrency/timeout limits, and explicitly set `EmailProcessing:Enabled=true`. A protected synthetic message can then be submitted through `POST /api/v1/messages`; poll `GET /api/v1/messages/{id}` for its classification and safe processing state. That evaluation can incur API charges. Do not use real customer email for this demo.

The automated evaluation harness in `tests/SmartInbox.Application.Tests/EmailAnalysis/synthetic-emails.jsonl` contains 31 synthetic cases. It applies deterministic validation to fixed candidate outputs; it is not a live model quality benchmark.

## Design and operational limits

- Each message has at most one current classification. A retryable failure stores its kind, safe message, next attempt time, and attempt count.
- PostgreSQL claims work with `FOR UPDATE SKIP LOCKED` and an expiring owner lease. Completion requires the current owner, preventing a reclaimed message's stale worker from overwriting the result.
- Provider I/O occurs outside database transactions. Completion writes the classification and processed state atomically.
- Transient connectivity, rate limits, and timeouts retry up to the configured limit. Permanent provider/authentication/schema errors stop without automatic retries.
- Available metrics preserve the latest provider call telemetry and total attempt count; the database does not retain a full per-attempt history. Estimated cost remains unknown unless model pricing is configured.
- Processing is at-least-once around external provider I/O. A crash after an OpenAI response but before the database commit can repeat that in-flight request after lease expiry; committed classifications and duplicate submissions do not trigger another analysis.
- Email content is stored as plain text. The shared service token does not provide user identity, mailbox-level authorization, rotation, or audit history.
- The OpenAI SDK adapter is covered by fake-client tests. Credentials, model entitlement, network connectivity, and live structured output have not been tested.

## Preparation for Phase 3

Keep classification immutable once processed. Phase 3 can add a human review queue and proposed drafts or work items, with explicit reviewer identity, approval state, and audit history. No sending, forwarding, CRM write, or other external action should be triggered by a classification alone.

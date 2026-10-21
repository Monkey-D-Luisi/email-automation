# Handoff — Smart Inbox Automation, Phase 1

## Status

Phase 1 is implemented in the independent `email-automation` repository. The solution contains the eight planned projects, the `InboundMessage` aggregate, CQRS with MediatR 12.5.0 and FluentValidation, EF Core/Npgsql persistence, a v1 API with service-token authentication, an initial migration, Docker Compose, CI, and documentation.

Message ingestion, retrieval, pagination, deduplication, conflicts, and concurrency were verified against PostgreSQL 17. The local API uses User Secrets; development values and `.env` remain outside the repository.

## Verification performed

- `dotnet restore SmartInbox.sln`: succeeded.
- `dotnet build SmartInbox.sln --configuration Release --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test SmartInbox.sln --configuration Release --no-build --no-restore`: **24/24 tests passed**, including API, unit, and PostgreSQL tests using independent contexts.
- `dotnet list SmartInbox.sln package --vulnerable --include-transitive`: no vulnerable packages were reported by the configured NuGet sources.
- Docker Compose PostgreSQL 17: container healthy on `127.0.0.1:5432`; `InitialCreate` migration applied to `smartinbox_dev`. Tests ran against `smartinbox_test`.
- PostgreSQL tests verified the real unique index, concurrent collisions, retries, conflict without overwriting, valid/missing/invalid authentication, and HTTP body limits.
- Local Kestrel smoke test: `/health` returned 200, an unauthenticated message request returned 401, a new POST returned 202, a repeated POST returned 200 with the same ID, a conflict returned 409, and GET/list returned 200; the original content remained unchanged. Two concurrent POSTs returned 202 and 200 with the same ID, and the list confirmed exactly one persisted row for the message.

The configured CI repeats restore, build, all tests with a PostgreSQL service, and NuGet auditing. GitHub Actions has not been run because this repository is not published.

## How to continue

1. In PowerShell, create `.env` from `.env.example` with a random local value for `SMARTINBOX_DB_PASSWORD`; Compose prepares `smartinbox_dev` and `smartinbox_test`.
2. Run `docker compose up -d` and store `ConnectionStrings:Database` and `ServiceAuthentication:Token` with `dotnet user-secrets set` for the API project.
3. Run `dotnet tool restore` and explicitly apply `dotnet ef database update`.
4. Start `dotnet run --project src/SmartInbox.API/SmartInbox.API.csproj` and try `docs/examples/synthetic-email.json`.
5. Run the full suite with `SMARTINBOX_TEST_CONNECTION_STRING` pointing to a dedicated database whose name ends in `_test`.

See [README.md](../README.md) for complete PowerShell commands.

## Current limitations and risks

- Email bodies are stored as readable text in PostgreSQL. There is no application-level encryption, retention policy, or automated deletion.
- MediatR 12.5.0 is retained to meet the Apache 2.0 license requirement; upstream support for major version 12 is not guaranteed, so review this choice before operational use.
- The shared token does not manage users, mailbox-level permissions, rotation, rate limiting, or access auditing.
- The demo is for localhost and synthetic data only; it is not suitable for public exposure or real customer email.
- Attachments contribute normalized metadata only; there is no download, reading, or persistence of attachment bytes.
- Gmail/Outlook, n8n, AI, drafts, sending, and a frontend are not implemented.
- The `document-processor` repository was available locally and informed the layer and IoC patterns. The requested `converso` URL returned 404, and no local copy was available, so no decisions were based on that repository.
- No repository was published and hosted CI was not run.

## Preparation for Phase 2

Define the classification use case, data policy, and retention before introducing an AI provider. Add a contract and implementation only when the use case is clear. Keep message bodies and prompts out of logs, use synthetic email for testing, and retain human approval for any future action.

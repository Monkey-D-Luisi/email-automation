# Smart Inbox Automation

Smart Inbox Automation is a local demo for receiving, validating, deduplicating, and querying normalized email messages. This delivery covers Phase 1 only: ingestion and persistence in PostgreSQL. Use synthetic messages only.

## Architecture

The solution uses .NET 10, ASP.NET Core Minimal API, CQRS with MediatR 12.5.0, FluentValidation, EF Core, and PostgreSQL.

```text
SmartInbox.API -> SmartInbox.Application -> SmartInbox.Domain
       |                    ^
       +-> SmartInbox.Infrastructure -+
                    |
                PostgreSQL
```

`Domain` contains the `InboundMessage` aggregate and its invariants. `Application` contains commands, queries, validators, and the repository contract. `Infrastructure` implements the repository, EF Core persistence, and `[IoC]` registration. `API` composes the layers and defines versioned HTTP contracts. No email-provider or AI integration is implemented.

The solution contains the eight required projects:

- `SmartInbox.Domain`, `SmartInbox.Application`, `SmartInbox.Infrastructure`, `SmartInbox.API`
- `SmartInbox.Domain.Tests`, `SmartInbox.Application.Tests`, `SmartInbox.Infrastructure.Tests`, `SmartInbox.API.Tests`

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for design decisions and boundaries, [docs/HANDOFF.md](docs/HANDOFF.md) for implementation status and continuation steps, and [docs/ROADMAP.md](docs/ROADMAP.md) for later phases.

## Requirements

- .NET 10 SDK
- Docker Desktop with Docker Compose
- PowerShell 7 for the commands in this guide

## Local setup

PostgreSQL is exposed only on `127.0.0.1:5432`. Compose creates separate `smartinbox_dev` and `smartinbox_test` databases; the local API uses the first and integration tests use the second. `.env.example` does not contain a usable password. On first run, create a local `.env` with a random password; do not commit it.

```powershell
if (-not (Test-Path '.env')) {
    $password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $values = Get-Content '.env.example' | ForEach-Object {
        if ($_ -like 'SMARTINBOX_DB_PASSWORD=*') { "SMARTINBOX_DB_PASSWORD=$password" } else { $_ }
    }
    Set-Content -LiteralPath '.env' -Value $values
}
docker compose up -d
```

Store a connection string and service token in User Secrets. The API project already has a `UserSecretsId`; these values stay outside the repository.

```powershell
$password = ((Get-Content '.env' | Where-Object { $_.StartsWith('SMARTINBOX_DB_PASSWORD=') }) -split '=', 2)[1]
$connection = "Host=127.0.0.1;Port=5432;Database=smartinbox_dev;Username=smartinbox;Password=$password"
$token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set 'ConnectionStrings:Database' $connection --project 'src/SmartInbox.API/SmartInbox.API.csproj'
dotnet user-secrets set 'ServiceAuthentication:Token' $token --project 'src/SmartInbox.API/SmartInbox.API.csproj'
```

Migrations do not run when the API starts. Apply them explicitly, then start the server:

```powershell
dotnet tool restore
$env:ConnectionStrings__Database = $connection
dotnet ef database update --project 'src/SmartInbox.Infrastructure/SmartInbox.Infrastructure.csproj' --startup-project 'src/SmartInbox.API/SmartInbox.API.csproj'
Remove-Item Env:ConnectionStrings__Database
dotnet run --project 'src/SmartInbox.API/SmartInbox.API.csproj'
```

The local API listens on `http://127.0.0.1:5168`. To use another environment, set `ConnectionStrings__Database` and `ServiceAuthentication__Token` through environment variables or User Secrets. The token must be at least 32 characters long.

## API v1

All endpoints under `/api/v1/messages` require `Authorization: Bearer <service-token>`. `/health` is public and checks PostgreSQL connectivity.

Example [synthetic email](docs/examples/synthetic-email.json):

```json
{
  "source": "demo",
  "mailboxId": "synthetic-mailbox-001",
  "externalMessageId": "demo-message-001",
  "externalThreadId": "demo-thread-001",
  "senderEmail": "alex@example.test",
  "senderName": "Alex Example",
  "subject": "Synthetic demo request",
  "bodyText": "This message is synthetic and is used to validate Smart Inbox Automation.",
  "receivedAt": "2026-10-21T09:00:00Z",
  "attachments": [
    {
      "externalAttachmentId": "demo-attachment-001",
      "name": "demo-budget.pdf",
      "contentType": "application/pdf",
      "sizeBytes": 12345
    }
  ]
}
```

`POST` returns `202 Accepted` when a message is first stored and `200 OK` when the same content is submitted again. Both responses include the same internal ID:

```json
{"id":"6a47dbcf-61fb-4a86-b91d-6adef46db57a","status":"Received"}
```

Reusing the same `(Source, MailboxId, ExternalMessageId)` combination with different content returns `409 Conflict` and leaves the original record unchanged:

```json
{
  "type": "https://smartinbox.local/problems/external-message-conflict",
  "title": "External message ID conflict",
  "status": 409,
  "detail": "The external message ID already exists with different content."
}
```

```powershell
$secretOutput = dotnet user-secrets list --project 'src/SmartInbox.API/SmartInbox.API.csproj' | Out-String
$token = [regex]::Match($secretOutput, 'ServiceAuthentication:Token\s*=\s*(.+)').Groups[1].Value.Trim()
$headers = @{ Authorization = "Bearer $token" }
$created = Invoke-WebRequest -Method Post -Uri 'http://127.0.0.1:5168/api/v1/messages' -Headers $headers -ContentType 'application/json' -InFile 'docs/examples/synthetic-email.json' -SkipHttpErrorCheck
$created.StatusCode
$id = ($created.Content | ConvertFrom-Json).id
Invoke-WebRequest -Uri "http://127.0.0.1:5168/api/v1/messages/$id" -Headers $headers -SkipHttpErrorCheck | Select-Object -ExpandProperty StatusCode
Invoke-WebRequest -Uri 'http://127.0.0.1:5168/api/v1/messages?pageNumber=1&pageSize=20' -Headers $headers -SkipHttpErrorCheck | Select-Object -ExpandProperty StatusCode
```

`GET /api/v1/messages/{id}` returns `404` when the internal ID does not exist. `GET /api/v1/messages` accepts `pageNumber` from 1 to 10,000 and `pageSize` from 1 to 100. Errors use `ProblemDetails`; the API does not return internal exceptions.

The API accepts up to 10 attachment metadata entries (external ID, name, MIME type, and size). It does not receive or store file bytes. Unknown JSON fields are rejected, including possible binary content fields.

Ingestion limits are: HTTP body size of 65,536 bytes, body text of 40,000 characters, subject of 998 characters, external IDs of 512 characters, and mailbox ID of 256 characters. Other limits are defined in `InboundMessageLimits` and validated before persistence.

## Tests, build, and package audit

Integration tests use PostgreSQL on localhost. The connection string must target a dedicated database whose name ends in `_test`; Compose creates `smartinbox_test`. Tests do not delete the database or its rows.

```powershell
$password = ((Get-Content '.env' | Where-Object { $_.StartsWith('SMARTINBOX_DB_PASSWORD=') }) -split '=', 2)[1]
$env:SMARTINBOX_TEST_CONNECTION_STRING = "Host=127.0.0.1;Port=5432;Database=smartinbox_test;Username=smartinbox;Password=$password"
dotnet restore SmartInbox.sln
dotnet build SmartInbox.sln --configuration Release --no-restore
dotnet test SmartInbox.sln --configuration Release --no-build --no-restore
dotnet list SmartInbox.sln package --vulnerable --include-transitive
Remove-Item Env:SMARTINBOX_TEST_CONNECTION_STRING
```

`dotnet tool restore` installs the pinned `dotnet-ef` version. GitHub Actions runs restore, Release build, unit and PostgreSQL integration tests, and a NuGet package audit. It does not use real credentials or external providers.

## Security and privacy

This application is limited to a local demo with synthetic data. Do not expose it to untrusted networks or process real customer email. Identity and authorization controls, credential rotation, retention and deletion, encryption, and operational controls would be needed before broader use. The email body is stored as plain text in PostgreSQL for this phase's ingestion requirement. Application logs do not include message bodies, recipients, tokens, or payloads.

Original code is distributed under the MIT License; MediatR 12.5.0 retains its Apache 2.0 license. See [LICENSE](LICENSE).

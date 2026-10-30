# Development setup on Windows

This guide targets the `Monkey-D-Luisi/email-automation` portfolio demonstration. The complete implementation is on `feature/outlook-graph-integration`. `main` remains the Phase 1 baseline; the other feature branches preserve earlier phases. The offline demo needs no Microsoft, Google, n8n, or OpenAI account.

Use synthetic email data only. This setup does not require OpenAI or Gmail/Outlook credentials and does not contact either provider or send email.

## Prerequisites

- Git for Windows: `winget install --id Git.Git --exact`, or use the [official installer](https://git-scm.com/install/windows).
- .NET 10 SDK: `winget install --id Microsoft.DotNet.SDK.10 --exact`. Verify with `dotnet --version`.
- A supported, patched Node.js release: **22.23.4 or a newer patched 22.x** for parity with CI and the [October 2026 security release](https://nodejs.org/en/blog/vulnerability/october-2026-security-releases). Install from the [official download page](https://nodejs.org/en/download) and verify with `node --version` and `npm --version`. The framework's older minimum version is not a security recommendation.
- Docker Desktop with Docker Compose. Install using the [official Windows guide](https://docs.docker.com/desktop/setup/install/windows-install/) and start Docker Desktop before running containers.
- PowerShell 7 is recommended for the commands below.
- Rider or VS Code is optional.

## Clone the complete implementation

Once the repository is public, cloning needs no GitHub sign-in. While it remains private, GitHub requires repository access. Run:

```powershell
git clone --branch feature/outlook-graph-integration https://github.com/Monkey-D-Luisi/email-automation.git
Set-Location email-automation
git status --short --branch
```

The Outlook branch contains the complete Phase 1–4 implementation plus the Outlook extension. Keep local changes and secrets out of commits.

## Start PostgreSQL

Compose binds PostgreSQL to `127.0.0.1:5432` and creates the development and test databases. Create a local `.env` with a random password, then start PostgreSQL:

```powershell
if (-not (Test-Path '.env')) {
    $dbPassword = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    Get-Content '.env.example' | ForEach-Object {
        if ($_ -like 'SMARTINBOX_DB_PASSWORD=*') { "SMARTINBOX_DB_PASSWORD=$dbPassword" } else { $_ }
    } | Set-Content -LiteralPath '.env'
}
docker compose up -d postgres
docker compose ps
```

The `.env` file and Docker volumes are local runtime state. Never commit them or copy real mailbox or customer data into the repository.

## Configure the API and reviewer

The API project has a stable User Secrets ID. These commands create local-only credentials and select the fake classifier so synthetic messages can reach the review queue without contacting OpenAI:

```powershell
$dbPassword = (Get-Content '.env' | Where-Object { $_.StartsWith('SMARTINBOX_DB_PASSWORD=') }) -split '=', 2 | Select-Object -Last 1
$connection = "Host=127.0.0.1;Port=5432;Database=smartinbox_dev;Username=smartinbox;Password=$dbPassword"
$serviceToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$reviewerPassword = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$apiProject = 'src/SmartInbox.API/SmartInbox.API.csproj'

dotnet user-secrets set 'ConnectionStrings:Database' $connection --project $apiProject
dotnet user-secrets set 'ServiceAuthentication:Token' $serviceToken --project $apiProject
dotnet user-secrets set 'ReviewAuthentication:ReviewerId' 'local-reviewer' --project $apiProject
dotnet user-secrets set 'ReviewAuthentication:DisplayName' 'Local reviewer' --project $apiProject
dotnet user-secrets set 'ReviewAuthentication:Password' $reviewerPassword --project $apiProject
dotnet user-secrets set 'EmailProcessing:Enabled' 'true' --project $apiProject
dotnet user-secrets set 'EmailAnalysis:Provider' 'fake' --project $apiProject
dotnet user-secrets set 'EmailDrafting:Enabled' 'false' --project $apiProject
dotnet user-secrets set 'GmailIntegration:Enabled' 'false' --project $apiProject
dotnet user-secrets set 'OutlookIntegration:Enabled' 'false' --project $apiProject
Remove-Variable serviceToken
```

Keep the generic service token and reviewer password distinct. When an email integration is enabled, its ingestion and draft-export tokens must also be unique across all enabled authentication schemes; startup validation enforces this.

Use `$reviewerPassword` to sign in, save it in a local password manager, then clear the shell variable with `Remove-Variable reviewerPassword`. User Secrets are stored outside Git and are not restored by cloning the repository. The fake provider returns synthetic, review-required classifications. Do not switch to the OpenAI provider or enable AI draft generation unless you separately intend to configure and use a billable provider.

Environment variables can replace User Secrets entirely. In the terminal that runs migrations and the API, set `ConnectionStrings__Database`, `ServiceAuthentication__Token`, `ReviewAuthentication__ReviewerId`, `ReviewAuthentication__DisplayName`, and `ReviewAuthentication__Password` to your new local values, plus `EmailProcessing__Enabled=true`, `EmailAnalysis__Provider=fake`, `EmailDrafting__Enabled=false`, `GmailIntegration__Enabled=false`, and `OutlookIntegration__Enabled=false`. Environment variables take precedence over User Secrets. Keep them set until the API stops; when using this alternative, skip the `Remove-Item Env:ConnectionStrings__Database` cleanup below until then. Do not print or commit them. Keep the database password in the ignored `.env` for Compose; the API does not automatically load that file.

Restore the local EF tool and apply migrations explicitly:

```powershell
dotnet tool restore
$env:ConnectionStrings__Database = $connection
dotnet ef database update --project 'src/SmartInbox.Infrastructure/SmartInbox.Infrastructure.csproj' --startup-project $apiProject
Remove-Item Env:ConnectionStrings__Database
Remove-Variable dbPassword, connection
```

Migrations do not run automatically when the API starts.

## Run the API and web panel

In one PowerShell terminal, start the API:

```powershell
dotnet run --project 'src/SmartInbox.API/SmartInbox.API.csproj'
```

The API listens at `http://127.0.0.1:5168`. From the repository root in a second terminal, install the locked frontend dependencies and start Next.js:

```powershell
Set-Location .\web
npm ci
$env:SMARTINBOX_API_ORIGIN = 'http://127.0.0.1:5168'
npm run dev
```

Open `http://127.0.0.1:3000` and sign in with the reviewer password created above. Submit only the synthetic example in `docs/examples/synthetic-email.json` while using the local API.

## Run tests, build, and package audits

Integration tests require the dedicated `smartinbox_test` database created by Compose. They create isolated schemas and use synthetic data and fake providers.

From the repository root, set the test connection string and run the backend checks:

```powershell
$dbPassword = (Get-Content '.env' | Where-Object { $_.StartsWith('SMARTINBOX_DB_PASSWORD=') }) -split '=', 2 | Select-Object -Last 1
$env:SMARTINBOX_TEST_CONNECTION_STRING = "Host=127.0.0.1;Port=5432;Database=smartinbox_test;Username=smartinbox;Password=$dbPassword"
dotnet restore SmartInbox.sln
dotnet build SmartInbox.sln --configuration Release --no-restore
dotnet test SmartInbox.sln --configuration Release --no-build --no-restore
dotnet list SmartInbox.sln package --vulnerable --include-transitive
Remove-Item Env:SMARTINBOX_TEST_CONNECTION_STRING
Remove-Variable dbPassword
```

Run frontend, workflow fixture, and package checks:

```powershell
Push-Location web
npm ci
npm test
npm run build
npm audit
Pop-Location
```

GitHub Actions runs these checks on `main` and `feature/**` pushes and pull requests. CI uses an isolated PostgreSQL service and synthetic/fake provider paths; it needs no production secrets and makes no Gmail, Outlook, Graph, or OpenAI calls. The hosted result is separate from local test results; see [Handoff](HANDOFF.md) for the latest run.

## Optional n8n runtime suspended

This release supplies no runnable or recommended n8n image. Compose contains only PostgreSQL, and `.env.example` contains only its database settings. The four workflow templates and their offline tests remain available; see [the workflow reference](../integrations/n8n/README.md) and [release resolution](PUBLIC_RELEASE_REVIEW.md). Runtime restoration requires a supported patched image and isolated compatibility checks.

Complete the API, fake-classifier, reviewer, and frontend steps above for the portfolio demo. They need no n8n process, OAuth credential, or mailbox access. Existing configured n8n instances, credentials, and volumes are outside this setup and were preserved; do not use orphan-removal or volume-cleanup commands against them.

## Local state that Git does not restore

A clone contains source, documentation, workflow exports, and migrations. It does not contain:

- .NET User Secrets, reviewer passwords, service tokens, OAuth client secrets, or provider access and refresh tokens.
- The local `.env`, personal environment variables, n8n encrypted credentials, or the n8n encryption key.
- PostgreSQL or n8n Docker volumes, local databases, or local mailbox data.

Create new local credentials on each computer. Do not copy production or personal mailbox credentials, database backups, or secret volumes into Git.

## Troubleshooting

- If ports `5432`, `5168`, or `3000` are already bound, stop the conflicting local service or change the local port and matching API origin before retrying.
- Check PostgreSQL startup with `docker compose logs postgres` and `docker compose ps`.
- The API and Next.js run on the host; local browser requests use `127.0.0.1`. No host-wide API binding or Docker gateway access is needed for this source-only demo.
- A missing test database means the PostgreSQL container was initialized without `docker/init/01-create-test-database.sql`; recreate a new local volume only if it contains no data you need, or create the dedicated `smartinbox_test` database manually.
- A missing table means migrations have not been applied to the selected database. Run the explicit EF migration command above.

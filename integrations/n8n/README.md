# Gmail and n8n integration

This local integration uses two n8n workflows and the existing Smart Inbox API. The workflows are exported inactive, contain no credential references or mailbox addresses, and require managed HTTP credentials to be assigned after import. n8n 2.44.5 is pinned in Compose. Use synthetic messages and a dedicated Gmail test account only.

## Boundaries and workflow mapping

| Workflow node | Smart Inbox contract or effect |
| --- | --- |
| Gmail Trigger | Polls up to 10 full parsed messages per minute from `INBOX`; excludes sent, drafts, spam, and trash; does not alter read state or labels. |
| Manual Synthetic Gmail Event | Sends three distinct synthetic messages through the same mapper and API as a Gmail event. |
| Map and Validate Gmail Message | Uses Gmail provider `id` and `threadId`; prefers parsed plain text, safely strips HTML if needed, and emits bounded diagnostic reasons without logging message content. |
| Gmail API Ingestion | `POST /api/v1/integrations/gmail/messages`; the API supplies configured `SourceId` and `MailboxId`. It returns `202` for accepted, `200` for identical replay, and `409` for a conflicting replay. |
| Poll Pending Gmail Draft Exports | Runs every minute and calls `POST /api/v1/integrations/gmail/draft-exports/claim`. PostgreSQL owns eligibility, snapshot, uniqueness, and leases. |
| Create Synthetic Gmail Draft ID | In `fake` mode, creates only a synthetic ID. It does not call Google. |
| Create Gmail Draft | In `live` mode, uses Gmail `Draft/Create` with the claimed subject, body, and recipient. It has no automatic retry. |
| Record Gmail Draft ID / Record Provider Outcome Unknown | Calls the attempt-correlated complete or fail callback. A possibly successful Gmail call is marked ambiguous and is not reissued automatically. |

Only a reviewer can request an export, and only for the current approved revision. The persisted snapshot cannot be changed by n8n. Duplicate requests for the same revision, mailbox, and recipient return the existing export. The API audit records intent, claim, outcome, and manual reconciliation. A Gmail draft ID is an external draft artifact; it is not a sent email or delivery receipt.

The workflow creates a standalone draft. It does not reply in the original Gmail thread because the application does not retain the RFC headers required to establish that relationship safely. Attachments are represented as metadata where the Gmail parser supplies it; no bytes are downloaded or attached to outbound drafts.

## Local Docker setup

The Compose service is optional and localhost-bound. It uses a separate `smartinbox-n8n-data` volume and mounts these workflow files read-only. The existing PostgreSQL image, database, and data volume are unchanged.

1. Create `.env` from `.env.example` if needed. Set a local database password as described in the root README.
2. Apply the new migration and run PostgreSQL as usual.
3. Configure the API for the integration and the offline fake analyzer using User Secrets:

   ```powershell
   $ingestionToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   $draftExportToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   dotnet user-secrets set 'GmailIntegration:Enabled' 'true' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   dotnet user-secrets set 'GmailIntegration:SourceId' 'gmail' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   dotnet user-secrets set 'GmailIntegration:MailboxId' 'gmail-test@example.test' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   dotnet user-secrets set 'GmailIntegration:IngestionToken' $ingestionToken --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   dotnet user-secrets set 'GmailIntegration:DraftExportToken' $draftExportToken --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   dotnet user-secrets set 'EmailProcessing:Enabled' 'true' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   dotnet user-secrets set 'EmailAnalysis:Provider' 'fake' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   ```

   The fake analyzer always returns an ambiguous, `Unknown` result that requires human review. It never sends a provider request and is unsuitable for classifying real messages. `EmailDrafting:Enabled` stays false.

4. Put only the non-secret API origin and fake mode in the ignored local `.env` for the n8n container:

   ```powershell
   $envLines = Get-Content -LiteralPath '.env' | Where-Object { $_ -notmatch '^(SMARTINBOX_API_ORIGIN|SMARTINBOX_GMAIL_MODE|SMARTINBOX_GMAIL_INGESTION_TOKEN|SMARTINBOX_GMAIL_DRAFT_EXPORT_TOKEN)=' }
   $envLines += "SMARTINBOX_API_ORIGIN=http://host.docker.internal:5168"
   $envLines += 'SMARTINBOX_GMAIL_MODE=fake'
   Set-Content -LiteralPath '.env' -Value $envLines
   ```

5. Start the API on the host so Docker can reach it. The temporary host binding is broader than loopback; keep it limited to this local session and apply the host firewall policy appropriate to your machine. The host filter must allow the Docker DNS name:

   ```powershell
   $env:ASPNETCORE_URLS = 'http://0.0.0.0:5168'
   $env:AllowedHosts = 'host.docker.internal;localhost;127.0.0.1'
   dotnet run --project 'src/SmartInbox.API/SmartInbox.API.csproj'
   ```

6. In another terminal, start n8n and open `http://localhost:5678`:

   ```powershell
   docker compose --profile integrations up -d n8n
   ```

   The container maps `host.docker.internal` to the Docker host gateway, and its `SMARTINBOX_API_ORIGIN` uses that name. The n8n editor port is bound to `127.0.0.1`; its generated encryption key and OAuth credentials live in the named volume. Back up that volume securely if the credentials must survive host replacement; never commit it.

7. Import `gmail-ingestion.json` and `gmail-approved-draft-export.json` from `/opt/smartinbox/workflows` through the n8n editor. Both should remain inactive.

8. In n8n, create two separate **Bearer Auth** credentials with the raw service-token values (do not include the `Bearer ` prefix). Assign the ingestion credential to **Gmail API Ingestion** and **Retry Gmail API Ingestion**. Assign the draft-export credential to **Claim Gmail Export**, **Record Gmail Draft ID**, and **Record Provider Outcome Unknown**. The workflow files intentionally contain no credential references or values. Run the ingestion workflow manually from **Manual Synthetic Gmail Event**. It submits three independent synthetic message identities; repeat runs exercise the API's duplicate response. The fake analyzer classifies messages as `Unknown` for reviewer inspection. Create, edit, and approve an internal draft, confirm the recipient in the review panel, and select **Create Gmail Draft**. Run the export workflow manually; `fake` mode records a synthetic Gmail draft ID through the normal completion callback. Set `SMARTINBOX_GMAIL_MODE=fake-ambiguous` only when deliberately validating the unknown-outcome path; it calls the normal API failure callback without contacting Gmail. Replaying requests returns existing state rather than creating a second record.

To stop the temporary API listener, stop the `dotnet run` process. Stop n8n with `docker compose --profile integrations stop n8n`. The workflows remain inactive unless explicitly activated in n8n.

## Service-token storage and n8n security

The service tokens stay in .NET User Secrets on the API host and in n8n's managed **Bearer Auth** credentials. The workflow JSON does not contain tokens. n8n encrypts credential data in its database with `N8N_ENCRYPTION_KEY`; n8n generates a key on first launch unless an operator supplies one. Protect the n8n data volume and its backups, and store a custom encryption key separately from database backups. If the credential database and key are both exposed, the credentials can be decrypted.

n8n-managed credentials keep service tokens out of workflow exports and process environment variables, but they do not prevent an authorized workflow editor from using a credential in an HTTP Request node. Restrict editor/admin access and keep the separate ingestion and draft-export tokens scoped to their matching API policies. Do not export decrypted credential data.

The local Compose setup keeps `SMARTINBOX_API_ORIGIN` and `SMARTINBOX_GMAIL_MODE` in `.env` because the workflows use them in expressions. These values are not secrets. `N8N_BLOCK_ENV_ACCESS_IN_NODE=false` allows expressions and Code nodes to read container environment variables, so do not add service tokens or other secrets to the n8n environment. For live Gmail, use n8n-managed service-token credentials and managed Gmail OAuth credentials before activating either workflow. See n8n's [HTTP Request credential options](https://docs.n8n.io/integrations/builtin/credentials/httprequest/), [environment access controls](https://docs.n8n.io/hosting/configuration/environment-variables/security/), [credential encryption setting](https://docs.n8n.io/hosting/configuration/environment-variables/deployment/), and [workflow credential sharing rules](https://docs.n8n.io/workflows/sharing/).

## Google OAuth setup for a separately authorized live test

Do not do this for automated tests or fake mode. Use a dedicated test mailbox and obtain explicit authorization before authorizing OAuth or activating either live workflow.

1. Create or select a Google Cloud project and enable the Gmail API.
2. Configure the OAuth consent screen. For a personal test, select the appropriate user type and add the exact test mailbox as a test user. Public applications using Gmail restricted scopes may require Google's verification and a security assessment.
3. Create an OAuth client for a Web application. Add this authorized redirect URI exactly: `http://localhost:5678/rest/oauth2-credential/callback`.
4. In n8n, create two separate **Gmail OAuth2 API** credentials using that client ID and secret. Keep the secret in n8n only. In each credential, enable **Custom Scopes**:
   - Ingestion: `https://www.googleapis.com/auth/gmail.readonly`
   - Draft creation: `https://www.googleapis.com/auth/gmail.compose`
5. Authorize both credentials as the same dedicated test account. Set `GmailIntegration:MailboxId` to that account's email address and verify it matches both n8n credentials before importing/activating the live path. This MVP does not read the OAuth identity at runtime, so this account-to-mailbox check is an operator setup requirement.
6. In the n8n editor, select the read-only credential on **Gmail Trigger** and the compose credential on **Create Gmail Draft**. Keep both workflows inactive while reviewing the nodes and testing the API connection.
7. For live polling, set `SMARTINBOX_GMAIL_MODE=live`, keep `GmailIntegration:Enabled=true`, and activate the ingestion workflow only for the test mailbox. The export workflow can be activated only after an authorized reviewer has requested a draft. It only creates Gmail drafts; it has no send, reply, forward, delete, or trash node. Stop and deactivate both workflows to end the test.

The `gmail.compose` scope also grants permission to send mail. This workflow does not send, but OAuth does not enforce that limitation. Google's [Gmail scope reference](https://developers.google.com/workspace/gmail/api/auth/scopes) marks both `gmail.readonly` and `gmail.compose` as restricted; a public application may need OAuth verification and a security assessment. n8n stores OAuth tokens encrypted in its database and refreshes access tokens through its credential runtime. Google expires refresh tokens after seven days when an external consent screen remains in Testing and requests Gmail scopes. The Compose volume stores the n8n database and generated encryption key, so protect and back up the volume as a secret. Localhost OAuth redirect URIs are intended for local testing, not public deployment.

Rotate service tokens by updating the matching API User Secret and n8n **Bearer Auth** credential for each policy, then verify both paths before resuming the workflow. Disable the API integration, set `SMARTINBOX_GMAIL_MODE=disabled`, deactivate the workflows, and revoke the Google grants when ending a live test.

## Failure handling and reconciliation

- Ingestion accepts `202`, treats identical replay `200` as success, and reports conflicting replay `409` as an integration error. HTTP 429, 5xx, and transport failures use at most three attempts with exponential backoff. Authentication and validation errors are not retried. Diagnostics carry a bounded correlation ID and provider message ID, but not body or headers.
- Export claims expire into `NeedsReconciliation`, not back into the automatic queue. If a Gmail result is uncertain, search the configured account's Drafts using the confirmed recipient, subject, and approximate creation time. If found, record its Gmail draft ID in the reviewer panel. If no matching draft exists, check the mailbox and explicitly confirm that before using **Retry after checking Gmail**. A retry is a new create attempt and could still duplicate if the earlier outcome was misidentified; exactly-once creation is not promised.
- A Gmail error does not trigger a Gmail node retry. The workflow reports `ProviderOutcomeUnknown`; a failed completion callback is retried because recording the same callback is idempotent. If the callback remains unavailable, wait for lease expiry and reconcile rather than re-running Gmail creation.
- `401` from the Smart Inbox API usually means the wrong integration token was copied or the corresponding API option is disabled. A connection error to `host.docker.internal` means the API is not listening on the host gateway or `AllowedHosts` rejected the request. A missing table means apply the migration before enabling the integration.

## Automated offline checks

`npm test` also validates workflow exports, item correlation through accepted/duplicate/retry responses, bounded retry attempts, filters, managed credential configuration, API contracts, message mapping, the fake paths, and the absence of Gmail send or destructive nodes. These checks do not substitute for importing and executing in n8n. Live OAuth, Gmail access, and Gmail draft creation are never part of normal tests.

## Local execution evidence — 2026-10-22

- Imported both workflows into an isolated local `n8nio/n8n:2.44.5` volume and executed them with the n8n CLI. The temporary import placed each manual synthetic trigger first because `n8n execute --id` starts the first trigger in the workflow; the workflow graph and node parameters matched the checked-in exports. The workflows remained inactive.
- Ran ingestion through the manual synthetic event against the Smart Inbox API and an isolated schema in the local `smartinbox_test` database. The API responses were `202` for the accepted message, `200` for the pre-seeded duplicate, and a controlled `503` followed by `202` for the retryable message. Each request retained its own Gmail message ID and correlation ID. All three messages reached `Processed` and created separate `PendingReview` work items.
- Created an approved internal draft through the reviewer API, then executed the outbound workflow in `fake-ambiguous` mode. It claimed the export and recorded `ProviderOutcomeUnknown`; PostgreSQL showed `NeedsReconciliation`, one attempt, and no Gmail draft ID. The ambiguous branch made no Gmail API call.
- No Gmail OAuth, live Gmail polling, Gmail draft, email send, or OpenAI request was used. These results verify the local workflow graph and API contract; they do not verify OAuth account identity or Google API behavior.

# Outlook Graph test setup

This guide prepares a local, synthetic-first Outlook.com integration. The exported n8n workflows are inactive and both Outlook modes default to `disabled`. Do not activate a workflow until the account identity, test folder, cutoff, and API configuration have been checked.

The committed workflow and configuration examples use placeholders only. Set `SMARTINBOX_OUTLOOK_MAILBOX_ID` and `OutlookIntegration:MailboxId` locally to the same authorized mailbox address; do not commit a personal address, OAuth secret, or token.

## 1. Register a Microsoft application

1. Sign in to the Microsoft Entra admin center with the personal Microsoft account that will own the test integration. No Microsoft 365 organization is required for this personal-account test.
2. Register an application and choose the supported account type **Personal Microsoft accounts only**. The `consumers` authority used below is for personal Microsoft accounts; do not use an `organizations`-only authority.
3. Add a **Web** redirect URI matching the n8n credential callback exactly:

   ```text
   http://localhost:5678/rest/oauth2-credential/callback
   ```

   If n8n displays a different callback URL, register the exact URL displayed by n8n instead.
4. Add Microsoft Graph **delegated** permissions only. Create two OAuth credentials in n8n and request these scopes separately:

   | n8n credential | Delegated scopes |
   | --- | --- |
   | Outlook Graph read | `User.Read Mail.Read offline_access` |
   | Outlook Graph draft creation | `User.Read Mail.ReadWrite offline_access` |

   `User.Read` supports `/me` identity verification. `offline_access` lets n8n refresh stored delegated authorization. `Mail.ReadWrite` is broader than draft creation at the OAuth permission level, but the workflow only creates a draft. Do not add `Mail.Send`, application permissions, calendar, contacts, OneDrive, shared mailbox, or tenant-wide permissions.
5. Create a client secret in the Microsoft portal only if the n8n OAuth credential setup requires it. Copy it directly into n8n's credential form; never put it in this repository, a workflow export, `.env`, or chat. Record the secret expiry and rotate it through the portal and n8n when required.

Microsoft may change portal labels and personal-account registration availability. If the portal does not offer personal Microsoft accounts, stop and resolve the app registration account type before configuring n8n; do not switch the workflow to an organizational-only authority.

## 2. Configure delegated OAuth in n8n

Start the local n8n editor with `docker compose --profile integrations up -d n8n`; it is bound to `127.0.0.1:5678`. In **Credentials**, create two **OAuth2 API** credentials using Authorization Code:

| Setting | Outlook Graph read | Outlook Graph draft creation |
| --- | --- | --- |
| Authorization URL | `https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize` | same |
| Access Token URL | `https://login.microsoftonline.com/consumers/oauth2/v2.0/token` | same |
| Client ID | the registered application's ID | same |
| Client Secret | enter directly in n8n | enter directly in n8n |
| Scope | `User.Read Mail.Read offline_access` | `User.Read Mail.ReadWrite offline_access` |
| Client authentication | Body | Body |

Use the callback URI shown by the credential editor. Authorize each credential by signing in as the intended personal Outlook account and approving Microsoft's consent screen. n8n stores OAuth credentials encrypted in its database and refreshes delegated access when the provider returns a refresh token.

The local Compose volume contains n8n's encrypted credential database and its generated encryption key. Protect the whole volume and its backups. If you set `N8N_ENCRYPTION_KEY`, store that key separately from the volume backup; the database and key together can reveal credentials. Do not export decrypted credential data or share the n8n editor with untrusted users.

## 3. Prepare the isolated test scope

1. In Outlook, create a dedicated custom folder for this integration test. Keep only synthetic messages in it. Do not select Inbox, Sent Items, Drafts, Deleted Items, Junk Email, Outbox, or Archive.
2. Obtain that folder's Graph ID by signing in to [Graph Explorer](https://developer.microsoft.com/graph/graph-explorer) with the same account and requesting:

   ```http
   GET https://graph.microsoft.com/v1.0/me/mailFolders?$select=id,displayName
   ```

   Match the custom folder by `displayName` and copy its `id` directly to the local `.env`. This request uses delegated `Mail.Read`. The Outlook workflow rejects well-known system-folder names and addresses only the configured folder ID.
3. Set a UTC cutoff immediately before the synthetic test message. The workflow requests only messages with `receivedDateTime ge` that cutoff, requests text bodies and immutable Graph IDs, and follows Graph `@odata.nextLink` pages. It does not mark messages read, move them, delete them, or download attachment bytes.
4. Configure these local, non-secret `.env` values:

   ```text
   SMARTINBOX_OUTLOOK_MODE=disabled
   SMARTINBOX_OUTLOOK_MAILBOX_ID=<authorized-test-mailbox>
   SMARTINBOX_OUTLOOK_TEST_FOLDER_ID=<custom-folder-graph-id>
   SMARTINBOX_OUTLOOK_RECEIVED_AFTER=<UTC-ISO-8601-cutoff>
   ```

   Keep the mode `disabled` while importing and checking workflows. Both workflow exports are inactive, and the API's `OutlookIntegration:Enabled` defaults to false.

The fixed cutoff intentionally re-reads the small test folder on each poll. PostgreSQL message identity makes those replays idempotent. This MVP does not keep a durable Graph delta checkpoint; use it only for a small isolated test folder. A larger ongoing mailbox integration needs durable delta state and failure-safe checkpoint handling.

## 4. Configure Smart Inbox and workflow credentials

Keep Outlook service tokens in .NET User Secrets and n8n-managed **Bearer Auth** credentials. Use unique random credentials for each purpose. All enabled integration tokens must differ from each other, the generic service token, and the reviewer password; Gmail and Outlook source IDs must also differ when both integrations are enabled. Do not put Outlook tokens in `.env` or a workflow JSON.

```powershell
$outlookIngestionToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$outlookDraftExportToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set 'OutlookIntegration:Enabled' 'true' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
dotnet user-secrets set 'OutlookIntegration:SourceId' 'outlook' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
dotnet user-secrets set 'OutlookIntegration:MailboxId' '<authorized-test-mailbox>' --project 'src/SmartInbox.API/SmartInbox.API.csproj'
dotnet user-secrets set 'OutlookIntegration:IngestionToken' $outlookIngestionToken --project 'src/SmartInbox.API/SmartInbox.API.csproj'
dotnet user-secrets set 'OutlookIntegration:DraftExportToken' $outlookDraftExportToken --project 'src/SmartInbox.API/SmartInbox.API.csproj'
Remove-Variable outlookIngestionToken, outlookDraftExportToken
```

The API mailbox value must match the mailbox identity from Graph `/me`; the workflow checks it case-insensitively before live polling or claiming an Outlook export. The browser receives only enabled status and the configured mailbox address, never OAuth or service credentials. The API does not contact Graph.

Import `integrations/n8n/outlook-ingestion.json` and `integrations/n8n/outlook-approved-draft-export.json`. Assign **Outlook Graph read** to **Verify Outlook Mailbox**, **List Outlook Test Folder Messages**, and **Retry Outlook Test Folder Messages**. Assign **Outlook Graph draft creation** to **Verify Outlook Export Mailbox** and **Create Outlook Draft**. Create distinct Smart Inbox Bearer Auth credentials and assign the ingestion one to both Outlook API ingestion nodes and the export one to the claim, completion, and failure callback nodes.

For a synthetic-only workflow check, keep both workflows inactive. Run **Manual Synthetic Outlook Event** to send the synthetic Graph fixtures through Smart Inbox. For the outbound fake path, set `SMARTINBOX_OUTLOOK_MODE=fake` locally and run **Manual Synthetic Outlook Export Event** after a reviewer has created an approved Outlook export intent. `fake-ambiguous` exercises the unknown-outcome callback without calling Graph. Restore `disabled` after the local fake run.

## 5. Separately authorize a controlled live test

After you have personally authorized both OAuth credentials, verify that `/me` resolves to the configured mailbox and that the folder ID is the dedicated test folder. Then, only for the separately approved test:

1. Add one synthetic email to the isolated custom folder and use a cutoff immediately before its received time.
2. Set `SMARTINBOX_OUTLOOK_MODE=live`, set `OutlookIntegration:Enabled=true`, and activate only the Outlook ingestion workflow. Confirm one inbound message and its duplicate-safe replay in Smart Inbox, then deactivate polling.
3. Complete the existing human review, save and approve the exact internal draft revision, and explicitly confirm the Outlook destination, mailbox, and recipient in the review panel.
4. Activate or run the Outlook export workflow for that approved intent. It calls only `POST /me/messages`, accepts success only when Graph returns `201`, a nonempty ID, and `isDraft=true`, and records the ID with the attempt-scoped callback.
5. Check the intended mailbox's Drafts folder and audit history. Confirm the draft is unsent. Leave both workflows inactive unless ongoing operation is separately authorized.

The create request has no automatic retry. If Graph may have created a draft but n8n lost the response or callback, the export becomes `NeedsReconciliation`. Graph does not provide an idempotency key for this create call, so the workflow does not search all drafts or claim exactly-once creation. A reviewer must inspect the intended Drafts folder using the approved recipient, subject, content, and approximate time; record a positively identified draft ID or confirm no matching draft before explicitly requesting a retry.

Never paste a password, client secret, access token, refresh token, or MFA code into chat. No Microsoft sign-in, Graph request, live inbox poll, draft creation, or email send is part of normal automated tests.

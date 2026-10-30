# Security

Smart Inbox is a single-operator localhost demonstration for synthetic messages. It is not intended for public hosting or real customer mail. Provider integrations and AI processing are disabled by default. Enabling the fake classifier does not enable fake AI drafting: keep `EmailDrafting:Enabled=false` for an offline demonstration.

Report a suspected vulnerability privately using GitHub's **Report a vulnerability** option when available. Otherwise, open an issue asking the maintainer for a private reporting channel without including exploit details or sensitive data. Never attach credentials, mailbox exports, database dumps, browser diagnostics, or unredacted logs to a public issue.

Store fresh credentials outside Git, using environment variables or .NET User Secrets and n8n-managed credentials. Treat the n8n volume and backups as sensitive: its generated encryption key and credential database share the volume. n8n execution history may also contain message bodies and approved draft snapshots.

Before publishing any branch or tag, scan its full history with a current secret scanner. If a real credential is exposed, revoke or rotate it at its issuer first; removing a file or rewriting Git history does not revoke access. Review author/committer metadata separately because secret scanners do not flag personal contact information.

See [the release preparation report](docs/PUBLIC_RELEASE_REVIEW.md) for the reviewed scope and remaining release decisions, and [development setup](docs/DEVELOPMENT_SETUP.md) for the offline demo.

namespace SmartInbox.API.Authentication;

public sealed class GmailIntegrationOptions
{
    public const string SectionName = "GmailIntegration";
    public const string IngestionSchemeName = "GmailIngestion";
    public const string DraftExportSchemeName = "GmailDraftExport";
    public const string IngestionPolicyName = "GmailIngestionService";
    public const string DraftExportPolicyName = "GmailDraftExportService";

    public bool Enabled { get; set; }
    public string SourceId { get; set; } = "gmail";
    public string MailboxId { get; set; } = string.Empty;
    public string IngestionToken { get; set; } = string.Empty;
    public string DraftExportToken { get; set; } = string.Empty;
    public int ClaimLeaseSeconds { get; set; } = 300;
}

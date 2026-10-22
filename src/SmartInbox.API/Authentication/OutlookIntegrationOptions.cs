namespace SmartInbox.API.Authentication;

public sealed class OutlookIntegrationOptions
{
    public const string SectionName = "OutlookIntegration";
    public const string IngestionSchemeName = "OutlookIngestion";
    public const string DraftExportSchemeName = "OutlookDraftExport";
    public const string IngestionPolicyName = "OutlookIngestionService";
    public const string DraftExportPolicyName = "OutlookDraftExportService";

    public bool Enabled { get; set; }
    public string SourceId { get; set; } = "outlook";
    public string MailboxId { get; set; } = string.Empty;
    public string IngestionToken { get; set; } = string.Empty;
    public string DraftExportToken { get; set; } = string.Empty;
    public int ClaimLeaseSeconds { get; set; } = 300;

    public bool HasDistinctSourceIdFrom(GmailIntegrationOptions gmail) =>
        !Enabled || !gmail.Enabled ||
        !string.Equals(SourceId, gmail.SourceId, StringComparison.OrdinalIgnoreCase);

    public bool DraftExportConfigured => Enabled &&
        !string.IsNullOrWhiteSpace(MailboxId) &&
        !string.IsNullOrWhiteSpace(DraftExportToken);
}

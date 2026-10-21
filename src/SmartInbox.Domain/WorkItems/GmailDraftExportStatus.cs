namespace SmartInbox.Domain.WorkItems;

public enum GmailDraftExportStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
    NeedsReconciliation,
    Cancelled,
}

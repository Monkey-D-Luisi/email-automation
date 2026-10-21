namespace SmartInbox.Domain.WorkItems;

public enum WorkItemAuditAction
{
    Created,
    ReviewStarted,
    CategoryCorrected,
    PriorityCorrected,
    NotesChanged,
    MissingInformationChanged,
    FactVerified,
    FactCorrected,
    FactRejected,
    DraftCreated,
    DraftEdited,
    DraftApproved,
    DraftRejected,
    GmailDraftExportRequested,
    GmailDraftExportClaimed,
    GmailDraftExportSucceeded,
    GmailDraftExportFailed,
    GmailDraftExportNeedsReconciliation,
    GmailDraftExportCancelled,
    GmailDraftExportRetryAuthorized,
    GmailDraftExportReconciled,
    Resolved,
}

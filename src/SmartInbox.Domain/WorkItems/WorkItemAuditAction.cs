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
    Resolved,
}

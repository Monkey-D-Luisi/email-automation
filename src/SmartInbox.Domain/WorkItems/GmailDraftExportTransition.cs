namespace SmartInbox.Domain.WorkItems;

public enum GmailDraftExportTransition
{
    Applied,
    Duplicate,
    Stale,
    LeaseExpired,
}

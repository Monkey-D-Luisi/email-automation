using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal static class GmailDraftExportAudit
{
    public static string WorkerActorId(DraftExportProvider provider) => provider switch
    {
        DraftExportProvider.Gmail => "gmail-n8n",
        DraftExportProvider.Outlook => "outlook-n8n",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    public static WorkItemAuditAction Action(DraftExportProvider provider, WorkItemAuditAction gmailAction) => provider switch
    {
        DraftExportProvider.Gmail => gmailAction,
        DraftExportProvider.Outlook => gmailAction switch
        {
            WorkItemAuditAction.GmailDraftExportRequested => WorkItemAuditAction.OutlookDraftExportRequested,
            WorkItemAuditAction.GmailDraftExportClaimed => WorkItemAuditAction.OutlookDraftExportClaimed,
            WorkItemAuditAction.GmailDraftExportSucceeded => WorkItemAuditAction.OutlookDraftExportSucceeded,
            WorkItemAuditAction.GmailDraftExportFailed => WorkItemAuditAction.OutlookDraftExportFailed,
            WorkItemAuditAction.GmailDraftExportNeedsReconciliation => WorkItemAuditAction.OutlookDraftExportNeedsReconciliation,
            WorkItemAuditAction.GmailDraftExportCancelled => WorkItemAuditAction.OutlookDraftExportCancelled,
            WorkItemAuditAction.GmailDraftExportRetryAuthorized => WorkItemAuditAction.OutlookDraftExportRetryAuthorized,
            WorkItemAuditAction.GmailDraftExportReconciled => WorkItemAuditAction.OutlookDraftExportReconciled,
            _ => throw new ArgumentOutOfRangeException(nameof(gmailAction)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    public static WorkItemAuditRecord Create(
        GmailDraftExport export,
        string actorId,
        WorkItemAuditAction action,
        DateTimeOffset now,
        string summary) => Create(export.WorkItemId, export.Id, export.ApprovedRevision, actorId, action, now, summary);

    public static WorkItemAuditRecord Create(
        Guid workItemId,
        Guid exportId,
        int revision,
        string actorId,
        WorkItemAuditAction action,
        DateTimeOffset now,
        string summary) => new()
    {
        Id = Guid.NewGuid(),
        WorkItemId = workItemId,
        ActorId = actorId,
        Action = action.ToString(),
        OccurredAt = now.ToUniversalTime(),
        EntityId = exportId,
        Revision = revision,
        Summary = summary,
    };
}

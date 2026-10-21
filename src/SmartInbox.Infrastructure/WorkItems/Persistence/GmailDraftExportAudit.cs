using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal static class GmailDraftExportAudit
{
    public const string WorkerActorId = "gmail-n8n";

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

namespace SmartInbox.Domain.WorkItems;

public sealed record WorkItemAuditEntry(
    Guid Id,
    Guid WorkItemId,
    string ActorId,
    WorkItemAuditAction Action,
    DateTimeOffset OccurredAt,
    Guid? EntityId,
    int? Revision,
    string Summary);

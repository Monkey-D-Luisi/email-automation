namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class WorkItemAuditRecord
{
    public Guid Id { get; set; }
    public Guid WorkItemId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? EntityId { get; set; }
    public int? Revision { get; set; }
    public string Summary { get; set; } = string.Empty;
}

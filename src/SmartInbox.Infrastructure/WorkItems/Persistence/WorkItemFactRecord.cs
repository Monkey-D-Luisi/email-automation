namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class WorkItemFactRecord
{
    public Guid Id { get; set; }
    public Guid WorkItemId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string OriginalValue { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}

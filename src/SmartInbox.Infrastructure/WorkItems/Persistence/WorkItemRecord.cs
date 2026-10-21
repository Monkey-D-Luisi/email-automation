using SmartInbox.Infrastructure.Messages.Persistence;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class WorkItemRecord
{
    public Guid Id { get; set; }
    public Guid SourceMessageId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AssignedReviewerId { get; set; }
    public string EffectiveCategory { get; set; } = string.Empty;
    public string EffectivePriority { get; set; } = string.Empty;
    public string? ReviewerNotes { get; set; }
    public string MissingInformationJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public int Version { get; set; }
    public InboundMessageRecord SourceMessage { get; set; } = null!;
    public List<WorkItemFactRecord> Facts { get; set; } = [];
    public ResponseDraftRecord? Draft { get; set; }
    public List<WorkItemAuditRecord> AuditEntries { get; set; } = [];
}

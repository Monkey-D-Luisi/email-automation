namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class GmailDraftExportRecord
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "Gmail";
    public Guid WorkItemId { get; set; }
    public Guid DraftId { get; set; }
    public Guid DraftRevisionId { get; set; }
    public int ApprovedRevision { get; set; }
    public string DestinationMailboxId { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ContentFingerprint { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? GmailDraftId { get; set; }
    public string? FailureKind { get; set; }
}

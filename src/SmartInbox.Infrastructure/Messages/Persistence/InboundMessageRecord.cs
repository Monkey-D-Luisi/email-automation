namespace SmartInbox.Infrastructure.Messages.Persistence;

internal sealed class InboundMessageRecord
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string MailboxId { get; set; } = string.Empty;
    public string ExternalMessageId { get; set; } = string.Empty;
    public string? ExternalThreadId { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ContentFingerprint { get; set; } = string.Empty;
    public string AttachmentsJson { get; set; } = "[]";
    public int ProcessingAttemptCount { get; set; }
    public string? FailureKind { get; set; }
    public string? FailureMessage { get; set; }
    public bool ProcessingRetryable { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? ProcessingLeaseOwner { get; set; }
    public DateTimeOffset? ProcessingLeaseExpiresAt { get; set; }
    public string? LastProviderId { get; set; }
    public string? LastModel { get; set; }
    public long? LastDurationMilliseconds { get; set; }
    public int? LastInputTokens { get; set; }
    public int? LastCachedInputTokens { get; set; }
    public int? LastOutputTokens { get; set; }
    public decimal? LastEstimatedCostUsd { get; set; }
    public EmailClassificationRecord? Classification { get; set; }
}

namespace SmartInbox.Infrastructure.Persistence;

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
}

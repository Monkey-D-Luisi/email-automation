namespace SmartInbox.API.V1.Contracts;

public sealed record InboundMessageRequestV1
{
    public string? Source { get; init; }
    public string? MailboxId { get; init; }
    public string? ExternalMessageId { get; init; }
    public string? ExternalThreadId { get; init; }
    public string? SenderEmail { get; init; }
    public string? SenderName { get; init; }
    public string? Subject { get; init; }
    public string? BodyText { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public IReadOnlyList<AttachmentMetadataRequestV1?>? Attachments { get; init; }
}

public sealed record AttachmentMetadataRequestV1(
    string? ExternalAttachmentId,
    string? Name,
    string? ContentType,
    long? SizeBytes);

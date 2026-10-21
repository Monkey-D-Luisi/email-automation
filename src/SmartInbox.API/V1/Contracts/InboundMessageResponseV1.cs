namespace SmartInbox.API.V1.Contracts;

public sealed record InboundMessageResponseV1(
    Guid Id,
    string Source,
    string MailboxId,
    string ExternalMessageId,
    string? ExternalThreadId,
    string? SenderEmail,
    string? SenderName,
    string Subject,
    string BodyText,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ImportedAt,
    string Status,
    IReadOnlyList<AttachmentMetadataResponseV1> Attachments);

public sealed record AttachmentMetadataResponseV1(
    string ExternalAttachmentId,
    string Name,
    string ContentType,
    long SizeBytes);

public sealed record ReceiveInboundMessageResponseV1(Guid Id, string Status);

public sealed record InboundMessageListResponseV1(
    IReadOnlyList<InboundMessageResponseV1> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

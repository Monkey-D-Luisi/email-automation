using MediatR;

namespace SmartInbox.Application.Messages.ReceiveInboundMessage;

public sealed record InboundAttachmentInput(
    string ExternalAttachmentId,
    string Name,
    string ContentType,
    long SizeBytes);

public sealed record ReceiveInboundMessageCommand(
    string Source,
    string MailboxId,
    string ExternalMessageId,
    string? ExternalThreadId,
    string? SenderEmail,
    string? SenderName,
    string Subject,
    string BodyText,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<InboundAttachmentInput?>? Attachments) : IRequest<ReceiveInboundMessageResult>;

public sealed record ReceiveInboundMessageResult(Guid Id, bool Created);

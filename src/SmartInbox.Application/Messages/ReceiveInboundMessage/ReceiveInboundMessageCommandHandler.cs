using MediatR;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.ReceiveInboundMessage;

public sealed class ReceiveInboundMessageCommandHandler(
    IInboundMessageRepository repository,
    TimeProvider timeProvider)
    : IRequestHandler<ReceiveInboundMessageCommand, ReceiveInboundMessageResult>
{
    public async Task<ReceiveInboundMessageResult> Handle(
        ReceiveInboundMessageCommand command,
        CancellationToken cancellationToken)
    {
        var attachments = command.Attachments?.Select(attachment => new AttachmentMetadata(
            attachment!.ExternalAttachmentId,
            attachment.Name,
            attachment.ContentType,
            attachment.SizeBytes));
        var message = InboundMessage.Receive(
            command.Source,
            command.MailboxId,
            command.ExternalMessageId,
            command.ExternalThreadId,
            command.SenderEmail,
            command.SenderName,
            command.Subject,
            command.BodyText,
            command.ReceivedAt,
            timeProvider.GetUtcNow(),
            attachments);

        var result = await repository.AddIfAbsentAsync(message, cancellationToken);
        if (!StringComparer.Ordinal.Equals(
                result.Message.ContentFingerprint,
                message.ContentFingerprint))
            throw new ExternalMessageConflictException();

        return new ReceiveInboundMessageResult(result.Message.Id, result.Created);
    }
}

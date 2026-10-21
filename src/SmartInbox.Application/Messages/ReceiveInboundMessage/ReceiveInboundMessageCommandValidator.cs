using FluentValidation;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.ReceiveInboundMessage;

public sealed class ReceiveInboundMessageCommandValidator : AbstractValidator<ReceiveInboundMessageCommand>
{
    public ReceiveInboundMessageCommandValidator()
    {
        RuleFor(command => command.Source)
            .NotEmpty().MaximumLength(InboundMessageLimits.Source)
            .Matches("^[a-zA-Z0-9._-]+$");
        RuleFor(command => command.MailboxId).NotEmpty().MaximumLength(InboundMessageLimits.MailboxId);
        RuleFor(command => command.ExternalMessageId).NotEmpty().MaximumLength(InboundMessageLimits.ExternalId);
        RuleFor(command => command.ExternalThreadId).MaximumLength(InboundMessageLimits.ExternalId);
        RuleFor(command => command.SenderEmail)
            .MaximumLength(InboundMessageLimits.SenderEmail)
            .EmailAddress()
            .When(command => !string.IsNullOrWhiteSpace(command.SenderEmail));
        RuleFor(command => command.SenderName).MaximumLength(InboundMessageLimits.SenderName);
        RuleFor(command => command.Subject).NotNull().MaximumLength(InboundMessageLimits.Subject);
        RuleFor(command => command.BodyText).NotNull().MaximumLength(InboundMessageLimits.BodyText);
        RuleFor(command => command.ReceivedAt).NotEqual(default(DateTimeOffset));
        RuleFor(command => command.Attachments).Must(attachments =>
                attachments is null || attachments.Count <= InboundMessageLimits.AttachmentCount)
            .WithMessage($"No more than {InboundMessageLimits.AttachmentCount} attachment metadata entries are accepted.");
        RuleForEach(command => command.Attachments).NotNull();
        RuleForEach(command => command.Attachments).ChildRules(attachment =>
        {
            attachment.RuleFor(item => item!.ExternalAttachmentId)
                .NotEmpty().MaximumLength(InboundMessageLimits.ExternalId);
            attachment.RuleFor(item => item!.Name).NotEmpty().MaximumLength(InboundMessageLimits.AttachmentName);
            attachment.RuleFor(item => item!.ContentType).NotEmpty().MaximumLength(InboundMessageLimits.ContentType);
            attachment.RuleFor(item => item!.SizeBytes).GreaterThanOrEqualTo(0);
        }).When(command => command.Attachments is not null);
    }
}

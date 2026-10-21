using System.Security.Cryptography;
using System.Text.Json;

namespace SmartInbox.Domain.Messages;

public sealed class InboundMessage
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string MailboxId { get; private set; } = string.Empty;
    public string ExternalMessageId { get; private set; } = string.Empty;
    public string? ExternalThreadId { get; private set; }
    public string? SenderEmail { get; private set; }
    public string? SenderName { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string BodyText { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset ImportedAt { get; private set; }
    public InboundMessageStatus Status { get; private set; }
    public string ContentFingerprint { get; private set; } = string.Empty;
    public IReadOnlyList<AttachmentMetadata> Attachments { get; private set; } = [];

    private InboundMessage() { }

    public static InboundMessage Receive(
        string source,
        string mailboxId,
        string externalMessageId,
        string? externalThreadId,
        string? senderEmail,
        string? senderName,
        string subject,
        string bodyText,
        DateTimeOffset receivedAt,
        DateTimeOffset importedAt,
        IEnumerable<AttachmentMetadata>? attachments = null)
    {
        ValidateRequired(source, InboundMessageLimits.Source, nameof(source));
        ValidateRequired(mailboxId, InboundMessageLimits.MailboxId, nameof(mailboxId));
        ValidateRequired(externalMessageId, InboundMessageLimits.ExternalId, nameof(externalMessageId));
        ValidateRequired(subject, InboundMessageLimits.Subject, nameof(subject), allowEmpty: true);
        ValidateRequired(bodyText, InboundMessageLimits.BodyText, nameof(bodyText), allowEmpty: true);

        var normalizedSource = source.Trim().ToLowerInvariant();
        if (!normalizedSource.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            throw new ArgumentException("Source must be a provider identifier.", nameof(source));
        var normalizedSenderEmail = NormalizeOptional(senderEmail);
        var normalizedSenderName = NormalizeOptional(senderName);
        var normalizedThreadId = string.IsNullOrEmpty(externalThreadId) ? null : externalThreadId;
        var normalizedAttachments = NormalizeAttachments(attachments);
        var receivedAtUtc = receivedAt.ToUniversalTime();
        var importedAtUtc = importedAt.ToUniversalTime();

        return new InboundMessage
        {
            Id = Guid.NewGuid(),
            Source = normalizedSource,
            MailboxId = mailboxId,
            ExternalMessageId = externalMessageId,
            ExternalThreadId = normalizedThreadId,
            SenderEmail = normalizedSenderEmail,
            SenderName = normalizedSenderName,
            Subject = subject,
            BodyText = bodyText,
            ReceivedAt = receivedAtUtc,
            ImportedAt = importedAtUtc,
            Status = InboundMessageStatus.Received,
            Attachments = normalizedAttachments,
            ContentFingerprint = CreateFingerprint(
                normalizedSource,
                mailboxId,
                externalMessageId,
                normalizedThreadId,
                normalizedSenderEmail,
                normalizedSenderName,
                subject,
                bodyText,
                receivedAtUtc,
                normalizedAttachments),
        };
    }

    public void TransitionTo(InboundMessageStatus nextStatus)
    {
        var allowed = (Status, nextStatus) switch
        {
            (InboundMessageStatus.Received, InboundMessageStatus.Processing) => true,
            (InboundMessageStatus.Processing, InboundMessageStatus.Processed or InboundMessageStatus.Failed) => true,
            (InboundMessageStatus.Failed, InboundMessageStatus.Processing) => true,
            _ => false,
        };

        if (!allowed)
            throw new InvalidOperationException($"Cannot transition an inbound message from {Status} to {nextStatus}.");

        Status = nextStatus;
    }

    public static InboundMessage Restore(
        Guid id,
        string source,
        string mailboxId,
        string externalMessageId,
        string? externalThreadId,
        string? senderEmail,
        string? senderName,
        string subject,
        string bodyText,
        DateTimeOffset receivedAt,
        DateTimeOffset importedAt,
        InboundMessageStatus status,
        string contentFingerprint,
        IEnumerable<AttachmentMetadata> attachments)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A persisted message ID is required.", nameof(id));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (contentFingerprint.Length != 64 || !contentFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("A persisted content fingerprint must be a SHA-256 digest.", nameof(contentFingerprint));

        var message = Receive(
            source, mailboxId, externalMessageId, externalThreadId, senderEmail, senderName,
            subject, bodyText, receivedAt, importedAt, attachments);
        message.Id = id;
        message.Status = status;
        message.ContentFingerprint = contentFingerprint.ToLowerInvariant();
        return message;
    }

    private static string CreateFingerprint(
        string source,
        string mailboxId,
        string externalMessageId,
        string? externalThreadId,
        string? senderEmail,
        string? senderName,
        string subject,
        string bodyText,
        DateTimeOffset receivedAt,
        IReadOnlyList<AttachmentMetadata> attachments)
    {
        var payload = new FingerprintPayload(
            source,
            mailboxId,
            externalMessageId,
            externalThreadId,
            senderEmail,
            senderName,
            subject,
            bodyText,
            receivedAt,
            attachments);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, FingerprintJsonOptions)))
            .ToLowerInvariant();
    }

    private static IReadOnlyList<AttachmentMetadata> NormalizeAttachments(IEnumerable<AttachmentMetadata>? attachments)
    {
        var result = attachments?.ToArray() ?? [];
        if (result.Length > InboundMessageLimits.AttachmentCount)
            throw new ArgumentException("Too many attachment metadata entries.", nameof(attachments));
        if (result.Any(attachment => attachment is null))
            throw new ArgumentException("Attachment metadata entries cannot be null.", nameof(attachments));
        return Array.AsReadOnly(result);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void ValidateRequired(string? value, int maxLength, string parameterName, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maxLength)
            throw new ArgumentException($"{parameterName} is invalid.", parameterName);
    }

    private sealed record FingerprintPayload(
        string Source,
        string MailboxId,
        string ExternalMessageId,
        string? ExternalThreadId,
        string? SenderEmail,
        string? SenderName,
        string Subject,
        string BodyText,
        DateTimeOffset ReceivedAt,
        IReadOnlyList<AttachmentMetadata> Attachments);
}

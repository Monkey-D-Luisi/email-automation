using System.Security.Cryptography;
using System.Net.Mail;
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
    public string? ExternalInternetMessageId { get; private set; }
    public IReadOnlyList<string> ExternalRecipientEmails { get; private set; } = [];
    public string? SenderEmail { get; private set; }
    public string? SenderName { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string BodyText { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset ImportedAt { get; private set; }
    public InboundMessageStatus Status { get; private set; }
    public string ContentFingerprint { get; private set; } = string.Empty;
    public IReadOnlyList<AttachmentMetadata> Attachments { get; private set; } = [];
    public EmailClassification? Classification { get; private set; }
    public EmailAnalysisMetrics? AnalysisMetrics { get; private set; }
    public int ProcessingAttemptCount { get; private set; }
    public ProcessingFailureKind? FailureKind { get; private set; }
    public string? FailureMessage { get; private set; }
    public bool ProcessingRetryable { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

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
        IEnumerable<AttachmentMetadata>? attachments = null,
        string? externalInternetMessageId = null,
        IEnumerable<string>? externalRecipientEmails = null)
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
        var normalizedInternetMessageId = NormalizeInternetMessageId(externalInternetMessageId);
        var normalizedRecipients = NormalizeRecipients(externalRecipientEmails);
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
            ExternalInternetMessageId = normalizedInternetMessageId,
            ExternalRecipientEmails = normalizedRecipients,
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
            (InboundMessageStatus.Failed, InboundMessageStatus.Processing) => true,
            _ => false,
        };

        if (!allowed)
            throw new InvalidOperationException($"Cannot transition an inbound message from {Status} to {nextStatus}.");

        if (nextStatus == InboundMessageStatus.Processing)
        {
            ProcessingAttemptCount++;
            FailureKind = null;
            FailureMessage = null;
            ProcessingRetryable = false;
            NextAttemptAt = null;
        }
        Status = nextStatus;
    }

    public void CompleteProcessing(EmailClassification classification, EmailAnalysisMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(metrics);
        if (Status != InboundMessageStatus.Processing)
            throw new InvalidOperationException("Only a processing message can be completed.");
        if (Classification is not null)
            throw new InvalidOperationException("A completed classification cannot be replaced.");
        ValidateMetrics(metrics);

        Classification = classification;
        AnalysisMetrics = metrics;
        FailureKind = null;
        FailureMessage = null;
        ProcessingRetryable = false;
        NextAttemptAt = null;
        Status = InboundMessageStatus.Processed;
    }

    public void FailProcessing(
        ProcessingFailureKind failureKind,
        string safeMessage,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        EmailAnalysisMetrics? metrics = null)
    {
        if (Status != InboundMessageStatus.Processing)
            throw new InvalidOperationException("Only a processing message can fail.");
        if (!Enum.IsDefined(failureKind))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        if (string.IsNullOrWhiteSpace(safeMessage) || safeMessage.Length > InboundMessageLimits.ProcessingFailureMessage)
            throw new ArgumentException("The processing failure message is invalid.", nameof(safeMessage));
        if (retryable != (nextAttemptAt is not null))
            throw new ArgumentException("A retryable failure must have a retry time.", nameof(nextAttemptAt));
        if (metrics is not null)
            ValidateMetrics(metrics);

        FailureKind = failureKind;
        FailureMessage = safeMessage.Trim();
        ProcessingRetryable = retryable;
        NextAttemptAt = nextAttemptAt?.ToUniversalTime();
        if (metrics is not null)
            AnalysisMetrics = metrics;
        Status = InboundMessageStatus.Failed;
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
        IEnumerable<AttachmentMetadata> attachments,
        EmailClassification? classification = null,
        EmailAnalysisMetrics? analysisMetrics = null,
        int processingAttemptCount = 0,
        ProcessingFailureKind? failureKind = null,
        string? failureMessage = null,
        bool processingRetryable = false,
        DateTimeOffset? nextAttemptAt = null,
        string? externalInternetMessageId = null,
        IEnumerable<string>? externalRecipientEmails = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A persisted message ID is required.", nameof(id));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (contentFingerprint.Length != 64 || !contentFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("A persisted content fingerprint must be a SHA-256 digest.", nameof(contentFingerprint));
        if (processingAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(processingAttemptCount));
        if (failureKind is not null && !Enum.IsDefined(failureKind.Value))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        if (processingRetryable != (nextAttemptAt is not null))
            throw new ArgumentException("Persisted retry state is inconsistent.", nameof(nextAttemptAt));
        if ((status == InboundMessageStatus.Processed) != (classification is not null))
            throw new ArgumentException("A processed message must have exactly one classification.", nameof(classification));
        if (status == InboundMessageStatus.Failed && (failureKind is null || string.IsNullOrWhiteSpace(failureMessage)))
            throw new ArgumentException("A failed message must have safe failure information.", nameof(failureMessage));
        if (status != InboundMessageStatus.Failed && (failureKind is not null || failureMessage is not null || processingRetryable))
            throw new ArgumentException("Only failed messages can have failure information.", nameof(failureKind));
        if (status == InboundMessageStatus.Processed && analysisMetrics is null)
            throw new ArgumentException("A processed message must have analysis metrics.", nameof(analysisMetrics));
        if (analysisMetrics is not null)
            ValidateMetrics(analysisMetrics);

        var message = Receive(
            source, mailboxId, externalMessageId, externalThreadId, senderEmail, senderName,
            subject, bodyText, receivedAt, importedAt, attachments, externalInternetMessageId, externalRecipientEmails);
        message.Id = id;
        message.Status = status;
        message.ContentFingerprint = contentFingerprint.ToLowerInvariant();
        message.Classification = classification;
        message.AnalysisMetrics = analysisMetrics;
        message.ProcessingAttemptCount = processingAttemptCount;
        message.FailureKind = failureKind;
        message.FailureMessage = failureMessage;
        message.ProcessingRetryable = processingRetryable;
        message.NextAttemptAt = nextAttemptAt?.ToUniversalTime();
        return message;
    }

    private static void ValidateMetrics(EmailAnalysisMetrics metrics)
    {
        if (string.IsNullOrWhiteSpace(metrics.ProviderId) || metrics.ProviderId.Length > 64 ||
            string.IsNullOrWhiteSpace(metrics.Model) || metrics.Model.Length > 128 ||
            metrics.DurationMilliseconds < 0 || metrics.InputTokens < 0 || metrics.CachedInputTokens < 0 ||
            metrics.OutputTokens < 0 || metrics.EstimatedCostUsd < 0)
            throw new ArgumentException("The processing metrics are invalid.", nameof(metrics));
        if (metrics.InputTokens is not null && metrics.CachedInputTokens > metrics.InputTokens)
            throw new ArgumentException("Cached token count cannot exceed the input token count.", nameof(metrics));
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

    private static string? NormalizeInternetMessageId(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is not null && (normalized.Length > InboundMessageLimits.ExternalId || normalized.Contains('\r') || normalized.Contains('\n')))
            throw new ArgumentException("The RFC internet message ID is invalid.", nameof(value));
        return normalized;
    }

    private static IReadOnlyList<string> NormalizeRecipients(IEnumerable<string>? recipients)
    {
        var values = recipients?.ToArray() ?? [];
        if (values.Length > 100)
            throw new ArgumentException("The recipient metadata is invalid.", nameof(recipients));
        var normalized = new List<string>(values.Length);
        foreach (var address in values)
        {
            if (!MailAddress.TryCreate(address, out var parsed) || parsed is null || parsed.Address.Length > InboundMessageLimits.SenderEmail)
                throw new ArgumentException("The recipient metadata is invalid.", nameof(recipients));
            normalized.Add(parsed.Address);
        }
        return Array.AsReadOnly(normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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

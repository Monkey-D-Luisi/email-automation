namespace SmartInbox.API.V1.Messages.Contracts;

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
    IReadOnlyList<AttachmentMetadataResponseV1> Attachments,
    EmailClassificationResponseV1? Classification,
    EmailProcessingResponseV1 Processing);

public sealed record AttachmentMetadataResponseV1(
    string ExternalAttachmentId,
    string Name,
    string ContentType,
    long SizeBytes);

public sealed record ReceiveInboundMessageResponseV1(Guid Id, string Status);

public sealed record EmailClassificationResponseV1(
    string Category,
    string Priority,
    string Summary,
    string Language,
    IReadOnlyList<EmailFactResponseV1> Facts,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> UncertaintyReasons,
    IReadOnlyList<string> ReviewReasons,
    bool RequiresReview,
    DateTimeOffset ClassifiedAt);

public sealed record EmailFactResponseV1(string Type, string Value);

public sealed record EmailProcessingResponseV1(
    int AttemptCount,
    string? FailureKind,
    string? FailureMessage,
    bool Retryable,
    DateTimeOffset? NextAttemptAt,
    EmailAnalysisMetricsResponseV1? Metrics);

public sealed record EmailAnalysisMetricsResponseV1(
    string Provider,
    string Model,
    long DurationMilliseconds,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd);

public sealed record InboundMessageListResponseV1(
    IReadOnlyList<InboundMessageResponseV1> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

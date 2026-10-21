using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.API.V1.WorkItems.Contracts;

public sealed record WorkItemListItemResponseV1(
    Guid Id,
    Guid SourceMessageId,
    string Subject,
    string? SenderEmail,
    string? SenderName,
    DateTimeOffset ReceivedAt,
    string Status,
    string Category,
    string Priority,
    bool RequiresReview,
    string? AssignedReviewerId,
    int Version);

public sealed record WorkItemListResponseV1(
    IReadOnlyList<WorkItemListItemResponseV1> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

public sealed record WorkItemResponseV1(
    Guid Id,
    Guid SourceMessageId,
    string Status,
    string? AssignedReviewerId,
    string EffectiveCategory,
    string EffectivePriority,
    string? ReviewerNotes,
    IReadOnlyList<string> MissingInformation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    int Version,
    string Subject,
    string BodyText,
    string? SenderEmail,
    string? SenderName,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ImportedAt,
    string ProcessingStatus,
    int ProcessingAttemptCount,
    string? ProcessingFailureKind,
    string? ProcessingFailureMessage,
    OriginalClassificationResponseV1? OriginalClassification,
    IReadOnlyList<ReviewedFactResponseV1> Facts,
    ResponseDraftResponseV1? Draft);

public sealed record OriginalClassificationResponseV1(
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

public sealed record ReviewedFactResponseV1(
    Guid Id,
    string Type,
    string OriginalValue,
    string Value,
    string Status,
    DateTimeOffset? ModifiedAt,
    string? ModifiedBy);

public sealed record ResponseDraftResponseV1(
    Guid Id,
    string RecipientEmail,
    int CurrentRevision,
    IReadOnlyList<ResponseDraftRevisionResponseV1> Revisions);

public sealed record ResponseDraftRevisionResponseV1(
    Guid Id,
    int Revision,
    string Subject,
    string Body,
    string Status,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? Feedback,
    DraftGenerationMetricsResponseV1? GenerationMetrics);

public sealed record DraftGenerationMetricsResponseV1(
    string ProviderId,
    string Model,
    long DurationMilliseconds,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd);

public sealed record WorkItemHistoryResponseV1(IReadOnlyList<WorkItemAuditEntryResponseV1> Entries);

public sealed record WorkItemAuditEntryResponseV1(
    Guid Id,
    Guid WorkItemId,
    string ActorId,
    string Action,
    DateTimeOffset OccurredAt,
    Guid? EntityId,
    int? Revision,
    string Summary);

public sealed record ExpectedVersionRequestV1(int ExpectedVersion);

public sealed record UpdateReviewRequestV1(
    int ExpectedVersion,
    EmailCategory Category,
    EmailPriority Priority,
    string? Notes,
    IReadOnlyList<string> MissingInformation);

public sealed record CorrectFactRequestV1(int ExpectedVersion, string Value);

public sealed record SaveResponseDraftRequestV1(int ExpectedVersion, string Subject, string Body);

public sealed record RejectDraftRequestV1(int ExpectedVersion, int Revision, string? Feedback);

public sealed record ApproveDraftRequestV1(int ExpectedVersion, int Revision);

public sealed record WorkItemMutationResponseV1(int Version);

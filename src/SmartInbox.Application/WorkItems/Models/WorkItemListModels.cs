using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Models;

public sealed record WorkItemListFilter(
    int PageNumber,
    int PageSize,
    WorkItemStatus? Status,
    EmailCategory? Category,
    EmailPriority? Priority,
    bool? RequiresReview,
    DateTimeOffset? ReceivedAfter,
    DateTimeOffset? ReceivedBefore);

public sealed record WorkItemListEntry(
    Guid Id,
    Guid SourceMessageId,
    string Subject,
    string? SenderEmail,
    string? SenderName,
    DateTimeOffset ReceivedAt,
    WorkItemStatus Status,
    EmailCategory Category,
    EmailPriority Priority,
    bool RequiresReview,
    string? AssignedReviewerId,
    int Version);

public sealed record WorkItemPage(
    IReadOnlyList<WorkItemListEntry> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

public sealed record WorkItemDetails(WorkItem WorkItem, InboundMessage SourceMessage);

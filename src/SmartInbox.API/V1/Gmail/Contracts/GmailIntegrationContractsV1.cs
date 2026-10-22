using SmartInbox.Domain.WorkItems;

namespace SmartInbox.API.V1.Gmail.Contracts;

public sealed record GmailAttachmentRequestV1(
    string? ExternalAttachmentId,
    string? Name,
    string? ContentType,
    long? SizeBytes);

public sealed record GmailInboundMessageRequestV1(
    string? GmailMessageId,
    string? GmailThreadId,
    string? SenderEmail,
    string? SenderName,
    string? Subject,
    string? BodyText,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<GmailAttachmentRequestV1?>? Attachments);

public sealed record CreateGmailDraftExportRequestV1(
    int ExpectedVersion,
    int Revision,
    string DestinationMailboxId,
    string RecipientEmail,
    DraftExportProvider Provider = DraftExportProvider.Gmail);

public sealed record RetryGmailDraftExportRequestV1(
    int ExpectedVersion,
    bool ConfirmedNoDraftExists,
    DraftExportProvider Provider = DraftExportProvider.Gmail);

public sealed record ReconcileGmailDraftExportRequestV1(
    int ExpectedVersion,
    string? GmailDraftId,
    DraftExportProvider Provider = DraftExportProvider.Gmail,
    string? ExternalDraftId = null);

public sealed record FailGmailDraftExportRequestV1(
    Guid AttemptId,
    GmailDraftExportFailureKind FailureKind,
    DraftExportProvider Provider = DraftExportProvider.Gmail);

public sealed record CompleteGmailDraftExportRequestV1(
    Guid AttemptId,
    string? GmailDraftId,
    DraftExportProvider Provider = DraftExportProvider.Gmail,
    string? ExternalDraftId = null);

public sealed record GmailDraftExportListResponseV1(
    bool Enabled,
    string? DestinationMailboxId,
    IReadOnlyList<GmailDraftExportResponseV1> Exports,
    bool OutlookEnabled = false,
    string? OutlookDestinationMailboxId = null);

public sealed record GmailDraftExportResponseV1(
    Guid Id,
    int ApprovedRevision,
    string DestinationMailboxId,
    string RecipientEmail,
    string ContentFingerprint,
    string RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int AttemptCount,
    string? GmailDraftId,
    string? FailureKind,
    DraftExportProvider Provider = DraftExportProvider.Gmail,
    string? ExternalDraftId = null);

public sealed record ClaimGmailDraftExportResponseV1(GmailDraftExportClaimV1? Claim);

public sealed record GmailDraftExportClaimV1(
    Guid ExportId,
    Guid AttemptId,
    Guid WorkItemId,
    Guid DraftId,
    Guid DraftRevisionId,
    int ApprovedRevision,
    string DestinationMailboxId,
    string RecipientEmail,
    string Subject,
    string Body,
    string ContentFingerprint,
    DateTimeOffset LeaseExpiresAt,
    DraftExportProvider Provider = DraftExportProvider.Gmail);

public sealed record GmailDraftExportCallbackResponseV1(string Status);

using SmartInbox.Domain.WorkItems;

namespace SmartInbox.API.V1.Outlook.Contracts;

public sealed record OutlookAttachmentRequestV1(
    string? ExternalAttachmentId,
    string? Name,
    string? ContentType,
    long? SizeBytes);

public sealed record OutlookInboundMessageRequestV1(
    string? GraphMessageId,
    string? InternetMessageId,
    string? ConversationId,
    string? SenderEmail,
    string? SenderName,
    IReadOnlyList<string>? RecipientEmails,
    string? Subject,
    string? BodyText,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<OutlookAttachmentRequestV1?>? Attachments);

public sealed record OutlookCompleteDraftExportRequestV1(Guid AttemptId, string? ExternalDraftId);

public sealed record OutlookFailDraftExportRequestV1(Guid AttemptId, GmailDraftExportFailureKind FailureKind);

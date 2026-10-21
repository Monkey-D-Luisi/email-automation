using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal static class GmailDraftExportPersistenceMapper
{
    public static GmailDraftExportRecord ToRecord(GmailDraftExport export) => new()
    {
        Id = export.Id,
        WorkItemId = export.WorkItemId,
        DraftId = export.DraftId,
        DraftRevisionId = export.DraftRevisionId,
        ApprovedRevision = export.ApprovedRevision,
        DestinationMailboxId = export.DestinationMailboxId,
        RecipientEmail = export.RecipientEmail,
        Subject = export.Subject,
        Body = export.Body,
        ContentFingerprint = export.ContentFingerprint,
        RequestedBy = export.RequestedBy,
        CreatedAt = export.CreatedAt,
        UpdatedAt = export.UpdatedAt,
        CompletedAt = export.CompletedAt,
        Status = export.Status.ToString(),
        AttemptCount = export.AttemptCount,
        AttemptId = export.AttemptId,
        ClaimedAt = export.ClaimedAt,
        LeaseExpiresAt = export.LeaseExpiresAt,
        GmailDraftId = export.GmailDraftId,
        FailureKind = export.FailureKind?.ToString(),
    };

    public static GmailDraftExport ToDomain(GmailDraftExportRecord record) => GmailDraftExport.Restore(
        record.Id,
        record.WorkItemId,
        record.DraftId,
        record.DraftRevisionId,
        record.ApprovedRevision,
        record.DestinationMailboxId,
        record.RecipientEmail,
        record.Subject,
        record.Body,
        record.ContentFingerprint,
        record.RequestedBy,
        record.CreatedAt,
        record.UpdatedAt,
        record.CompletedAt,
        Enum.Parse<GmailDraftExportStatus>(record.Status),
        record.AttemptCount,
        record.AttemptId,
        record.ClaimedAt,
        record.LeaseExpiresAt,
        record.GmailDraftId,
        record.FailureKind is null ? null : Enum.Parse<GmailDraftExportFailureKind>(record.FailureKind));

    public static void Update(GmailDraftExportRecord record, GmailDraftExport export)
    {
        record.UpdatedAt = export.UpdatedAt;
        record.CompletedAt = export.CompletedAt;
        record.Status = export.Status.ToString();
        record.AttemptCount = export.AttemptCount;
        record.AttemptId = export.AttemptId;
        record.ClaimedAt = export.ClaimedAt;
        record.LeaseExpiresAt = export.LeaseExpiresAt;
        record.GmailDraftId = export.GmailDraftId;
        record.FailureKind = export.FailureKind?.ToString();
    }
}

using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Ports;

public interface IGmailDraftExportRepository
{
    Task<IReadOnlyList<GmailDraftExport>?> ListByWorkItemAsync(Guid workItemId, CancellationToken cancellationToken = default);
    Task<(GmailDraftExport Export, bool Created)> CreateOrGetAsync(
        GmailDraftExport export,
        int expectedWorkItemVersion,
        CancellationToken cancellationToken = default);
    Task<GmailDraftExport?> ClaimPendingAsync(string destinationMailboxId, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<GmailDraftExportTransition> CompleteAsync(
        Guid exportId,
        Guid attemptId,
        string gmailDraftId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<GmailDraftExportTransition> FailAsync(
        Guid exportId,
        Guid attemptId,
        GmailDraftExportFailureKind failureKind,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<GmailDraftExport?> RetryAfterManualCheckAsync(
        Guid workItemId,
        Guid exportId,
        int expectedWorkItemVersion,
        string destinationMailboxId,
        string reviewerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<GmailDraftExport?> ReconcileAsync(
        Guid workItemId,
        Guid exportId,
        int expectedWorkItemVersion,
        string destinationMailboxId,
        string reviewerId,
        string gmailDraftId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

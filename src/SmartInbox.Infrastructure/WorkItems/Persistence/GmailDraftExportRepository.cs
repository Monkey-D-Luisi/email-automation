using Microsoft.EntityFrameworkCore;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.WorkItems;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

[IoC(typeof(IGmailDraftExportRepository), IoCLifetime.Scoped)]
internal sealed class GmailDraftExportRepository(AppDbContext dbContext) : IGmailDraftExportRepository
{
    public async Task<IReadOnlyList<GmailDraftExport>?> ListByWorkItemAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.WorkItems.AnyAsync(item => item.Id == workItemId, cancellationToken))
            return null;
        var records = await dbContext.GmailDraftExports.AsNoTracking()
            .Where(export => export.WorkItemId == workItemId)
            .OrderByDescending(export => export.CreatedAt)
            .ThenBy(export => export.Id)
            .ToArrayAsync(cancellationToken);
        return records.Select(GmailDraftExportPersistenceMapper.ToDomain).ToArray();
    }

    public async Task<(GmailDraftExport Export, bool Created)> CreateOrGetAsync(
        GmailDraftExport export,
        int expectedWorkItemVersion,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var workItem = await LockWorkItemAsync(export.WorkItemId, cancellationToken)
            ?? throw new WorkItemNotFoundException();
        if (workItem.Version != expectedWorkItemVersion)
            throw new WorkItemConcurrencyException();

        var existing = await dbContext.GmailDraftExports.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Provider == export.Provider.ToString() &&
            item.DraftId == export.DraftId &&
            item.ApprovedRevision == export.ApprovedRevision &&
            item.DestinationMailboxId == export.DestinationMailboxId &&
            item.RecipientEmail == export.RecipientEmail, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return (GmailDraftExportPersistenceMapper.ToDomain(existing), false);
        }

        var record = GmailDraftExportPersistenceMapper.ToRecord(export);
        dbContext.GmailDraftExports.Add(record);
        dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
            export,
            export.RequestedBy,
            GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportRequested),
            export.CreatedAt,
            $"{export.Provider} draft export requested for approved revision {export.ApprovedRevision}."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (GmailDraftExportPersistenceMapper.ToDomain(record), true);
    }

    public Task<GmailDraftExport?> ClaimPendingAsync(
        string destinationMailboxId,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        ClaimPendingAsync(DraftExportProvider.Gmail, destinationMailboxId, leaseDuration, now, cancellationToken);

    public async Task<GmailDraftExport?> ClaimPendingAsync(
        DraftExportProvider provider,
        string destinationMailboxId,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var timestamp = now.ToUniversalTime();
        if (await ProcessOldestExpiredClaimAsync(provider, timestamp, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var candidate = await dbContext.GmailDraftExports.AsNoTracking()
            .Where(export => export.Provider == provider.ToString() && export.Status == GmailDraftExportStatus.Pending.ToString())
            .OrderBy(export => export.CreatedAt)
            .ThenBy(export => export.Id)
            .Select(export => new { export.Id, export.WorkItemId })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var workItemRecord = await LockWorkItemAsync(candidate.WorkItemId, cancellationToken);
        var record = await LockExportAsync(candidate.Id, cancellationToken, GmailDraftExportStatus.Pending);
        if (workItemRecord is null || record is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (!string.Equals(record.DestinationMailboxId, destinationMailboxId, StringComparison.Ordinal))
        {
            var exportForOldMailbox = GmailDraftExportPersistenceMapper.ToDomain(record);
            await CancelPendingExportAsync(record, exportForOldMailbox, timestamp,
                $"Pending {exportForOldMailbox.Provider} draft export cancelled because its destination is no longer configured.",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var draft = await LoadDraftAsync(workItemRecord.Id, cancellationToken);
        var revision = draft is null
            ? null
            : await LoadCurrentRevisionAsync(draft.Id, cancellationToken);
        var exportToClaim = GmailDraftExportPersistenceMapper.ToDomain(record);
        if (!IsStillCurrentApprovedRevision(workItemRecord, draft, revision, exportToClaim))
        {
            await CancelPendingExportAsync(record, exportToClaim, timestamp,
                $"Pending {exportToClaim.Provider} draft export cancelled because its approved revision is no longer current.",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var attemptId = Guid.NewGuid();
        exportToClaim.Claim(attemptId, timestamp, leaseDuration);
        GmailDraftExportPersistenceMapper.Update(record, exportToClaim);
        dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
            exportToClaim,
            GmailDraftExportAudit.WorkerActorId(exportToClaim.Provider),
            GmailDraftExportAudit.Action(exportToClaim.Provider, WorkItemAuditAction.GmailDraftExportClaimed),
            timestamp,
            $"{exportToClaim.Provider} draft export claimed for attempt {attemptId:D}."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return GmailDraftExportPersistenceMapper.ToDomain(record);
    }

    public Task<GmailDraftExportTransition> CompleteAsync(
        Guid exportId,
        Guid attemptId,
        string gmailDraftId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => CompleteAsync(
            DraftExportProvider.Gmail, exportId, attemptId, gmailDraftId, now, cancellationToken);

    public Task<GmailDraftExportTransition> CompleteAsync(
        DraftExportProvider provider,
        Guid exportId,
        Guid attemptId,
        string externalDraftId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => MutateCallbackAsync(
            exportId,
            provider,
            now,
            export => export.Complete(attemptId, externalDraftId, now),
            cancellationToken);

    public Task<GmailDraftExportTransition> FailAsync(
        Guid exportId,
        Guid attemptId,
        GmailDraftExportFailureKind failureKind,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => FailAsync(
            DraftExportProvider.Gmail, exportId, attemptId, failureKind, now, cancellationToken);

    public Task<GmailDraftExportTransition> FailAsync(
        DraftExportProvider provider,
        Guid exportId,
        Guid attemptId,
        GmailDraftExportFailureKind failureKind,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => MutateCallbackAsync(
            exportId,
            provider,
            now,
            export => export.Fail(attemptId, failureKind, now),
            cancellationToken);

    public Task<GmailDraftExport?> RetryAfterManualCheckAsync(
        Guid workItemId,
        Guid exportId,
        int expectedWorkItemVersion,
        string destinationMailboxId,
        string reviewerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => RetryAfterManualCheckAsync(
            workItemId, exportId, expectedWorkItemVersion, destinationMailboxId, reviewerId,
            DraftExportProvider.Gmail, now, cancellationToken);

    public async Task<GmailDraftExport?> RetryAfterManualCheckAsync(
        Guid workItemId,
        Guid exportId,
        int expectedWorkItemVersion,
        string destinationMailboxId,
        string reviewerId,
        DraftExportProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var workItem = await LockWorkItemAsync(workItemId, cancellationToken);
        if (workItem is null)
            return null;
        EnsureExpectedReviewerVersion(workItem, expectedWorkItemVersion, reviewerId);
        var record = await LockExportAsync(exportId, cancellationToken);
        if (record is null || record.WorkItemId != workItemId)
            return null;
        var export = GmailDraftExportPersistenceMapper.ToDomain(record);
        if (export.Provider != provider)
            return null;
        EnsureDestinationMailbox(export, destinationMailboxId);
        await LoadAndRequireCurrentApprovedRevisionAsync(workItem, export, cancellationToken);
        Apply(() => export.RequeueAfterManualCheck(true, now));
        GmailDraftExportPersistenceMapper.Update(record, export);
        dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
            export,
            reviewerId,
            GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportRetryAuthorized),
            now,
            $"Reviewer confirmed no {export.Provider} draft exists and authorized retry of revision {export.ApprovedRevision}."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return GmailDraftExportPersistenceMapper.ToDomain(record);
    }

    public Task<GmailDraftExport?> ReconcileAsync(
        Guid workItemId,
        Guid exportId,
        int expectedWorkItemVersion,
        string destinationMailboxId,
        string reviewerId,
        string gmailDraftId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => ReconcileAsync(
            workItemId, exportId, expectedWorkItemVersion, destinationMailboxId, reviewerId,
            gmailDraftId, DraftExportProvider.Gmail, now, cancellationToken);

    public async Task<GmailDraftExport?> ReconcileAsync(
        Guid workItemId,
        Guid exportId,
        int expectedWorkItemVersion,
        string destinationMailboxId,
        string reviewerId,
        string gmailDraftId,
        DraftExportProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var workItem = await LockWorkItemAsync(workItemId, cancellationToken);
        if (workItem is null)
            return null;
        EnsureExpectedReviewerVersion(workItem, expectedWorkItemVersion, reviewerId);
        var record = await LockExportAsync(exportId, cancellationToken);
        if (record is null || record.WorkItemId != workItemId)
            return null;
        var export = GmailDraftExportPersistenceMapper.ToDomain(record);
        if (export.Provider != provider)
            return null;
        EnsureDestinationMailbox(export, destinationMailboxId);
        if (export.Status == GmailDraftExportStatus.Succeeded && export.ExternalDraftId == gmailDraftId)
        {
            await transaction.CommitAsync(cancellationToken);
            return export;
        }
        Apply(() => export.Reconcile(gmailDraftId, now));
        GmailDraftExportPersistenceMapper.Update(record, export);
        dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
            export,
            reviewerId,
            GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportReconciled),
            now,
            $"Reviewer reconciled {export.Provider} draft export with provider draft ID {gmailDraftId}."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return GmailDraftExportPersistenceMapper.ToDomain(record);
    }

    private async Task<bool> ProcessOldestExpiredClaimAsync(
        DraftExportProvider provider,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var expired = await dbContext.GmailDraftExports.AsNoTracking()
            .Where(export => export.Provider == provider.ToString() && export.Status == GmailDraftExportStatus.InProgress.ToString() && export.LeaseExpiresAt <= timestamp)
            .OrderBy(export => export.LeaseExpiresAt)
            .Select(export => new { export.Id, export.WorkItemId })
            .FirstOrDefaultAsync(cancellationToken);
        if (expired is null)
            return false;

        // Finding an expired candidate ends this poll even if another worker already handled it.
        await LockWorkItemAsync(expired.WorkItemId, cancellationToken);
        var expiredRecord = await LockExportAsync(expired.Id, cancellationToken);
        if (expiredRecord is null)
            return true;
        var expiredExport = GmailDraftExportPersistenceMapper.ToDomain(expiredRecord);
        if (!expiredExport.ExpireLease(timestamp))
            return true;

        GmailDraftExportPersistenceMapper.Update(expiredRecord, expiredExport);
        dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
            expiredExport,
            GmailDraftExportAudit.WorkerActorId(expiredExport.Provider),
            GmailDraftExportAudit.Action(expiredExport.Provider, WorkItemAuditAction.GmailDraftExportNeedsReconciliation),
            timestamp,
            $"The {expiredExport.Provider} draft export claim expired; provider outcome requires manual reconciliation."));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CancelPendingExportAsync(
        GmailDraftExportRecord record,
        GmailDraftExport export,
        DateTimeOffset timestamp,
        string summary,
        CancellationToken cancellationToken)
    {
        export.CancelPending(timestamp);
        GmailDraftExportPersistenceMapper.Update(record, export);
        dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
            export,
            GmailDraftExportAudit.WorkerActorId(export.Provider),
            GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportCancelled),
            timestamp,
            summary));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<GmailDraftExportTransition> MutateCallbackAsync(
        Guid exportId,
        DraftExportProvider provider,
        DateTimeOffset now,
        Func<GmailDraftExport, GmailDraftExportTransition> transition,
        CancellationToken cancellationToken)
    {
        var workItemId = await dbContext.GmailDraftExports.AsNoTracking()
            .Where(export => export.Id == exportId)
            .Select(export => (Guid?)export.WorkItemId)
            .SingleOrDefaultAsync(cancellationToken);
        if (workItemId is null)
            return GmailDraftExportTransition.Stale;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockWorkItemAsync(workItemId.Value, cancellationToken);
        var record = await LockExportAsync(exportId, cancellationToken);
        if (record is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return GmailDraftExportTransition.Stale;
        }

        var export = GmailDraftExportPersistenceMapper.ToDomain(record);
        if (export.Provider != provider)
        {
            await transaction.CommitAsync(cancellationToken);
            return GmailDraftExportTransition.Stale;
        }
        var result = transition(export);
        if (result is GmailDraftExportTransition.Applied or GmailDraftExportTransition.LeaseExpired)
        {
            GmailDraftExportPersistenceMapper.Update(record, export);
            var action = export.Status switch
            {
                GmailDraftExportStatus.Succeeded => GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportSucceeded),
                GmailDraftExportStatus.Failed => GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportFailed),
                _ => GmailDraftExportAudit.Action(export.Provider, WorkItemAuditAction.GmailDraftExportNeedsReconciliation),
            };
            var summary = export.Status switch
            {
                GmailDraftExportStatus.Succeeded => $"{export.Provider} draft export succeeded with provider draft ID {export.ExternalDraftId}.",
                GmailDraftExportStatus.Failed => $"{export.Provider} draft export failed before the provider call started.",
                _ => $"{export.Provider} draft creation may have succeeded; manual reconciliation is required.",
            };
            dbContext.WorkItemAuditEntries.Add(GmailDraftExportAudit.Create(
                export, GmailDraftExportAudit.WorkerActorId(export.Provider), action, now, summary));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    // A tracking raw-SQL query can reuse scoped state without applying the locked row's values.
    private async Task<WorkItemRecord?> LockWorkItemAsync(Guid workItemId, CancellationToken cancellationToken)
    {
        var workItem = await dbContext.WorkItems
            .FromSqlInterpolated($"SELECT * FROM \"work_items\" WHERE \"Id\" = {workItemId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (workItem is not null)
            await dbContext.Entry(workItem).ReloadAsync(cancellationToken);
        return workItem;
    }

    private async Task<GmailDraftExportRecord?> LockExportAsync(
        Guid exportId,
        CancellationToken cancellationToken,
        GmailDraftExportStatus? status = null)
    {
        GmailDraftExportRecord? export;
        if (status is null)
            export = await dbContext.GmailDraftExports
                .FromSqlInterpolated($"SELECT * FROM \"gmail_draft_exports\" WHERE \"Id\" = {exportId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        else
            export = await dbContext.GmailDraftExports
                .FromSqlInterpolated($"SELECT * FROM \"gmail_draft_exports\" WHERE \"Id\" = {exportId} AND \"Status\" = {status.Value.ToString()} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        if (export is not null)
            await dbContext.Entry(export).ReloadAsync(cancellationToken);
        return export;
    }

    private Task<ResponseDraftRecord?> LoadDraftAsync(Guid workItemId, CancellationToken cancellationToken) =>
        dbContext.ResponseDrafts.AsNoTracking()
            .SingleOrDefaultAsync(draft => draft.WorkItemId == workItemId, cancellationToken);

    private Task<ResponseDraftRevisionRecord?> LoadCurrentRevisionAsync(Guid draftId, CancellationToken cancellationToken) =>
        dbContext.ResponseDraftRevisions.AsNoTracking()
            .Where(revision => revision.ResponseDraftId == draftId)
            .OrderByDescending(revision => revision.Revision)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task LoadAndRequireCurrentApprovedRevisionAsync(
        WorkItemRecord workItem,
        GmailDraftExport export,
        CancellationToken cancellationToken)
    {
        var draft = await LoadDraftAsync(workItem.Id, cancellationToken);
        var revision = draft is null ? null : await LoadCurrentRevisionAsync(draft.Id, cancellationToken);
        if (!IsStillCurrentApprovedRevision(workItem, draft, revision, export))
            throw new WorkItemStateConflictException("The exported revision is no longer the current approved revision.");
    }

    private static bool IsStillCurrentApprovedRevision(
        WorkItemRecord workItem,
        ResponseDraftRecord? draft,
        ResponseDraftRevisionRecord? revision,
        GmailDraftExport export) =>
        workItem.Status == "InReview" &&
        string.Equals(workItem.AssignedReviewerId, export.RequestedBy, StringComparison.Ordinal) &&
        draft?.Id == export.DraftId &&
        revision is not null &&
        revision.Id == export.DraftRevisionId &&
        revision.Revision == export.ApprovedRevision &&
        revision.Status == "Approved";

    private static void EnsureExpectedReviewerVersion(WorkItemRecord workItem, int expectedVersion, string reviewerId)
    {
        if (workItem.Version != expectedVersion)
            throw new WorkItemConcurrencyException();
        if (workItem.Status != "InReview" || !string.Equals(workItem.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
            throw new WorkItemStateConflictException("The work item is not assigned to this reviewer.");
    }

    private static void EnsureDestinationMailbox(GmailDraftExport export, string configuredMailboxId)
    {
        if (!string.Equals(export.DestinationMailboxId, configuredMailboxId, StringComparison.Ordinal))
            throw new WorkItemStateConflictException("This export targets a mailbox that is no longer configured.");
    }

    private static void Apply(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkItemStateConflictException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new WorkItemValidationException(exception.Message, exception);
        }
    }
}

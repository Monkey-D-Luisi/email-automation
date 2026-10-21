using Microsoft.EntityFrameworkCore;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.WorkItems;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

[IoC(typeof(IWorkItemRepository), IoCLifetime.Scoped)]
internal sealed class WorkItemRepository(AppDbContext dbContext) : IWorkItemRepository
{
    public async Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await WithDetails(dbContext.WorkItems.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return record is null ? null : WorkItemPersistenceMapper.ToDomain(record);
    }

    public async Task<WorkItemPage> ListAsync(WorkItemListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = dbContext.WorkItems.AsNoTracking().AsQueryable();
        if (filter.Status is not null)
            query = query.Where(item => item.Status == filter.Status.ToString());
        if (filter.Category is not null)
            query = query.Where(item => item.EffectiveCategory == filter.Category.ToString());
        if (filter.Priority is not null)
            query = query.Where(item => item.EffectivePriority == filter.Priority.ToString());
        if (filter.ReceivedAfter is not null)
            query = query.Where(item => item.SourceMessage.ReceivedAt >= filter.ReceivedAfter);
        if (filter.ReceivedBefore is not null)
            query = query.Where(item => item.SourceMessage.ReceivedAt <= filter.ReceivedBefore);
        if (filter.RequiresReview is not null)
            query = query.Where(item => item.SourceMessage.Classification!.RequiresReview == filter.RequiresReview);

        var total = await query.LongCountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(item => item.SourceMessage.ReceivedAt)
            .ThenBy(item => item.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(item => new
            {
                item.Id,
                item.SourceMessageId,
                item.SourceMessage.Subject,
                item.SourceMessage.SenderEmail,
                item.SourceMessage.SenderName,
                item.SourceMessage.ReceivedAt,
                item.Status,
                item.EffectiveCategory,
                item.EffectivePriority,
                item.SourceMessage.Classification!.RequiresReview,
                item.AssignedReviewerId,
                item.Version})
            .ToArrayAsync(cancellationToken);

        return new WorkItemPage(records.Select(item => new WorkItemListEntry(
            item.Id,
            item.SourceMessageId,
            item.Subject,
            item.SenderEmail,
            item.SenderName,
            item.ReceivedAt,
            Enum.Parse<WorkItemStatus>(item.Status),
            Enum.Parse<SmartInbox.Domain.Messages.EmailCategory>(item.EffectiveCategory),
            Enum.Parse<SmartInbox.Domain.Messages.EmailPriority>(item.EffectivePriority),
            item.RequiresReview,
            item.AssignedReviewerId,
            item.Version)).ToArray(), filter.PageNumber, filter.PageSize, total);
    }

    public async Task<IReadOnlyList<WorkItemAuditEntry>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.WorkItems.AnyAsync(item => item.Id == id, cancellationToken))
            return null;
        var entries = await dbContext.WorkItemAuditEntries.AsNoTracking()
            .Where(entry => entry.WorkItemId == id)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .ToArrayAsync(cancellationToken);
        return entries.Select(WorkItemPersistenceMapper.ToDomain).ToArray();
    }

    public async Task SaveAsync(WorkItem workItem, CancellationToken cancellationToken = default)
    {
        var record = await WithDetails(dbContext.WorkItems)
            .SingleOrDefaultAsync(item => item.Id == workItem.Id, cancellationToken)
            ?? throw new WorkItemConcurrencyException();
        if (record.Version != workItem.Version - 1)
            throw new WorkItemConcurrencyException($"Work item version mismatch. Current version is {record.Version}; submitted version was {workItem.Version - 1}.");

        var auditCount = record.AuditEntries.Count;
        var existingDraftRevisionIds = record.Draft?.Revisions.Select(revision => revision.Id).ToHashSet() ?? [];
        var creatingDraft = record.Draft is null && workItem.Draft is not null;
        WorkItemPersistenceMapper.Update(record, workItem);
        if (creatingDraft)
            dbContext.ResponseDrafts.Add(record.Draft!);
        else if (record.Draft is not null)
            dbContext.ResponseDraftRevisions.AddRange(record.Draft.Revisions
                .Where(revision => !existingDraftRevisionIds.Contains(revision.Id)));
        dbContext.WorkItemAuditEntries.AddRange(record.AuditEntries.Skip(auditCount));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new WorkItemConcurrencyException();
        }
    }

    private static IQueryable<WorkItemRecord> WithDetails(IQueryable<WorkItemRecord> query) => query
        .AsSplitQuery()
        .Include(item => item.Facts)
        .Include(item => item.Draft)
        .ThenInclude(draft => draft!.Revisions);
}

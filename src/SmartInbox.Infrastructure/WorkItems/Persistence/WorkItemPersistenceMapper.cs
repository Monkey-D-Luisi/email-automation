using System.Text.Json;
using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal static class WorkItemPersistenceMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WorkItem ToDomain(WorkItemRecord record) => WorkItem.Restore(
        record.Id,
        record.SourceMessageId,
        Enum.Parse<WorkItemStatus>(record.Status),
        record.AssignedReviewerId,
        Enum.Parse<EmailCategory>(record.EffectiveCategory),
        Enum.Parse<EmailPriority>(record.EffectivePriority),
        record.ReviewerNotes,
        JsonSerializer.Deserialize<string[]>(record.MissingInformationJson, JsonOptions) ?? [],
        record.CreatedAt,
        record.UpdatedAt,
        record.ResolvedAt,
        record.ResolvedBy,
        record.Version,
        record.Facts.OrderBy(fact => fact.Id).Select(fact => ReviewedFact.Restore(
            fact.Id,
            Enum.Parse<EmailFactType>(fact.Type),
            fact.OriginalValue,
            fact.Value,
            Enum.Parse<ReviewedFactStatus>(fact.Status),
            fact.ModifiedAt,
            fact.ModifiedBy)),
        record.Draft is null ? null : ToDomain(record.Draft));

    public static WorkItemRecord ToRecord(WorkItem item)
    {
        var record = new WorkItemRecord { Id = item.Id, SourceMessageId = item.SourceMessageId };
        UpdateScalars(record, item);
        record.Facts.AddRange(item.Facts.Select(fact => ToRecord(item.Id, fact)));
        if (item.Draft is not null)
            record.Draft = ToRecord(item.Draft);
        record.AuditEntries.AddRange(item.PendingAuditEntries.Select(ToRecord));
        return record;
    }

    public static void Update(WorkItemRecord record, WorkItem item)
    {
        UpdateScalars(record, item);
        var facts = item.Facts.ToDictionary(fact => fact.Id);
        foreach (var factRecord in record.Facts)
        {
            if (!facts.Remove(factRecord.Id, out var fact))
                throw new InvalidOperationException("A persisted work item fact cannot be removed.");
            factRecord.Type = fact.Type.ToString();
            factRecord.OriginalValue = fact.OriginalValue;
            factRecord.Value = fact.Value;
            factRecord.Status = fact.Status.ToString();
            factRecord.ModifiedAt = fact.ModifiedAt;
            factRecord.ModifiedBy = fact.ModifiedBy;
        }
        if (facts.Count > 0)
            throw new InvalidOperationException("New reviewed facts require an explicit add operation.");

        if (item.Draft is not null)
        {
            if (record.Draft is null)
                record.Draft = ToRecord(item.Draft);
            else
                Update(record.Draft, item.Draft);
        }

        foreach (var audit in item.PendingAuditEntries)
            record.AuditEntries.Add(ToRecord(audit));
    }

    private static void UpdateScalars(WorkItemRecord record, WorkItem item)
    {
        record.Status = item.Status.ToString();
        record.AssignedReviewerId = item.AssignedReviewerId;
        record.EffectiveCategory = item.EffectiveCategory.ToString();
        record.EffectivePriority = item.EffectivePriority.ToString();
        record.ReviewerNotes = item.ReviewerNotes;
        record.MissingInformationJson = JsonSerializer.Serialize(item.MissingInformation, JsonOptions);
        record.CreatedAt = item.CreatedAt;
        record.UpdatedAt = item.UpdatedAt;
        record.ResolvedAt = item.ResolvedAt;
        record.ResolvedBy = item.ResolvedBy;
        record.Version = item.Version;
    }

    private static WorkItemFactRecord ToRecord(Guid workItemId, ReviewedFact fact) => new()
    {
        Id = fact.Id,
        WorkItemId = workItemId,
        Type = fact.Type.ToString(),
        OriginalValue = fact.OriginalValue,
        Value = fact.Value,
        Status = fact.Status.ToString(),
        ModifiedAt = fact.ModifiedAt,
        ModifiedBy = fact.ModifiedBy,
    };

    public static WorkItemAuditEntry ToDomain(WorkItemAuditRecord record) => new(
        record.Id,
        record.WorkItemId,
        record.ActorId,
        Enum.Parse<WorkItemAuditAction>(record.Action),
        record.OccurredAt,
        record.EntityId,
        record.Revision,
        record.Summary);

    private static ResponseDraft ToDomain(ResponseDraftRecord record) => ResponseDraft.Restore(
        record.Id,
        record.RecipientEmail,
        record.Revisions.OrderBy(revision => revision.Revision).Select(revision => ResponseDraftRevision.Restore(
            revision.Id,
            revision.Revision,
            revision.Subject,
            revision.Body,
            Enum.Parse<ResponseDraftStatus>(revision.Status),
            revision.CreatedAt,
            revision.CreatedBy,
            revision.DecidedAt,
            revision.DecidedBy,
            revision.Feedback,
            revision.ProviderId is null ? null : new EmailAnalysisMetrics(
                revision.ProviderId,
                revision.Model!,
                revision.DurationMilliseconds ?? 0,
                revision.InputTokens,
                revision.CachedInputTokens,
                revision.OutputTokens,
                revision.EstimatedCostUsd))));

    private static ResponseDraftRecord ToRecord(ResponseDraft draft)
    {
        var record = new ResponseDraftRecord { Id = draft.Id, RecipientEmail = draft.RecipientEmail };
        Update(record, draft);
        return record;
    }

    private static void Update(ResponseDraftRecord record, ResponseDraft draft)
    {
        record.RecipientEmail = draft.RecipientEmail;
        var revisions = record.Revisions.ToDictionary(revision => revision.Revision);
        foreach (var revision in draft.Revisions)
        {
            if (!revisions.Remove(revision.Revision, out var existing))
            {
                existing = ToRecord(revision);
                existing.ResponseDraftId = draft.Id;
                record.Revisions.Add(existing);
                continue;
            }
            Update(existing, revision);
        }
        if (revisions.Count > 0)
            throw new InvalidOperationException("A saved response draft revision cannot be removed.");
    }

    private static ResponseDraftRevisionRecord ToRecord(ResponseDraftRevision revision)
    {
        var record = new ResponseDraftRevisionRecord { Id = revision.Id };
        Update(record, revision);
        return record;
    }

    private static void Update(ResponseDraftRevisionRecord record, ResponseDraftRevision revision)
    {
        record.Revision = revision.Revision;
        record.Subject = revision.Subject;
        record.Body = revision.Body;
        record.Status = revision.Status.ToString();
        record.CreatedAt = revision.CreatedAt;
        record.CreatedBy = revision.CreatedBy;
        record.DecidedAt = revision.DecidedAt;
        record.DecidedBy = revision.DecidedBy;
        record.Feedback = revision.Feedback;
        record.ProviderId = revision.GenerationMetrics?.ProviderId;
        record.Model = revision.GenerationMetrics?.Model;
        record.DurationMilliseconds = revision.GenerationMetrics?.DurationMilliseconds;
        record.InputTokens = revision.GenerationMetrics?.InputTokens;
        record.CachedInputTokens = revision.GenerationMetrics?.CachedInputTokens;
        record.OutputTokens = revision.GenerationMetrics?.OutputTokens;
        record.EstimatedCostUsd = revision.GenerationMetrics?.EstimatedCostUsd;
    }

    private static WorkItemAuditRecord ToRecord(WorkItemAuditEntry entry) => new()
    {
        Id = entry.Id,
        WorkItemId = entry.WorkItemId,
        ActorId = entry.ActorId,
        Action = entry.Action.ToString(),
        OccurredAt = entry.OccurredAt,
        EntityId = entry.EntityId,
        Revision = entry.Revision,
        Summary = entry.Summary,
    };
}

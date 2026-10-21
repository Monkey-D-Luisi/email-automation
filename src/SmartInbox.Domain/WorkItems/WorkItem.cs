using SmartInbox.Domain.Messages;

namespace SmartInbox.Domain.WorkItems;

public sealed class WorkItem
{
    public const int MaximumNotesLength = 2_000;
    public const int MaximumMissingInformationItems = 20;
    public const int MaximumMissingInformationLength = 120;

    private readonly List<WorkItemAuditEntry> pendingAuditEntries = [];

    public Guid Id { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public WorkItemStatus Status { get; private set; }
    public string? AssignedReviewerId { get; private set; }
    public EmailCategory EffectiveCategory { get; private set; }
    public EmailPriority EffectivePriority { get; private set; }
    public string? ReviewerNotes { get; private set; }
    public IReadOnlyList<string> MissingInformation { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<ReviewedFact> Facts { get; private set; } = [];
    public ResponseDraft? Draft { get; private set; }
    public IReadOnlyList<WorkItemAuditEntry> PendingAuditEntries => pendingAuditEntries;

    private WorkItem() { }

    public static WorkItem Create(Guid sourceMessageId, EmailClassification classification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(classification);
        if (sourceMessageId == Guid.Empty)
            throw new ArgumentException("A source message is required.", nameof(sourceMessageId));
        var timestamp = now.ToUniversalTime();
        var item = new WorkItem
        {
            Id = Guid.NewGuid(),
            SourceMessageId = sourceMessageId,
            Status = WorkItemStatus.PendingReview,
            EffectiveCategory = classification.Category,
            EffectivePriority = classification.Priority,
            MissingInformation = Array.AsReadOnly(classification.MissingInformation.ToArray()),
            Facts = Array.AsReadOnly(classification.Facts.Select(ReviewedFact.Create).ToArray()),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        item.Record("system", WorkItemAuditAction.Created, timestamp, null, null, "Work item created from a completed classification.");
        return item;
    }

    public static WorkItem Restore(
        Guid id,
        Guid sourceMessageId,
        WorkItemStatus status,
        string? assignedReviewerId,
        EmailCategory effectiveCategory,
        EmailPriority effectivePriority,
        string? reviewerNotes,
        IEnumerable<string> missingInformation,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? resolvedAt,
        string? resolvedBy,
        int version,
        IEnumerable<ReviewedFact> facts,
        ResponseDraft? draft)
    {
        var missing = ValidateMissingInformation(missingInformation);
        var restoredFacts = facts?.ToArray() ?? throw new ArgumentNullException(nameof(facts));
        if (id == Guid.Empty || sourceMessageId == Guid.Empty || !Enum.IsDefined(status) ||
            !Enum.IsDefined(effectiveCategory) || !Enum.IsDefined(effectivePriority) || version < 0 ||
            (reviewerNotes?.Length ?? 0) > MaximumNotesLength ||
            ((status == WorkItemStatus.PendingReview) != (assignedReviewerId is null)) ||
            ((status == WorkItemStatus.Resolved) != (resolvedAt is not null && resolvedBy is not null)) ||
            (resolvedAt is null) != (resolvedBy is null))
            throw new ArgumentException("The persisted work item is invalid.");
        return new WorkItem
        {
            Id = id,
            SourceMessageId = sourceMessageId,
            Status = status,
            AssignedReviewerId = assignedReviewerId,
            EffectiveCategory = effectiveCategory,
            EffectivePriority = effectivePriority,
            ReviewerNotes = reviewerNotes,
            MissingInformation = Array.AsReadOnly(missing),
            CreatedAt = createdAt.ToUniversalTime(),
            UpdatedAt = updatedAt.ToUniversalTime(),
            ResolvedAt = resolvedAt?.ToUniversalTime(),
            ResolvedBy = resolvedBy,
            Version = version,
            Facts = Array.AsReadOnly(restoredFacts),
            Draft = draft,
        };
    }

    public void StartReview(string reviewerId, DateTimeOffset now)
    {
        ValidateReviewer(reviewerId);
        if (Status != WorkItemStatus.PendingReview)
            throw new InvalidOperationException("Only a pending work item can be claimed.");
        Status = WorkItemStatus.InReview;
        AssignedReviewerId = reviewerId;
        Changed(reviewerId, WorkItemAuditAction.ReviewStarted, now, null, null, "Review started.");
    }

    public void EnsureEditableBy(string reviewerId) => RequireReviewer(reviewerId);

    public bool UpdateReview(
        EmailCategory category,
        EmailPriority priority,
        string? notes,
        IEnumerable<string> missingInformation,
        string reviewerId,
        DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        if (!Enum.IsDefined(category) || !Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(category));
        if ((notes?.Length ?? 0) > MaximumNotesLength)
            throw new ArgumentException("Reviewer notes are too long.", nameof(notes));
        var missing = ValidateMissingInformation(missingInformation);
        var changes = new List<string>();
        var classificationChanged = false;
        if (category != EffectiveCategory)
        {
            Record(reviewerId, WorkItemAuditAction.CategoryCorrected, now, null, null, $"Category changed from {EffectiveCategory} to {category}.");
            EffectiveCategory = category;
            classificationChanged = true;
        }
        if (priority != EffectivePriority)
        {
            Record(reviewerId, WorkItemAuditAction.PriorityCorrected, now, null, null, $"Priority changed from {EffectivePriority} to {priority}.");
            EffectivePriority = priority;
            classificationChanged = true;
        }
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalizedNotes != ReviewerNotes)
        {
            ReviewerNotes = normalizedNotes;
            changes.Add("review notes updated");
        }
        if (!missing.SequenceEqual(MissingInformation, StringComparer.Ordinal))
        {
            MissingInformation = Array.AsReadOnly(missing);
            changes.Add("missing information updated");
        }
        if (changes.Count == 0 && !classificationChanged)
            return false;
        if (changes.Count > 0)
            Record(reviewerId, changes.Contains("review notes updated") ? WorkItemAuditAction.NotesChanged : WorkItemAuditAction.MissingInformationChanged,
                now, null, null, string.Join("; ", changes));
        Changed(now);
        return true;
    }

    public void VerifyFact(Guid factId, string reviewerId, DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        var fact = FindFact(factId);
        if (!fact.Verify(reviewerId, now))
            throw new InvalidOperationException("Only an unverified fact can be verified.");
        Changed(reviewerId, WorkItemAuditAction.FactVerified, now, fact.Id, null, $"{fact.Type} fact verified.");
    }

    public void CorrectFact(Guid factId, string value, string reviewerId, DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        var fact = FindFact(factId);
        if (!fact.Correct(value, reviewerId, now))
            return;
        Changed(reviewerId, WorkItemAuditAction.FactCorrected, now, fact.Id, null, $"{fact.Type} fact corrected; verification reset.");
    }

    public void RejectFact(Guid factId, string reviewerId, DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        var fact = FindFact(factId);
        if (!fact.Reject(reviewerId, now))
            throw new InvalidOperationException("Only an unverified fact can be rejected.");
        Changed(reviewerId, WorkItemAuditAction.FactRejected, now, fact.Id, null, $"{fact.Type} fact rejected.");
    }

    public void SaveDraft(
        string recipientEmail,
        string subject,
        string body,
        string reviewerId,
        DateTimeOffset now,
        EmailAnalysisMetrics? generationMetrics = null)
    {
        RequireReviewer(reviewerId);
        var created = Draft is null;
        if (Draft is null)
            Draft = ResponseDraft.Create(recipientEmail, subject, body, reviewerId, now, generationMetrics);
        else
            Draft.AddRevision(subject, body, reviewerId, now, generationMetrics);
        Changed(reviewerId, created ? WorkItemAuditAction.DraftCreated : WorkItemAuditAction.DraftEdited,
            now, Draft.Id, Draft.CurrentRevision.Revision, $"Draft revision {Draft.CurrentRevision.Revision} saved as unapproved.");
    }

    public void ApproveDraft(int revision, string reviewerId, DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        if (Draft is null)
            throw new InvalidOperationException("A response draft is required before approval.");
        Draft.Approve(revision, reviewerId, now);
        Changed(reviewerId, WorkItemAuditAction.DraftApproved, now, Draft.Id, revision, $"Draft revision {revision} approved.");
    }

    public void RejectDraft(int revision, string? feedback, string reviewerId, DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        if (Draft is null)
            throw new InvalidOperationException("A response draft is required before rejection.");
        Draft.Reject(revision, reviewerId, feedback, now);
        Changed(reviewerId, WorkItemAuditAction.DraftRejected, now, Draft.Id, revision, $"Draft revision {revision} rejected.");
    }

    public void Resolve(string reviewerId, DateTimeOffset now)
    {
        RequireReviewer(reviewerId);
        if (Facts.Any(fact => fact.Status == ReviewedFactStatus.Unverified))
            throw new InvalidOperationException("All extracted facts must be verified or rejected before resolution.");
        Status = WorkItemStatus.Resolved;
        ResolvedAt = now.ToUniversalTime();
        ResolvedBy = reviewerId;
        Changed(reviewerId, WorkItemAuditAction.Resolved, now, null, null, "Work item resolved.");
    }

    private ReviewedFact FindFact(Guid factId) => Facts.FirstOrDefault(fact => fact.Id == factId)
        ?? throw new KeyNotFoundException("The reviewed fact does not exist.");

    private void RequireReviewer(string reviewerId)
    {
        ValidateReviewer(reviewerId);
        if (Status != WorkItemStatus.InReview || !string.Equals(AssignedReviewerId, reviewerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The work item is not assigned to this reviewer.");
    }

    private static string[] ValidateMissingInformation(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = values.ToArray();
        if (items.Length > MaximumMissingInformationItems ||
            items.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaximumMissingInformationLength))
            throw new ArgumentException("Missing information is invalid.", nameof(values));
        return items.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateReviewer(string reviewerId)
    {
        if (string.IsNullOrWhiteSpace(reviewerId) || reviewerId.Length > 128)
            throw new ArgumentException("A reviewer identity is required.", nameof(reviewerId));
    }

    private void Changed(string actor, WorkItemAuditAction action, DateTimeOffset now, Guid? entityId, int? revision, string summary)
    {
        Record(actor, action, now, entityId, revision, summary);
        Changed(now);
    }

    private void Changed(DateTimeOffset now)
    {
        UpdatedAt = now.ToUniversalTime();
        Version = checked(Version + 1);
    }

    private void Record(string actor, WorkItemAuditAction action, DateTimeOffset now, Guid? entityId, int? revision, string summary) =>
        pendingAuditEntries.Add(new WorkItemAuditEntry(Guid.NewGuid(), Id, actor, action, now.ToUniversalTime(), entityId, revision, summary));
}

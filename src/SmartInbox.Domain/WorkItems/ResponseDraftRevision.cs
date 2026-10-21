using SmartInbox.Domain.Messages;

namespace SmartInbox.Domain.WorkItems;

public sealed class ResponseDraftRevision
{
    public Guid Id { get; private set; }
    public int Revision { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public ResponseDraftStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecidedBy { get; private set; }
    public string? Feedback { get; private set; }
    public EmailAnalysisMetrics? GenerationMetrics { get; private set; }

    private ResponseDraftRevision() { }

    internal static ResponseDraftRevision Create(
        int revision,
        string subject,
        string body,
        string reviewerId,
        DateTimeOffset now,
        EmailAnalysisMetrics? generationMetrics)
    {
        ValidateContent(subject, body);
        ValidateMetrics(generationMetrics);
        return new ResponseDraftRevision
        {
            Id = Guid.NewGuid(),
            Revision = revision,
            Subject = subject,
            Body = body,
            Status = ResponseDraftStatus.Draft,
            CreatedAt = now.ToUniversalTime(),
            CreatedBy = reviewerId,
            GenerationMetrics = generationMetrics,
        };
    }

    public static ResponseDraftRevision Restore(
        Guid id,
        int revision,
        string subject,
        string body,
        ResponseDraftStatus status,
        DateTimeOffset createdAt,
        string createdBy,
        DateTimeOffset? decidedAt,
        string? decidedBy,
        string? feedback,
        EmailAnalysisMetrics? generationMetrics)
    {
        ValidateContent(subject, body);
        if (id == Guid.Empty || revision < 1 || !Enum.IsDefined(status) || string.IsNullOrWhiteSpace(createdBy) ||
            (status == ResponseDraftStatus.Draft) != (decidedAt is null && decidedBy is null) ||
            (decidedAt is null) != (decidedBy is null) || (feedback?.Length ?? 0) > 500)
            throw new ArgumentException("The persisted draft revision is invalid.");
        ValidateMetrics(generationMetrics);
        return new ResponseDraftRevision
        {
            Id = id,
            Revision = revision,
            Subject = subject,
            Body = body,
            Status = status,
            CreatedAt = createdAt.ToUniversalTime(),
            CreatedBy = createdBy,
            DecidedAt = decidedAt?.ToUniversalTime(),
            DecidedBy = decidedBy,
            Feedback = feedback,
            GenerationMetrics = generationMetrics,
        };
    }

    internal void Approve(string reviewerId, DateTimeOffset now)
    {
        EnsurePendingDecision();
        Status = ResponseDraftStatus.Approved;
        DecidedAt = now.ToUniversalTime();
        DecidedBy = reviewerId;
    }

    internal void Reject(string reviewerId, string? feedback, DateTimeOffset now)
    {
        EnsurePendingDecision();
        if (feedback?.Length > 500)
            throw new ArgumentException("Draft feedback is too long.", nameof(feedback));
        Status = ResponseDraftStatus.Rejected;
        DecidedAt = now.ToUniversalTime();
        DecidedBy = reviewerId;
        Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
    }

    private void EnsurePendingDecision()
    {
        if (Status != ResponseDraftStatus.Draft)
            throw new InvalidOperationException("Only an unapproved draft revision can be decided.");
        ValidateContent(Subject, Body);
    }

    private static void ValidateContent(string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 998)
            throw new ArgumentException("Draft subject is invalid.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body) || body.Length > 20_000)
            throw new ArgumentException("Draft body is invalid.", nameof(body));
    }

    private static void ValidateMetrics(EmailAnalysisMetrics? metrics)
    {
        if (metrics is not null &&
            (string.IsNullOrWhiteSpace(metrics.ProviderId) || metrics.ProviderId.Length > 64 ||
             string.IsNullOrWhiteSpace(metrics.Model) || metrics.Model.Length > 128 ||
             metrics.DurationMilliseconds < 0 || metrics.InputTokens is < 0 || metrics.OutputTokens is < 0 ||
             metrics.CachedInputTokens is < 0 ||
             metrics.CachedInputTokens is not null && metrics.InputTokens is null ||
             metrics.CachedInputTokens > metrics.InputTokens || metrics.EstimatedCostUsd is < 0))
            throw new ArgumentException("Draft generation metrics are invalid.", nameof(metrics));
    }
}

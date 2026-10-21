namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class ResponseDraftRevisionRecord
{
    public Guid Id { get; set; }
    public Guid ResponseDraftId { get; set; }
    public int Revision { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? Feedback { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public long? DurationMilliseconds { get; set; }
    public int? InputTokens { get; set; }
    public int? CachedInputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
}

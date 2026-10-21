namespace SmartInbox.Domain.Messages;

public sealed record EmailAnalysisMetrics(
    string ProviderId,
    string Model,
    long DurationMilliseconds,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd);

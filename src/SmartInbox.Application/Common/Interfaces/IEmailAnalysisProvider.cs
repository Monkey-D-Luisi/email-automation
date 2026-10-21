using SmartInbox.Application.Common.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Common.Interfaces;

public interface IEmailAnalysisProvider
{
    EmailAnalysisProviderType ProviderType { get; }

    Task<EmailAnalysisProviderResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default);
}

public interface IEmailAnalysisProviderFactory
{
    IEmailAnalysisProvider Get(EmailAnalysisProviderType providerType);
}

public sealed record EmailFactCandidate(string Type, string Value);

public sealed record EmailAnalysisCandidate(
    string Category,
    string SuggestedPriority,
    string Summary,
    string Language,
    IReadOnlyList<EmailFactCandidate> Facts,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> Warnings,
    bool IsAmbiguous,
    IReadOnlyList<string> UncertaintyReasons,
    string? PriorityEvidence);

public sealed record EmailAnalysisProviderResult(
    EmailAnalysisCandidate Candidate,
    string Model,
    long DurationMilliseconds,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd);

public sealed record EmailAnalyzerResult(EmailClassification Classification, EmailAnalysisMetrics Metrics);

public interface IEmailAnalyzer
{
    Task<EmailAnalyzerResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default);
}

using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Classification.Policies;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Classification;

[IoC(typeof(IEmailAnalyzer), IoCLifetime.Scoped)]
public sealed class EmailAnalyzer(
    IEmailAnalysisProviderFactory providerFactory,
    EmailAnalysisProviderType providerType,
    EmailAnalysisPolicy policy,
    TimeProvider timeProvider) : IEmailAnalyzer
{
    public async Task<EmailAnalyzerResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default)
    {
        var provider = providerFactory.Get(providerType);
        var result = await provider.AnalyzeAsync(message, cancellationToken);
        try
        {
            var classification = policy.Apply(message, result.Candidate, timeProvider.GetUtcNow());
            var metrics = new EmailAnalysisMetrics(
                EmailAnalysisProviderTypeCodec.ToId(provider.ProviderType),
                result.Model,
                result.DurationMilliseconds,
                result.InputTokens,
                result.CachedInputTokens,
                result.OutputTokens,
                result.EstimatedCostUsd);
            return new EmailAnalyzerResult(classification, metrics);
        }
        catch (EmailAnalysisException exception) when (exception.Metrics is null)
        {
            throw new EmailAnalysisException(
                exception.Message,
                exception.FailureKind,
                exception.Retryable,
                new EmailAnalysisMetrics(
                    EmailAnalysisProviderTypeCodec.ToId(provider.ProviderType), result.Model,
                    result.DurationMilliseconds, result.InputTokens, result.CachedInputTokens,
                    result.OutputTokens, result.EstimatedCostUsd),
                exception);
        }
    }
}

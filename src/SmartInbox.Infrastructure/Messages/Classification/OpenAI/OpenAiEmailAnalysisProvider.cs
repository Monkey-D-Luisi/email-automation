using System.ClientModel;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Classification;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Infrastructure.Messages.Classification.OpenAI;

[IoC(typeof(IEmailAnalysisProvider), IoCLifetime.Scoped, allowMultiple: true)]
internal sealed class OpenAiEmailAnalysisProvider(
    IOpenAiResponsesClient client,
    IOptions<OpenAiEmailAnalysisOptions> openAiOptions,
    IOptions<EmailProcessingOptions> processingOptions,
    ILogger<OpenAiEmailAnalysisProvider> logger) : IEmailAnalysisProvider
{
    public EmailAnalysisProviderType ProviderType => EmailAnalysisProviderType.OpenAi;

    public async Task<EmailAnalysisProviderResult> AnalyzeAsync(
        InboundMessage message,
        CancellationToken cancellationToken = default)
    {
        var configuration = openAiOptions.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(processingOptions.Value.RequestTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await client.CreateResponseAsync(
                EmailAnalysisPromptFactory.Create(configuration, message.Subject, message.BodyText),
                timeout.Token);
            stopwatch.Stop();
            var model = response.Model ?? configuration.Model;
            var metrics = CreateMetrics(model, response, stopwatch.ElapsedMilliseconds, configuration);
            if (!string.Equals(response.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new EmailAnalysisException(
                    "The model did not complete the email analysis.",
                    ProcessingFailureKind.InvalidStructuredOutput,
                    retryable: false,
                    metrics);

            try
            {
                var candidate = EmailAnalysisResponseParser.Parse(response.OutputText);
                logger.LogInformation(
                    "OpenAI email analysis completed model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} cachedInputTokens={CachedInputTokens} outputTokens={OutputTokens} estimatedCostUsd={EstimatedCostUsd}",
                    model,
                    metrics.DurationMilliseconds,
                    metrics.InputTokens,
                    metrics.CachedInputTokens,
                    metrics.OutputTokens,
                    metrics.EstimatedCostUsd);
                return new EmailAnalysisProviderResult(
                    candidate,
                    model,
                    metrics.DurationMilliseconds,
                    metrics.InputTokens,
                    metrics.CachedInputTokens,
                    metrics.OutputTokens,
                    metrics.EstimatedCostUsd);
            }
            catch (EmailAnalysisException exception) when (exception.Metrics is null)
            {
                throw new EmailAnalysisException(
                    exception.Message,
                    exception.FailureKind,
                    exception.Retryable,
                    metrics,
                    exception);
            }
        }
        catch (EmailAnalysisException exception)
        {
            logger.LogWarning(
                "OpenAI email analysis failed failureKind={FailureKind} latencyMs={LatencyMs}",
                exception.FailureKind,
                stopwatch.ElapsedMilliseconds);
            if (exception.Metrics is not null)
                throw;
            throw new EmailAnalysisException(
                exception.Message,
                exception.FailureKind,
                exception.Retryable,
                CreateFailureMetrics(configuration, stopwatch.ElapsedMilliseconds),
                exception);
        }
        catch (ClientResultException exception)
        {
            var kind = exception.Status switch
            {
                401 or 403 => ProcessingFailureKind.Authentication,
                408 => ProcessingFailureKind.Timeout,
                429 => ProcessingFailureKind.RateLimited,
                >= 500 => ProcessingFailureKind.TransientConnectivity,
                400 or 404 or 413 or 422 => ProcessingFailureKind.UnsupportedProviderOrModel,
                _ => ProcessingFailureKind.Unknown,
            };
            logger.LogWarning(
                "OpenAI request failed statusCode={StatusCode} latencyMs={LatencyMs}",
                exception.Status,
                stopwatch.ElapsedMilliseconds);
            throw new EmailAnalysisException(
                "The OpenAI request could not be completed.",
                kind,
                kind is ProcessingFailureKind.Timeout or ProcessingFailureKind.RateLimited or ProcessingFailureKind.TransientConnectivity,
                CreateFailureMetrics(configuration, stopwatch.ElapsedMilliseconds),
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("OpenAI connection failed latencyMs={LatencyMs}", stopwatch.ElapsedMilliseconds);
            throw new EmailAnalysisException(
                "The OpenAI service could not be reached.",
                ProcessingFailureKind.TransientConnectivity,
                retryable: true,
                metrics: CreateFailureMetrics(configuration, stopwatch.ElapsedMilliseconds),
                innerException: exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning("OpenAI request timed out latencyMs={LatencyMs}", stopwatch.ElapsedMilliseconds);
            throw new EmailAnalysisException(
                "The OpenAI service did not respond before the configured timeout.",
                ProcessingFailureKind.Timeout,
                retryable: true,
                metrics: CreateFailureMetrics(configuration, stopwatch.ElapsedMilliseconds),
                innerException: exception);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "OpenAI request failed exceptionType={ExceptionType} latencyMs={LatencyMs}",
                exception.GetType().Name,
                stopwatch.ElapsedMilliseconds);
            throw new EmailAnalysisException(
                "The OpenAI request failed unexpectedly.",
                ProcessingFailureKind.Unknown,
                retryable: false,
                metrics: CreateFailureMetrics(configuration, stopwatch.ElapsedMilliseconds),
                innerException: exception);
        }
    }

    private static EmailAnalysisMetrics CreateMetrics(
        string model,
        OpenAiResponsesResult response,
        long durationMilliseconds,
        OpenAiEmailAnalysisOptions configuration) => new(
        EmailAnalysisProviderTypeCodec.ToId(EmailAnalysisProviderType.OpenAi),
        model,
        durationMilliseconds,
        response.InputTokens,
        response.CachedInputTokens,
        response.OutputTokens,
        EstimateCost(model, response.InputTokens, response.CachedInputTokens, response.OutputTokens, configuration));

    private static EmailAnalysisMetrics CreateFailureMetrics(OpenAiEmailAnalysisOptions configuration, long durationMilliseconds) =>
        new(
            EmailAnalysisProviderTypeCodec.ToId(EmailAnalysisProviderType.OpenAi),
            configuration.Model,
            durationMilliseconds,
            null,
            null,
            null,
            null);

    private static decimal? EstimateCost(
        string model,
        int? inputTokens,
        int? cachedInputTokens,
        int? outputTokens,
        OpenAiEmailAnalysisOptions configuration)
    {
        var pricing = configuration.ModelPricing.FirstOrDefault(item =>
            string.Equals(item.Key, model, StringComparison.OrdinalIgnoreCase)).Value;
        if (inputTokens is null || outputTokens is null || pricing?.InputCostPerMillionTokensUsd is null ||
            pricing.OutputCostPerMillionTokensUsd is null || cachedInputTokens is < 0 || cachedInputTokens > inputTokens)
            return null;

        var cached = cachedInputTokens ?? 0;
        var uncached = inputTokens.Value - cached;
        var cachedRate = pricing.CachedInputCostPerMillionTokensUsd ?? pricing.InputCostPerMillionTokensUsd.Value;
        var inputCost = uncached / 1_000_000m * pricing.InputCostPerMillionTokensUsd.Value +
                        cached / 1_000_000m * cachedRate;
        var outputCost = outputTokens.Value / 1_000_000m * pricing.OutputCostPerMillionTokensUsd.Value;
        return decimal.Round(inputCost + outputCost, 8, MidpointRounding.AwayFromZero);
    }
}

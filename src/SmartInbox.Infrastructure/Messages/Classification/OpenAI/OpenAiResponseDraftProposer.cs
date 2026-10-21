#pragma warning disable OPENAI001

using System.ClientModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Infrastructure.Messages.Classification.OpenAI;

[IoC(typeof(IResponseDraftProposer), IoCLifetime.Singleton)]
internal sealed class OpenAiResponseDraftProposer(
    IOpenAiResponsesClient client,
    IOptions<OpenAiEmailAnalysisOptions> openAiOptions,
    IOptions<EmailDraftingOptions> draftingOptions,
    ILogger<OpenAiResponseDraftProposer> logger) : IResponseDraftProposer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly SemaphoreSlim concurrency = new(draftingOptions.Value.MaxConcurrency);

    public async Task<DraftProposalResult> ProposeAsync(
        DraftProposalContext context,
        CancellationToken cancellationToken = default)
    {
        var openAi = openAiOptions.Value;
        var limits = draftingOptions.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(limits.RequestTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();
        var acquired = false;
        try
        {
            await concurrency.WaitAsync(timeout.Token);
            acquired = true;
            stopwatch.Restart();
            var response = await client.CreateResponseAsync(
                EmailDraftPromptFactory.Create(openAi.Model, limits.MaxOutputTokens, context),
                timeout.Token);
            stopwatch.Stop();
            if (!string.Equals(response.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new DraftProposalException("The response draft provider did not complete the request.");

            var result = JsonSerializer.Deserialize<ResponseContract>(response.OutputText, JsonOptions);
            if (result is null || string.IsNullOrWhiteSpace(result.Body) || result.Body.Length > 10_000)
                throw new DraftProposalException("The response draft provider returned invalid content.");

            var model = response.Model ?? openAi.Model;
            var metrics = new EmailAnalysisMetrics(
                "openai",
                model,
                stopwatch.ElapsedMilliseconds,
                response.InputTokens,
                response.CachedInputTokens,
                response.OutputTokens,
                EstimateCost(model, response.InputTokens, response.CachedInputTokens, response.OutputTokens, openAi));
            logger.LogInformation(
                "OpenAI response draft completed model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} cachedInputTokens={CachedInputTokens} outputTokens={OutputTokens} estimatedCostUsd={EstimatedCostUsd}",
                model,
                metrics.DurationMilliseconds,
                metrics.InputTokens,
                metrics.CachedInputTokens,
                metrics.OutputTokens,
                metrics.EstimatedCostUsd);
            return new DraftProposalResult(result.Body, metrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning("OpenAI response draft timed out latencyMs={LatencyMs}", stopwatch.ElapsedMilliseconds);
            throw new DraftProposalException("The response draft provider timed out. The request may have been billed.", exception);
        }
        catch (ClientResultException exception)
        {
            logger.LogWarning("OpenAI response draft failed statusCode={StatusCode} latencyMs={LatencyMs}", exception.Status, stopwatch.ElapsedMilliseconds);
            throw new DraftProposalException("The response draft provider could not complete the request. It may have been billed.", exception);
        }
        catch (DraftProposalException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            logger.LogWarning("OpenAI response draft returned invalid structured output latencyMs={LatencyMs}", stopwatch.ElapsedMilliseconds);
            throw new DraftProposalException("The response draft provider returned invalid structured output.", exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("OpenAI response draft connection failed latencyMs={LatencyMs}", stopwatch.ElapsedMilliseconds);
            throw new DraftProposalException("The response draft provider could not be reached. The request may have been billed.", exception);
        }
        catch (Exception exception)
        {
            logger.LogWarning("OpenAI response draft failed exceptionType={ExceptionType}", exception.GetType().Name);
            throw new DraftProposalException("The response draft could not be generated.", exception);
        }
        finally
        {
            if (acquired)
                concurrency.Release();
        }
    }

    private static decimal? EstimateCost(
        string model,
        int? inputTokens,
        int? cachedInputTokens,
        int? outputTokens,
        OpenAiEmailAnalysisOptions options)
    {
        var pricing = options.ModelPricing.FirstOrDefault(item =>
            string.Equals(item.Key, model, StringComparison.OrdinalIgnoreCase)).Value;
        if (inputTokens is null || outputTokens is null || pricing?.InputCostPerMillionTokensUsd is null ||
            pricing.OutputCostPerMillionTokensUsd is null || cachedInputTokens is < 0 || cachedInputTokens > inputTokens)
            return null;

        var cached = cachedInputTokens ?? 0;
        var uncached = inputTokens.Value - cached;
        var cachedRate = pricing.CachedInputCostPerMillionTokensUsd ?? pricing.InputCostPerMillionTokensUsd.Value;
        var cost = uncached / 1_000_000m * pricing.InputCostPerMillionTokensUsd.Value +
                   cached / 1_000_000m * cachedRate +
                   outputTokens.Value / 1_000_000m * pricing.OutputCostPerMillionTokensUsd.Value;
        return decimal.Round(cost, 8, MidpointRounding.AwayFromZero);
    }

    private sealed class ResponseContract
    {
        [JsonRequired] public string Body { get; set; } = string.Empty;
    }
}

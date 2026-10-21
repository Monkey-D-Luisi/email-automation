#pragma warning disable OPENAI001

using OpenAI.Responses;
using SmartInbox.Application.Common.DependencyInjection;

namespace SmartInbox.Infrastructure.Messages.Classification.OpenAI;

internal interface IOpenAiResponsesClient
{
    Task<OpenAiResponsesResult> CreateResponseAsync(CreateResponseOptions options, CancellationToken cancellationToken);
}

internal sealed record OpenAiResponsesResult(
    string OutputText,
    string? Model,
    string? Status,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens);

[IoC(typeof(IOpenAiResponsesClient), IoCLifetime.Singleton)]
internal sealed class OpenAiResponsesClient(ResponsesClient client) : IOpenAiResponsesClient
{
    public async Task<OpenAiResponsesResult> CreateResponseAsync(
        CreateResponseOptions options,
        CancellationToken cancellationToken)
    {
        var result = await client.CreateResponseAsync(options, cancellationToken);
        var response = result.Value;
        return new OpenAiResponsesResult(
            response.GetOutputText(),
            response.Model,
            response.Status?.ToString(),
            response.Usage?.InputTokenCount,
            response.Usage?.InputTokenDetails?.CachedTokenCount,
            response.Usage?.OutputTokenCount);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using SmartInbox.Application.Messages.Classification;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Infrastructure.Messages.Classification.OpenAI;

internal static class EmailAnalysisResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static EmailAnalysisCandidate Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<ResponseContract>(json, JsonOptions)
                ?? throw InvalidResponse();
            if (result.Facts is not { Count: <= InboundMessageLimits.ClassificationFacts } facts ||
                result.MissingInformation is not { Count: <= InboundMessageLimits.ClassificationItems } missing ||
                result.Warnings is not { Count: <= InboundMessageLimits.ClassificationItems } warnings ||
                result.UncertaintyReasons is not { Count: <= InboundMessageLimits.ClassificationItems } uncertainty ||
                facts.Any(fact => fact is null) || missing.Any(value => value is null) ||
                warnings.Any(value => value is null) || uncertainty.Any(value => value is null))
                throw InvalidResponse();

            return new EmailAnalysisCandidate(
                result.Category,
                result.SuggestedPriority,
                result.Summary,
                result.Language,
                facts.Select(fact => new EmailFactCandidate(fact!.Type, fact.Value)).ToArray(),
                missing.Select(value => value!).ToArray(),
                warnings.Select(value => value!).ToArray(),
                result.IsAmbiguous,
                uncertainty.Select(value => value!).ToArray(),
                result.PriorityEvidence);
        }
        catch (JsonException exception)
        {
            throw new EmailAnalysisException(
                "The model returned invalid structured output.",
                ProcessingFailureKind.InvalidStructuredOutput,
                retryable: false,
                innerException: exception);
        }
    }

    private static EmailAnalysisException InvalidResponse() => new(
        "The model returned an incomplete structured analysis.",
        ProcessingFailureKind.InvalidStructuredOutput,
        retryable: false);

    private sealed class ResponseContract
    {
        [JsonRequired] public string Category { get; set; } = string.Empty;
        [JsonRequired] public string SuggestedPriority { get; set; } = string.Empty;
        [JsonRequired] public string Summary { get; set; } = string.Empty;
        [JsonRequired] public string Language { get; set; } = string.Empty;
        [JsonRequired] public List<FactContract?>? Facts { get; set; }
        [JsonRequired] public List<string?>? MissingInformation { get; set; }
        [JsonRequired] public List<string?>? Warnings { get; set; }
        [JsonRequired] public bool IsAmbiguous { get; set; }
        [JsonRequired] public List<string?>? UncertaintyReasons { get; set; }
        [JsonRequired] public string? PriorityEvidence { get; set; }
    }

    private sealed class FactContract
    {
        [JsonRequired] public string Type { get; set; } = string.Empty;
        [JsonRequired] public string Value { get; set; } = string.Empty;
    }
}

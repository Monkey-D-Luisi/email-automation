using System.Text.Json;
using SmartInbox.Application.Common.Exceptions;
using SmartInbox.Application.Common.Interfaces;
using SmartInbox.Domain.Messages;
using SmartInbox.Infrastructure.AI;

namespace SmartInbox.Infrastructure.Tests.AI;

public sealed class EmailAnalysisResponseParserTests
{
    [Fact]
    public void Prompt_schema_uses_supported_constraints_for_bounded_strings_and_collections()
    {
        using var schema = JsonDocument.Parse(EmailAnalysisPromptFactory.JsonSchema.ToArray());
        var properties = schema.RootElement.GetProperty("properties");

        Assert.Equal("^[\\s\\S]{1,1000}$", properties.GetProperty("summary").GetProperty("pattern").GetString());
        Assert.Equal(20, properties.GetProperty("facts").GetProperty("maxItems").GetInt32());
        Assert.Equal("^[\\s\\S]{1,300}$", properties.GetProperty("facts").GetProperty("items")
            .GetProperty("properties").GetProperty("value").GetProperty("pattern").GetString());
    }

    [Fact]
    public void Parse_maps_a_complete_structured_response()
    {
        var result = EmailAnalysisResponseParser.Parse(ValidResponse);

        Assert.Equal("SalesInquiry", result.Category);
        Assert.Equal("High", result.SuggestedPriority);
        Assert.Equal("en", result.Language);
        Assert.Equal("CompanyName", Assert.Single(result.Facts).Type);
        Assert.Equal("Acme Ltd", result.Facts[0].Value);
        Assert.Equal("Please quote this as high priority.", result.PriorityEvidence);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"category\":\"GeneralInquiry\"}")]
    [InlineData("{\"category\":\"GeneralInquiry\",\"suggestedPriority\":\"Normal\",\"summary\":\"Question\",\"language\":\"en\",\"facts\":[],\"missingInformation\":[],\"warnings\":[],\"isAmbiguous\":false,\"uncertaintyReasons\":[],\"priorityEvidence\":null,\"unexpected\":true}")]
    public void Parse_rejects_invalid_or_incomplete_structured_output(string json)
    {
        var exception = Assert.Throws<EmailAnalysisException>(() => EmailAnalysisResponseParser.Parse(json));

        Assert.Equal(ProcessingFailureKind.InvalidStructuredOutput, exception.FailureKind);
        Assert.False(exception.Retryable);
    }

    private const string ValidResponse = """
        {
          "category": "SalesInquiry",
          "suggestedPriority": "High",
          "summary": "A company requests a quote.",
          "language": "en",
          "facts": [{"type": "CompanyName", "value": "Acme Ltd"}],
          "missingInformation": [],
          "warnings": [],
          "isAmbiguous": false,
          "uncertaintyReasons": [],
          "priorityEvidence": "Please quote this as high priority."
        }
        """;
}

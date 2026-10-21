using System.Text.Json;
using System.Text.Json.Serialization;
using SmartInbox.Application.Common.Interfaces;
using SmartInbox.Application.EmailAnalysis;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Tests.EmailAnalysis;

public sealed class SyntheticEmailEvaluationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Synthetic_dataset_matches_expected_categories_and_review_outcomes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "EmailAnalysis", "synthetic-emails.jsonl");
        var cases = (await File.ReadAllLinesAsync(path))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<SyntheticEmailCase>(line, JsonOptions)
                ?? throw new InvalidDataException("The synthetic evaluation dataset contains an invalid row."))
            .ToArray();
        var policy = new EmailAnalysisPolicy();

        Assert.True(cases.Length >= 30);
        foreach (var item in cases)
        {
            var message = InboundMessage.Receive(
                "synthetic-evaluation", "synthetic-mailbox", item.Id, null, null, null,
                item.Subject, item.Body, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            var result = policy.Apply(message, item.Candidate, DateTimeOffset.UnixEpoch);

            Assert.True(result.Category == item.ExpectedCategory,
                $"{item.Id}: expected {item.ExpectedCategory}, got {result.Category}.");
            Assert.True(result.RequiresReview == item.ExpectedRequiresReview,
                $"{item.Id}: expected RequiresReview={item.ExpectedRequiresReview}, got {result.RequiresReview}.");
        }
    }

    private sealed record SyntheticEmailCase(
        string Id,
        string Subject,
        string Body,
        EmailAnalysisCandidate Candidate,
        EmailCategory ExpectedCategory,
        bool ExpectedRequiresReview);
}

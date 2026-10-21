using SmartInbox.Application.Messages.Classification.Policies;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;
using SmartInbox.Infrastructure.Messages.Classification.Fake;

namespace SmartInbox.Infrastructure.Tests.Messages.Classification;

public sealed class FakeEmailAnalysisProviderTests
{
    [Fact]
    public async Task Fake_provider_returns_a_review_required_result_without_provider_usage()
    {
        var provider = new FakeEmailAnalysisProvider();
        var message = InboundMessage.Receive(
            "demo", "synthetic-mailbox", "synthetic-message", null, null, null,
            "Synthetic subject", "Synthetic body", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var result = await provider.AnalyzeAsync(message);
        var classification = new EmailAnalysisPolicy().Apply(message, result.Candidate, DateTimeOffset.UtcNow);

        Assert.Equal(EmailAnalysisProviderType.Fake, provider.ProviderType);
        Assert.Equal("offline-fake-v1", result.Model);
        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
        Assert.True(classification.RequiresReview);
        Assert.Equal(EmailCategory.Unknown, classification.Category);
    }
}

using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Tests.Messages.Classification;

public sealed class EmailAnalysisProviderFactoryTests
{
    [Theory]
    [InlineData("openai", EmailAnalysisProviderType.OpenAi)]
    [InlineData(" OpenAI ", EmailAnalysisProviderType.OpenAi)]
    public void Provider_identity_parses_stable_ids(string providerId, EmailAnalysisProviderType expected)
    {
        Assert.True(EmailAnalysisProviderTypeCodec.TryParse(providerId, out var parsed));
        Assert.Equal(expected, parsed);
        Assert.Equal("openai", EmailAnalysisProviderTypeCodec.ToId(parsed));
    }

    [Fact]
    public void Provider_identity_rejects_unknown_ids()
    {
        Assert.False(EmailAnalysisProviderTypeCodec.TryParse("future-ai", out _));
        Assert.Throws<ArgumentException>(() => EmailAnalysisProviderTypeCodec.Parse("future-ai"));
    }

    [Fact]
    public void Factory_selects_the_registered_provider_by_typed_identity()
    {
        var provider = new ProbeProvider(EmailAnalysisProviderType.OpenAi);
        var factory = new EmailAnalysisProviderFactory([provider], EmailAnalysisProviderType.OpenAi);

        Assert.Same(provider, factory.Get(EmailAnalysisProviderType.OpenAi));
    }

    [Fact]
    public void Factory_rejects_duplicate_provider_implementations()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EmailAnalysisProviderFactory(
        [
            new ProbeProvider(EmailAnalysisProviderType.OpenAi),
            new ProbeProvider(EmailAnalysisProviderType.OpenAi),
        ], EmailAnalysisProviderType.OpenAi));

        Assert.Contains("Duplicate email analysis providers", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ProbeProvider(EmailAnalysisProviderType providerType) : IEmailAnalysisProvider
    {
        public EmailAnalysisProviderType ProviderType => providerType;

        public Task<EmailAnalysisProviderResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}

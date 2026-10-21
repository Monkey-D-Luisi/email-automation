using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Infrastructure.Messages.Classification.Fake;

[IoC(typeof(IEmailAnalysisProvider), IoCLifetime.Scoped, allowMultiple: true)]
public sealed class FakeEmailAnalysisProvider : IEmailAnalysisProvider
{
    public EmailAnalysisProviderType ProviderType => EmailAnalysisProviderType.Fake;

    public Task<EmailAnalysisProviderResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var candidate = new EmailAnalysisCandidate(
            "Unknown",
            "Normal",
            "Offline fake result. Review this synthetic message before use.",
            "unknown",
            [],
            [],
            ["The offline fake provider does not classify business content."],
            true,
            ["Offline fake provider requires human review."],
            null);

        return Task.FromResult(new EmailAnalysisProviderResult(
            candidate,
            "offline-fake-v1",
            0,
            null,
            null,
            null,
            null));
    }
}

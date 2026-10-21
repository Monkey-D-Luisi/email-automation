using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Classification.Ports;

public interface IEmailAnalysisProvider
{
    EmailAnalysisProviderType ProviderType { get; }

    Task<EmailAnalysisProviderResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default);
}

public interface IEmailAnalysisProviderFactory
{
    IEmailAnalysisProvider Get(EmailAnalysisProviderType providerType);
}

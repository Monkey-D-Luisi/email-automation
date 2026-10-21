using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Classification.Ports;

public interface IEmailAnalyzer
{
    Task<EmailAnalyzerResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default);
}

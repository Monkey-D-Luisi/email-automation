using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Classification;

[IoC(typeof(InboundMessageProcessor), IoCLifetime.Scoped)]
public sealed class InboundMessageProcessor(
    IInboundMessageRepository repository,
    IEmailAnalyzer analyzer,
    EmailProcessingOptions options,
    TimeProvider timeProvider)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var settings = options;
        var now = timeProvider.GetUtcNow();
        var leaseOwner = Guid.NewGuid();
        var claim = await repository.ClaimNextForProcessingAsync(
            leaseOwner,
            now,
            TimeSpan.FromSeconds(settings.LeaseDurationSeconds),
            settings.MaxAttempts,
            cancellationToken);
        if (claim is null)
            return false;

        if (claim.Message.Subject.Length + claim.Message.BodyText.Length > settings.MaxInputCharacters)
        {
            await FailAsync(claim, ProcessingFailureKind.ProviderRejected, "Message exceeds the configured analysis input limit.", false, null, null, cancellationToken);
            return true;
        }

        EmailAnalyzerResult result;
        try
        {
            result = await analyzer.AnalyzeAsync(claim.Message, cancellationToken);
        }
        catch (EmailAnalysisException exception)
        {
            var retryable = exception.Retryable && claim.Message.ProcessingAttemptCount < settings.MaxAttempts;
            var safeMessage = retryable || exception.Retryable
                ? "The email analysis provider could not complete the request."
                : exception.Message;
            await FailAsync(
                claim,
                exception.FailureKind,
                safeMessage,
                retryable,
                retryable ? timeProvider.GetUtcNow().AddSeconds(settings.RetryDelaySeconds) : null,
                exception.Metrics,
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await FailAsync(
                claim,
                ProcessingFailureKind.Unknown,
                "Email processing failed unexpectedly.",
                false,
                null,
                null,
                cancellationToken);
            return true;
        }

        await repository.CompleteProcessingAsync(
            claim.Message.Id, leaseOwner, result.Classification, result.Metrics, cancellationToken);

        return true;
    }

    private Task<bool> FailAsync(
        InboundMessageProcessingClaim claim,
        ProcessingFailureKind failureKind,
        string safeMessage,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        EmailAnalysisMetrics? metrics,
        CancellationToken cancellationToken) =>
        repository.FailProcessingAsync(
            claim.Message.Id,
            claim.LeaseOwner,
            failureKind,
            safeMessage,
            retryable,
            nextAttemptAt,
            metrics,
            cancellationToken);
}

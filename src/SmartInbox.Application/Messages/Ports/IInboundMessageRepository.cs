using SmartInbox.Domain.Messages;
using SmartInbox.Application.Messages.Models;

namespace SmartInbox.Application.Messages.Ports;

public interface IInboundMessageRepository
{
    Task<AddInboundMessageResult> AddIfAbsentAsync(
        InboundMessage message,
        CancellationToken cancellationToken = default);

    Task<InboundMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<InboundMessagePage> ListAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<InboundMessageProcessingClaim?> ClaimNextForProcessingAsync(
        Guid leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteProcessingAsync(
        Guid id,
        Guid leaseOwner,
        EmailClassification classification,
        EmailAnalysisMetrics metrics,
        CancellationToken cancellationToken = default);

    Task<bool> FailProcessingAsync(
        Guid id,
        Guid leaseOwner,
        ProcessingFailureKind failureKind,
        string safeMessage,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        EmailAnalysisMetrics? metrics,
        CancellationToken cancellationToken = default);
}

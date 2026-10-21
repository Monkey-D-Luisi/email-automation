using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Common.Exceptions;

public sealed class EmailAnalysisException(
    string safeMessage,
    ProcessingFailureKind failureKind,
    bool retryable,
    EmailAnalysisMetrics? metrics = null,
    Exception? innerException = null) : Exception(safeMessage, innerException)
{
    public ProcessingFailureKind FailureKind { get; } = failureKind;
    public bool Retryable { get; } = retryable;
    public EmailAnalysisMetrics? Metrics { get; } = metrics;
}

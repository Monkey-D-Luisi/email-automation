namespace SmartInbox.Domain.Messages;

public enum ProcessingFailureKind
{
    TransientConnectivity,
    RateLimited,
    Timeout,
    Authentication,
    Configuration,
    InvalidStructuredOutput,
    UnsupportedProviderOrModel,
    ProviderRejected,
    Unknown,
    RetryLimitReached,
}

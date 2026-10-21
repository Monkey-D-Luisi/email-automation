namespace SmartInbox.Domain.Messages;

public enum EmailReviewReason
{
    UnknownCategory,
    AmbiguousContent,
    MissingInformation,
    ModelWarning,
    ConflictingFacts,
    UnsupportedFact,
    UnsupportedLanguage,
    UnsubstantiatedPriority,
    UrgentPriorityNotSupported,
}

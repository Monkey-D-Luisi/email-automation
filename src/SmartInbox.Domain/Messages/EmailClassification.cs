namespace SmartInbox.Domain.Messages;

public sealed class EmailClassification
{
    public EmailCategory Category { get; private set; }
    public EmailPriority Priority { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string Language { get; private set; } = string.Empty;
    public IReadOnlyList<EmailFact> Facts { get; private set; } = [];
    public IReadOnlyList<string> MissingInformation { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public IReadOnlyList<string> UncertaintyReasons { get; private set; } = [];
    public IReadOnlyList<EmailReviewReason> ReviewReasons { get; private set; } = [];
    public bool RequiresReview => ReviewReasons.Count > 0;
    public DateTimeOffset ClassifiedAt { get; private set; }

    private EmailClassification() { }

    public static EmailClassification Create(
        EmailCategory category,
        EmailPriority priority,
        string summary,
        string language,
        IEnumerable<EmailFact> facts,
        IEnumerable<string> missingInformation,
        IEnumerable<string> warnings,
        IEnumerable<string> uncertaintyReasons,
        IEnumerable<EmailReviewReason> reviewReasons,
        DateTimeOffset classifiedAt)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(missingInformation);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(uncertaintyReasons);
        ArgumentNullException.ThrowIfNull(reviewReasons);
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(priority));
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > InboundMessageLimits.ClassificationSummary)
            throw new ArgumentException("The classification summary is invalid.", nameof(summary));
        if (language is not ("en" or "es" or "unknown"))
            throw new ArgumentException("The classification language is unsupported.", nameof(language));

        var normalizedFacts = facts.ToArray();
        var normalizedMissing = ValidateStrings(missingInformation, InboundMessageLimits.ClassificationItems, InboundMessageLimits.ClassificationItem);
        var normalizedWarnings = ValidateStrings(warnings, InboundMessageLimits.ClassificationItems, InboundMessageLimits.ClassificationWarning);
        var normalizedUncertainty = ValidateStrings(uncertaintyReasons, InboundMessageLimits.ClassificationItems, InboundMessageLimits.ClassificationItem);
        var normalizedReviewReasons = reviewReasons.Distinct().ToArray();
        if (normalizedFacts.Length > InboundMessageLimits.ClassificationFacts ||
            normalizedFacts.Any(fact => fact is null || !Enum.IsDefined(fact.Type) ||
                string.IsNullOrWhiteSpace(fact.Value) || fact.Value.Length > InboundMessageLimits.ClassificationFactValue))
            throw new ArgumentException("The extracted facts are invalid.", nameof(facts));
        if (normalizedReviewReasons.Length > InboundMessageLimits.ClassificationItems ||
            normalizedReviewReasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentException("The review reasons are invalid.", nameof(reviewReasons));

        return new EmailClassification
        {
            Category = category,
            Priority = priority,
            Summary = summary.Trim(),
            Language = language,
            Facts = Array.AsReadOnly(normalizedFacts),
            MissingInformation = Array.AsReadOnly(normalizedMissing),
            Warnings = Array.AsReadOnly(normalizedWarnings),
            UncertaintyReasons = Array.AsReadOnly(normalizedUncertainty),
            ReviewReasons = Array.AsReadOnly(normalizedReviewReasons),
            ClassifiedAt = classifiedAt.ToUniversalTime(),
        };
    }

    private static string[] ValidateStrings(IEnumerable<string> values, int maxCount, int maxLength)
    {
        var result = values.ToArray();
        if (result.Length > maxCount || result.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > maxLength))
            throw new ArgumentException("The classification text values are invalid.");
        return result.Select(value => value.Trim()).ToArray();
    }
}

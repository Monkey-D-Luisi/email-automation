using System.Text.RegularExpressions;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Classification;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Classification.Policies;

[IoC(typeof(EmailAnalysisPolicy), IoCLifetime.Scoped)]
public sealed class EmailAnalysisPolicy
{
    private static readonly string[] ElevatedPriorityTerms =
    [
        "urgent", "urgently", "asap", "immediately", "critical", "high priority", "emergency",
        "danger", "safety", "cannot operate", "system is down", "outage", "urgente", "inmediato",
        "inmediatamente", "crítico", "prioridad alta", "emergencia", "peligro", "seguridad",
        "no podemos operar", "no puedo operar", "caído", "fuera de servicio",
    ];

    private static readonly string[] UrgentPriorityTerms =
    [
        "urgent", "urgente", "emergency", "danger", "safety", "system is down", "outage", "immediately", "emergencia",
        "inmediatamente", "peligro", "seguridad", "cannot operate", "no podemos operar", "no puedo operar", "caído", "fuera de servicio",
    ];

    private static readonly string[] NegatablePriorityTerms =
    [
        "urgent", "urgently", "asap", "immediately", "critical", "high priority", "emergency", "danger", "safety",
        "urgente", "inmediato", "inmediatamente", "crítico", "prioridad alta", "emergencia", "peligro", "seguridad",
    ];

    private static readonly char[] PriorityEvidenceBoundaries = ['.', '!', '?', ';', ':', '\r', '\n'];

    private static readonly Regex NegatedPriorityPattern = new(
        $@"(?:\b(?:not|never|isn['’]?t|wasn['’]?t|aren['’]?t|weren['’]?t|no\s+longer|nunca)\b[^.!?;:]{{0,32}}\b(?:{string.Join("|", NegatablePriorityTerms.Select(Regex.Escape))})\b|\bno\s+(?:(?:es|está|esta|parece|resulta|era|fue)\s+[^.!?;:]{{0,32}})?(?:{string.Join("|", NegatablePriorityTerms.Select(Regex.Escape))})\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public EmailClassification Apply(
        InboundMessage message,
        EmailAnalysisCandidate candidate,
        DateTimeOffset classifiedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateCandidate(candidate);

        var reviewReasons = new HashSet<EmailReviewReason>();
        if (!TryParseEnumName(candidate.Category, out EmailCategory category))
        {
            category = EmailCategory.Unknown;
            reviewReasons.Add(EmailReviewReason.UnknownCategory);
        }
        if (category == EmailCategory.Unknown)
            reviewReasons.Add(EmailReviewReason.UnknownCategory);
        if (!TryParseEnumName(candidate.SuggestedPriority, out EmailPriority priority))
            throw InvalidCandidate("The model returned an unsupported priority.");

        var language = candidate.Language.Trim().ToLowerInvariant();
        if (language is not ("en" or "es" or "unknown"))
        {
            language = "unknown";
            reviewReasons.Add(EmailReviewReason.UnsupportedLanguage);
        }

        if (candidate.IsAmbiguous)
            reviewReasons.Add(EmailReviewReason.AmbiguousContent);
        if (candidate.MissingInformation.Count > 0)
            reviewReasons.Add(EmailReviewReason.MissingInformation);
        if (candidate.Warnings.Count > 0)
            reviewReasons.Add(EmailReviewReason.ModelWarning);

        var sourceText = $"{message.Subject}\n{message.BodyText}";
        var facts = ValidateFacts(candidate.Facts, sourceText, reviewReasons);
        priority = ValidatePriority(priority, candidate.PriorityEvidence, sourceText, reviewReasons);

        var uncertaintyReasons = candidate.UncertaintyReasons
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (uncertaintyReasons.Length > 0)
            reviewReasons.Add(EmailReviewReason.AmbiguousContent);

        return EmailClassification.Create(
            category,
            priority,
            candidate.Summary.Trim(),
            language,
            facts,
            candidate.MissingInformation.Select(value => value.Trim()),
            candidate.Warnings.Select(value => value.Trim()),
            uncertaintyReasons,
            reviewReasons,
            classifiedAt);
    }

    private static EmailPriority ValidatePriority(
        EmailPriority suggested,
        string? evidence,
        string sourceText,
        ISet<EmailReviewReason> reviewReasons)
    {
        if (suggested is not (EmailPriority.High or EmailPriority.Urgent))
            return suggested;

        var quote = evidence?.Trim();
        var isSourceQuote = !string.IsNullOrWhiteSpace(quote) &&
            sourceText.Contains(quote, StringComparison.OrdinalIgnoreCase);
        var hasElevatedSignal = isSourceQuote && ElevatedPriorityTerms.Any(term => quote!.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (!hasElevatedSignal || HasNegatedPriorityContext(sourceText, quote!))
        {
            reviewReasons.Add(EmailReviewReason.UnsubstantiatedPriority);
            return EmailPriority.Normal;
        }

        if (suggested == EmailPriority.Urgent &&
            !UrgentPriorityTerms.Any(term => quote!.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            reviewReasons.Add(EmailReviewReason.UrgentPriorityNotSupported);
            return EmailPriority.High;
        }

        return suggested;
    }

    private static bool HasNegatedPriorityContext(string sourceText, string quote)
    {
        var searchFrom = 0;
        while (searchFrom < sourceText.Length)
        {
            var matchIndex = sourceText.IndexOf(quote, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                return false;

            var previousBoundary = sourceText.LastIndexOfAny(PriorityEvidenceBoundaries, matchIndex);
            var nextBoundary = sourceText.IndexOfAny(PriorityEvidenceBoundaries, matchIndex + quote.Length);
            var contextStart = previousBoundary < 0 ? 0 : previousBoundary + 1;
            var contextEnd = nextBoundary < 0 ? sourceText.Length : nextBoundary;
            if (NegatedPriorityPattern.IsMatch(sourceText[contextStart..contextEnd]))
                return true;

            searchFrom = matchIndex + quote.Length;
        }

        return false;
    }

    private static IReadOnlyList<EmailFact> ValidateFacts(
        IReadOnlyList<EmailFactCandidate> candidates,
        string sourceText,
        ISet<EmailReviewReason> reviewReasons)
    {
        var valid = new List<EmailFact>();
        foreach (var candidate in candidates)
        {
            if (!TryParseEnumName(candidate.Type, out EmailFactType type))
                throw InvalidCandidate("The model returned an unsupported fact type.");
            if (!sourceText.Contains(candidate.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                reviewReasons.Add(EmailReviewReason.UnsupportedFact);
                continue;
            }
            if (RequiresBusinessFactReview(type))
                reviewReasons.Add(EmailReviewReason.UnverifiedBusinessFact);
            valid.Add(new EmailFact(type, candidate.Value.Trim()));
        }

        var result = new List<EmailFact>();
        foreach (var group in valid.GroupBy(fact => fact.Type))
        {
            var values = group.Select(fact => fact.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length > 1)
            {
                reviewReasons.Add(EmailReviewReason.ConflictingFacts);
                continue;
            }
            result.Add(group.First());
        }
        return result;
    }

    private static void ValidateCandidate(EmailAnalysisCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Category) || candidate.Category.Length > 32 ||
            string.IsNullOrWhiteSpace(candidate.SuggestedPriority) || candidate.SuggestedPriority.Length > 32 ||
            string.IsNullOrWhiteSpace(candidate.Summary) || candidate.Summary.Length > InboundMessageLimits.ClassificationSummary ||
            string.IsNullOrWhiteSpace(candidate.Language) || candidate.Language.Length > 16 ||
            candidate.Facts is null || candidate.Facts.Count > InboundMessageLimits.ClassificationFacts || candidate.Facts.Any(fact =>
                fact is null || string.IsNullOrWhiteSpace(fact.Type) || fact.Type.Length > 32 ||
                string.IsNullOrWhiteSpace(fact.Value) || fact.Value.Length > InboundMessageLimits.ClassificationFactValue) ||
            !ValidTextValues(candidate.MissingInformation, InboundMessageLimits.ClassificationItems, InboundMessageLimits.ClassificationItem) ||
            !ValidTextValues(candidate.Warnings, InboundMessageLimits.ClassificationItems, InboundMessageLimits.ClassificationWarning) ||
            !ValidTextValues(candidate.UncertaintyReasons, InboundMessageLimits.ClassificationItems, InboundMessageLimits.ClassificationItem) ||
            candidate.PriorityEvidence?.Length > InboundMessageLimits.ClassificationWarning)
            throw InvalidCandidate("The model returned an invalid structured analysis.");
    }

    private static bool ValidTextValues(IReadOnlyList<string>? values, int maxCount, int maxLength) =>
        values is not null && values.Count <= maxCount &&
        values.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength);

    private static bool TryParseEnumName<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var enumName = value.Trim();
        if (Enum.TryParse(enumName, ignoreCase: true, out parsed) && Enum.IsDefined(parsed) &&
            string.Equals(parsed.ToString(), enumName, StringComparison.OrdinalIgnoreCase))
            return true;

        parsed = default;
        return false;
    }

    private static bool RequiresBusinessFactReview(EmailFactType type) => type is
        EmailFactType.InvoiceNumber or
        EmailFactType.Amount or
        EmailFactType.Currency or
        EmailFactType.OrderReference or
        EmailFactType.RequestedDate;

    private static EmailAnalysisException InvalidCandidate(string message) =>
        new(message, ProcessingFailureKind.InvalidStructuredOutput, retryable: false);
}

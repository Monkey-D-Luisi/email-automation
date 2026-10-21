using SmartInbox.Application.Messages.Classification;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification.Policies;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Tests.Messages.Classification;

public sealed class EmailAnalysisPolicyTests
{
    private static readonly EmailAnalysisPolicy Policy = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("SalesInquiry", EmailCategory.SalesInquiry, false)]
    [InlineData("SupportRequest", EmailCategory.SupportRequest, false)]
    [InlineData("BillingDocument", EmailCategory.BillingDocument, false)]
    [InlineData("GeneralInquiry", EmailCategory.GeneralInquiry, false)]
    [InlineData("Irrelevant", EmailCategory.Irrelevant, false)]
    [InlineData("Unknown", EmailCategory.Unknown, true)]
    public void Apply_preserves_each_supported_category(string category, EmailCategory expected, bool requiresReview)
    {
        var result = Apply(Candidate(category: category));

        Assert.Equal(expected, result.Category);
        Assert.Equal(requiresReview, result.RequiresReview);
    }

    [Theory]
    [InlineData("Low", null, EmailPriority.Low)]
    [InlineData("Normal", null, EmailPriority.Normal)]
    [InlineData("High", "Please treat this as high priority.", EmailPriority.High)]
    [InlineData("Urgent", "This is urgent because the system is down.", EmailPriority.Urgent)]
    public void Apply_accepts_only_validated_priority(string suggested, string? evidence, EmailPriority expected)
    {
        var message = Message(evidence ?? "Routine request.");
        var result = Policy.Apply(message, Candidate(priority: suggested, evidence: evidence), Now);

        Assert.Equal(expected, result.Priority);
        Assert.False(result.RequiresReview);
    }

    [Theory]
    [InlineData("This is not urgent.")]
    [InlineData("No es urgente.")]
    [InlineData("This is urgent, but it is not urgent.")]
    public void Apply_downgrades_negated_or_conflicting_urgent_evidence(string statement)
    {
        var result = Policy.Apply(
            Message(statement),
            Candidate(priority: "Urgent", evidence: statement),
            Now);

        Assert.Equal(EmailPriority.Normal, result.Priority);
        Assert.Contains(EmailReviewReason.UnsubstantiatedPriority, result.ReviewReasons);
        Assert.True(result.RequiresReview);
    }

    [Theory]
    [InlineData("This is not urgent.", "Urgent", "urgent")]
    [InlineData("This is not high priority.", "High", "This is not high priority.")]
    [InlineData("No urgente.", "Urgent", "urgente")]
    public void Apply_downgrades_elevated_priority_when_source_context_negates_quoted_evidence(
        string source,
        string priority,
        string evidence)
    {
        var result = Policy.Apply(Message(source), Candidate(priority: priority, evidence: evidence), Now);

        Assert.Equal(EmailPriority.Normal, result.Priority);
        Assert.Contains(EmailReviewReason.UnsubstantiatedPriority, result.ReviewReasons);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public void Apply_downgrades_elevated_priority_without_verbatim_evidence_and_requires_review()
    {
        var result = Apply(Candidate(priority: "Urgent", evidence: "urgent and critical"));

        Assert.Equal(EmailPriority.Normal, result.Priority);
        Assert.Contains(EmailReviewReason.UnsubstantiatedPriority, result.ReviewReasons);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public void Apply_downgrades_urgent_to_high_when_only_high_priority_evidence_is_present()
    {
        var result = Policy.Apply(
            Message("Please treat this as high priority."),
            Candidate(priority: "Urgent", evidence: "Please treat this as high priority."),
            Now);

        Assert.Equal(EmailPriority.High, result.Priority);
        Assert.Contains(EmailReviewReason.UrgentPriorityNotSupported, result.ReviewReasons);
    }

    [Fact]
    public void Apply_marks_unknown_category_for_review()
    {
        var result = Apply(Candidate(category: "FutureCategory"));

        Assert.Equal(EmailCategory.Unknown, result.Category);
        Assert.Contains(EmailReviewReason.UnknownCategory, result.ReviewReasons);
    }

    [Theory]
    [InlineData("0")]
    public void Apply_does_not_accept_numeric_category_values(string category)
    {
        var result = Apply(Candidate(category: category));

        Assert.Equal(EmailCategory.Unknown, result.Category);
        Assert.Contains(EmailReviewReason.UnknownCategory, result.ReviewReasons);
    }

    [Theory]
    [InlineData("3")]
    public void Apply_rejects_numeric_priority_values(string priority)
    {
        var exception = Assert.Throws<EmailAnalysisException>(() => Apply(Candidate(priority: priority)));

        Assert.Equal(ProcessingFailureKind.InvalidStructuredOutput, exception.FailureKind);
    }

    [Theory]
    [InlineData("10")]
    public void Apply_rejects_numeric_fact_type_values(string factType)
    {
        var exception = Assert.Throws<EmailAnalysisException>(() => Apply(Candidate(facts:
        [
            new EmailFactCandidate(factType, "INV-123"),
        ])));

        Assert.Equal(ProcessingFailureKind.InvalidStructuredOutput, exception.FailureKind);
    }

    [Fact]
    public void Apply_preserves_whitespace_trimming_for_named_enum_values()
    {
        var result = Policy.Apply(
            Message("Routine business question mentioning INV-123."),
            Candidate(category: " SalesInquiry ", priority: " Normal ", facts:
            [
                new EmailFactCandidate(" InvoiceNumber ", "INV-123"),
            ]),
            Now);

        Assert.Equal(EmailCategory.SalesInquiry, result.Category);
        Assert.Equal(EmailPriority.Normal, result.Priority);
        Assert.Equal(EmailFactType.InvoiceNumber, Assert.Single(result.Facts).Type);
    }

    [Fact]
    public void Apply_rejects_unknown_priority_and_oversized_candidates()
    {
        var badPriority = Assert.Throws<EmailAnalysisException>(() => Apply(Candidate(priority: "Critical")));
        var oversizedFacts = Assert.Throws<EmailAnalysisException>(() => Apply(Candidate(facts: Enumerable.Range(0, 21)
            .Select(index => new EmailFactCandidate("Other", $"Fact {index}"))
            .ToArray())));

        Assert.Equal(ProcessingFailureKind.InvalidStructuredOutput, badPriority.FailureKind);
        Assert.False(badPriority.Retryable);
        Assert.Equal(ProcessingFailureKind.InvalidStructuredOutput, oversizedFacts.FailureKind);
    }

    [Fact]
    public void Apply_drops_conflicting_facts_and_marks_review()
    {
        const string source = "Our companies Acme and Contoso need service.";
        var result = Policy.Apply(Message(source), Candidate(facts:
        [
            new EmailFactCandidate("CompanyName", "Acme"),
            new EmailFactCandidate("CompanyName", "Contoso"),
        ]), Now);

        Assert.Empty(result.Facts);
        Assert.Contains(EmailReviewReason.ConflictingFacts, result.ReviewReasons);
    }

    [Fact]
    public void Apply_drops_facts_not_found_in_source_text()
    {
        var result = Policy.Apply(Message("Please send the invoice for 500 euros."), Candidate(facts:
        [
            new EmailFactCandidate("Amount", "500 euros"),
            new EmailFactCandidate("InvoiceNumber", "INV-999"),
        ]), Now);

        Assert.Single(result.Facts);
        Assert.Equal("500 euros", result.Facts[0].Value);
        Assert.Contains(EmailReviewReason.UnsupportedFact, result.ReviewReasons);
    }

    [Fact]
    public void Apply_marks_business_facts_for_human_review_even_when_the_value_appears_in_source()
    {
        var result = Policy.Apply(
            Message("Please call extension 123."),
            Candidate(facts: [new EmailFactCandidate("InvoiceNumber", "123")]),
            Now);

        Assert.Equal(EmailFactType.InvoiceNumber, Assert.Single(result.Facts).Type);
        Assert.True(result.RequiresReview);
        Assert.Contains(EmailReviewReason.UnverifiedBusinessFact, result.ReviewReasons);
    }

    [Fact]
    public void Apply_records_missing_information_warnings_and_uncertainty()
    {
        var result = Apply(Candidate(
            missing: ["Requested delivery date"],
            warnings: ["Two products were mentioned."],
            ambiguous: true,
            uncertainty: ["The requested item is unclear."]));

        Assert.True(result.RequiresReview);
        Assert.Contains(EmailReviewReason.MissingInformation, result.ReviewReasons);
        Assert.Contains(EmailReviewReason.ModelWarning, result.ReviewReasons);
        Assert.Contains(EmailReviewReason.AmbiguousContent, result.ReviewReasons);
        Assert.Equal("The requested item is unclear.", Assert.Single(result.UncertaintyReasons));
    }

    [Fact]
    public void Apply_normalizes_unsupported_language_to_unknown_and_requires_review()
    {
        var result = Apply(Candidate(language: "fr"));

        Assert.Equal("unknown", result.Language);
        Assert.Contains(EmailReviewReason.UnsupportedLanguage, result.ReviewReasons);
    }

    private static EmailClassification Apply(EmailAnalysisCandidate candidate) =>
        Policy.Apply(Message("Routine business question."), candidate, Now);

    private static InboundMessage Message(string body) => InboundMessage.Receive(
        "test", "mailbox", Guid.NewGuid().ToString("N"), null, null, null, "Synthetic subject", body, Now, Now);

    private static EmailAnalysisCandidate Candidate(
        string category = "GeneralInquiry",
        string priority = "Normal",
        string language = "en",
        IReadOnlyList<EmailFactCandidate>? facts = null,
        IReadOnlyList<string>? missing = null,
        IReadOnlyList<string>? warnings = null,
        bool ambiguous = false,
        IReadOnlyList<string>? uncertainty = null,
        string? evidence = null) => new(
        category,
        priority,
        "A concise summary.",
        language,
        facts ?? [],
        missing ?? [],
        warnings ?? [],
        ambiguous,
        uncertainty ?? [],
        evidence);
}

using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Models;

public sealed record DraftProposalContext(
    string Subject,
    string Body,
    string Language,
    EmailCategory OriginalCategory,
    EmailCategory ReviewedCategory,
    EmailPriority ReviewedPriority,
    IReadOnlyList<EmailFact> VerifiedFacts,
    IReadOnlyList<string> MissingInformation,
    bool HasUnverifiedFacts);

public sealed record DraftProposalResult(string Body, EmailAnalysisMetrics Metrics);

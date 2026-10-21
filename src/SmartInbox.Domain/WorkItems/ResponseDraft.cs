using System.Net.Mail;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Domain.WorkItems;

public sealed class ResponseDraft
{
    public Guid Id { get; private set; }
    public string RecipientEmail { get; private set; } = string.Empty;
    public IReadOnlyList<ResponseDraftRevision> Revisions { get; private set; } = [];
    public ResponseDraftRevision CurrentRevision => Revisions[^1];

    private ResponseDraft() { }

    internal static ResponseDraft Create(
        string recipientEmail,
        string subject,
        string body,
        string reviewerId,
        DateTimeOffset now,
        EmailAnalysisMetrics? generationMetrics)
    {
        var recipient = ValidateRecipient(recipientEmail);
        return new ResponseDraft
        {
            Id = Guid.NewGuid(),
            RecipientEmail = recipient,
            Revisions = [ResponseDraftRevision.Create(1, subject, body, reviewerId, now, generationMetrics)],
        };
    }

    public static ResponseDraft Restore(Guid id, string recipientEmail, IEnumerable<ResponseDraftRevision> revisions)
    {
        var restored = revisions?.OrderBy(item => item.Revision).ToArray()
            ?? throw new ArgumentNullException(nameof(revisions));
        if (id == Guid.Empty || restored.Length == 0 ||
            restored.Select((revision, index) => revision.Revision == index + 1).Any(isSequential => !isSequential))
            throw new ArgumentException("The persisted response draft is invalid.");
        return new ResponseDraft
        {
            Id = id,
            RecipientEmail = ValidateRecipient(recipientEmail),
            Revisions = Array.AsReadOnly(restored),
        };
    }

    internal void AddRevision(string subject, string body, string reviewerId, DateTimeOffset now, EmailAnalysisMetrics? generationMetrics) =>
        Revisions = Array.AsReadOnly(Revisions.Append(
            ResponseDraftRevision.Create(CurrentRevision.Revision + 1, subject, body, reviewerId, now, generationMetrics)).ToArray());

    internal void Approve(int revision, string reviewerId, DateTimeOffset now)
    {
        EnsureCurrentRevision(revision);
        CurrentRevision.Approve(reviewerId, now);
    }

    internal void Reject(int revision, string reviewerId, string? feedback, DateTimeOffset now)
    {
        EnsureCurrentRevision(revision);
        CurrentRevision.Reject(reviewerId, feedback, now);
    }

    private void EnsureCurrentRevision(int revision)
    {
        if (CurrentRevision.Revision != revision)
            throw new InvalidOperationException("The response draft revision is stale.");
    }

    private static string ValidateRecipient(string recipientEmail)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail) || recipientEmail.Length > 320 ||
            !MailAddress.TryCreate(recipientEmail, out var address) || address is null)
            throw new ArgumentException("A valid inbound sender address is required for a response draft.", nameof(recipientEmail));
        return address.Address;
    }
}

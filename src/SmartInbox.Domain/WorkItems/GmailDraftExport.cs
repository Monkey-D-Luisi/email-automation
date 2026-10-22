using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmartInbox.Domain.WorkItems;

public sealed class GmailDraftExport
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public Guid WorkItemId { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid DraftRevisionId { get; private set; }
    public int ApprovedRevision { get; private set; }
    public string DestinationMailboxId { get; private set; } = string.Empty;
    public string RecipientEmail { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string ContentFingerprint { get; private set; } = string.Empty;
    public DraftExportProvider Provider { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public GmailDraftExportStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid? AttemptId { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? ExternalDraftId { get; private set; }
    public string? GmailDraftId => Provider == DraftExportProvider.Gmail ? ExternalDraftId : null;
    public GmailDraftExportFailureKind? FailureKind { get; private set; }

    private GmailDraftExport() { }

    public static GmailDraftExport Create(
        WorkItem workItem,
        string destinationMailboxId,
        string confirmedRecipientEmail,
        string reviewerId,
        DateTimeOffset now,
        DraftExportProvider provider = DraftExportProvider.Gmail)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        if (!Enum.IsDefined(provider))
            throw new ArgumentOutOfRangeException(nameof(provider));
        if (string.IsNullOrWhiteSpace(reviewerId) || reviewerId.Length > 128)
            throw new ArgumentException("A reviewer identity is required.", nameof(reviewerId));
        if (string.IsNullOrWhiteSpace(destinationMailboxId) || destinationMailboxId.Length > 256 ||
            destinationMailboxId.Contains('\r') || destinationMailboxId.Contains('\n'))
            throw new ArgumentException("The destination mailbox identity is invalid.", nameof(destinationMailboxId));

        workItem.EnsureEditableBy(reviewerId);
        var draft = workItem.Draft ?? throw new InvalidOperationException("A response draft is required before external export.");
        var revision = draft.CurrentRevision;
        if (revision.Status != ResponseDraftStatus.Approved)
            throw new InvalidOperationException("The current response draft revision must be approved before external export.");
        if (revision.Subject.Contains('\r') || revision.Subject.Contains('\n'))
            throw new ArgumentException("The draft subject cannot contain line breaks.", nameof(workItem));

        if (!MailAddress.TryCreate(confirmedRecipientEmail, out var recipient) || recipient is null ||
            !string.Equals(recipient.Address, draft.RecipientEmail, StringComparison.Ordinal))
            throw new ArgumentException("Confirm the recipient already saved on the internal response draft.", nameof(confirmedRecipientEmail));

        var timestamp = now.ToUniversalTime();
        return new GmailDraftExport
        {
            Id = Guid.NewGuid(),
            WorkItemId = workItem.Id,
            DraftId = draft.Id,
            DraftRevisionId = revision.Id,
            ApprovedRevision = revision.Revision,
            DestinationMailboxId = destinationMailboxId,
            RecipientEmail = recipient.Address,
            Subject = revision.Subject,
            Body = revision.Body,
            ContentFingerprint = Fingerprint(
                draft.Id,
                revision.Id,
                revision.Revision,
                destinationMailboxId,
                recipient.Address,
                revision.Subject,
                revision.Body),
            Provider = provider,
            RequestedBy = reviewerId,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            Status = GmailDraftExportStatus.Pending,
        };
    }

    public static GmailDraftExport Restore(
        Guid id,
        Guid workItemId,
        Guid draftId,
        Guid draftRevisionId,
        int approvedRevision,
        string destinationMailboxId,
        string recipientEmail,
        string subject,
        string body,
        string contentFingerprint,
        string requestedBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt,
        GmailDraftExportStatus status,
        int attemptCount,
        Guid? attemptId,
        DateTimeOffset? claimedAt,
        DateTimeOffset? leaseExpiresAt,
        string? externalDraftId,
        GmailDraftExportFailureKind? failureKind,
        DraftExportProvider provider = DraftExportProvider.Gmail)
    {
        if (id == Guid.Empty || workItemId == Guid.Empty || draftId == Guid.Empty || draftRevisionId == Guid.Empty ||
            approvedRevision < 1 || string.IsNullOrWhiteSpace(destinationMailboxId) || destinationMailboxId.Length > 256 ||
            !MailAddress.TryCreate(recipientEmail, out var recipient) || recipient is null ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 998 || subject.Contains('\r') || subject.Contains('\n') ||
            string.IsNullOrWhiteSpace(body) || body.Length > 20_000 ||
            string.IsNullOrWhiteSpace(requestedBy) || requestedBy.Length > 128 || attemptCount < 0 ||
            !Enum.IsDefined(status) || !Enum.IsDefined(provider) || (failureKind is not null && !Enum.IsDefined(failureKind.Value)) ||
            (contentFingerprint.Length != 64 || !contentFingerprint.All(Uri.IsHexDigit)))
            throw new ArgumentException("The persisted Gmail draft export is invalid.");

        var expectedFingerprint = Fingerprint(
            draftId, draftRevisionId, approvedRevision, destinationMailboxId,
            recipient.Address, subject, body);
        if (!string.Equals(contentFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The persisted Gmail draft export fingerprint does not match its snapshot.");

        var hasAttempt = attemptId is not null;
        var hasLease = claimedAt is not null && leaseExpiresAt is not null;
        var stateIsValid = status switch
        {
            GmailDraftExportStatus.Pending => !hasLease && externalDraftId is null && completedAt is null,
            GmailDraftExportStatus.InProgress => hasAttempt && hasLease && externalDraftId is null && completedAt is null,
            GmailDraftExportStatus.Succeeded => hasAttempt && !hasLease && completedAt is not null && !string.IsNullOrWhiteSpace(externalDraftId) && failureKind is null,
            GmailDraftExportStatus.Failed or GmailDraftExportStatus.NeedsReconciliation => hasAttempt && !hasLease && completedAt is not null && failureKind is not null && externalDraftId is null,
            GmailDraftExportStatus.Cancelled => !hasLease && completedAt is not null && externalDraftId is null,
            _ => false,
        };
        if (!stateIsValid)
            throw new ArgumentException("The persisted Gmail draft export state is inconsistent.");

        return new GmailDraftExport
        {
            Id = id,
            WorkItemId = workItemId,
            DraftId = draftId,
            DraftRevisionId = draftRevisionId,
            ApprovedRevision = approvedRevision,
            DestinationMailboxId = destinationMailboxId,
            RecipientEmail = recipient.Address,
            Subject = subject,
            Body = body,
            ContentFingerprint = expectedFingerprint,
            Provider = provider,
            RequestedBy = requestedBy,
            CreatedAt = createdAt.ToUniversalTime(),
            UpdatedAt = updatedAt.ToUniversalTime(),
            CompletedAt = completedAt?.ToUniversalTime(),
            Status = status,
            AttemptCount = attemptCount,
            AttemptId = attemptId,
            ClaimedAt = claimedAt?.ToUniversalTime(),
            LeaseExpiresAt = leaseExpiresAt?.ToUniversalTime(),
            ExternalDraftId = externalDraftId,
            FailureKind = failureKind,
        };
    }

    public void Claim(Guid attemptId, DateTimeOffset now, TimeSpan leaseDuration)
    {
        if (Status != GmailDraftExportStatus.Pending)
            throw new InvalidOperationException("Only a pending Gmail draft export can be claimed.");
        if (attemptId == Guid.Empty || leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
            throw new ArgumentException("The Gmail draft export lease is invalid.", nameof(leaseDuration));

        var timestamp = now.ToUniversalTime();
        Status = GmailDraftExportStatus.InProgress;
        AttemptCount = checked(AttemptCount + 1);
        AttemptId = attemptId;
        ClaimedAt = timestamp;
        LeaseExpiresAt = timestamp.Add(leaseDuration);
        CompletedAt = null;
        ExternalDraftId = null;
        FailureKind = null;
        UpdatedAt = timestamp;
    }

    public GmailDraftExportTransition Complete(Guid attemptId, string gmailDraftId, DateTimeOffset now)
    {
        if (Status == GmailDraftExportStatus.Succeeded && AttemptId == attemptId &&
            string.Equals(ExternalDraftId, gmailDraftId, StringComparison.Ordinal))
            return GmailDraftExportTransition.Duplicate;
        if (Status != GmailDraftExportStatus.InProgress || AttemptId != attemptId)
            return GmailDraftExportTransition.Stale;
        if (string.IsNullOrWhiteSpace(gmailDraftId) || gmailDraftId.Length > 512)
            throw new ArgumentException("The Gmail draft identifier is invalid.", nameof(gmailDraftId));

        var timestamp = now.ToUniversalTime();
        if (LeaseExpiresAt <= timestamp)
        {
            MarkNeedsReconciliation(timestamp);
            return GmailDraftExportTransition.LeaseExpired;
        }

        Status = GmailDraftExportStatus.Succeeded;
        ExternalDraftId = gmailDraftId;
        CompletedAt = timestamp;
        ClaimedAt = null;
        LeaseExpiresAt = null;
        UpdatedAt = timestamp;
        return GmailDraftExportTransition.Applied;
    }

    public GmailDraftExportTransition Fail(Guid attemptId, GmailDraftExportFailureKind failureKind, DateTimeOffset now)
    {
        if (!Enum.IsDefined(failureKind))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        if (Status is GmailDraftExportStatus.Failed or GmailDraftExportStatus.NeedsReconciliation &&
            AttemptId == attemptId && FailureKind == failureKind)
            return GmailDraftExportTransition.Duplicate;
        if (Status != GmailDraftExportStatus.InProgress || AttemptId != attemptId)
            return GmailDraftExportTransition.Stale;

        var timestamp = now.ToUniversalTime();
        if (LeaseExpiresAt <= timestamp)
        {
            MarkNeedsReconciliation(timestamp);
            return GmailDraftExportTransition.LeaseExpired;
        }

        Status = failureKind == GmailDraftExportFailureKind.ProviderNotStarted
            ? GmailDraftExportStatus.Failed
            : GmailDraftExportStatus.NeedsReconciliation;
        FailureKind = failureKind;
        CompletedAt = timestamp;
        ClaimedAt = null;
        LeaseExpiresAt = null;
        UpdatedAt = timestamp;
        return GmailDraftExportTransition.Applied;
    }

    public bool ExpireLease(DateTimeOffset now)
    {
        var timestamp = now.ToUniversalTime();
        if (Status != GmailDraftExportStatus.InProgress || LeaseExpiresAt > timestamp)
            return false;
        MarkNeedsReconciliation(timestamp);
        return true;
    }

    public void Reconcile(string gmailDraftId, DateTimeOffset now)
    {
        if (Status is not (GmailDraftExportStatus.Failed or GmailDraftExportStatus.NeedsReconciliation))
            throw new InvalidOperationException("Only an unresolved Gmail draft export can be reconciled.");
        if (string.IsNullOrWhiteSpace(gmailDraftId) || gmailDraftId.Length > 512)
            throw new ArgumentException("The Gmail draft identifier is invalid.", nameof(gmailDraftId));

        Status = GmailDraftExportStatus.Succeeded;
        ExternalDraftId = gmailDraftId;
        FailureKind = null;
        CompletedAt = now.ToUniversalTime();
        UpdatedAt = now.ToUniversalTime();
    }

    public void RequeueAfterManualCheck(bool confirmedNoDraftExists, DateTimeOffset now)
    {
        if (!confirmedNoDraftExists)
            throw new ArgumentException("Confirm that no Gmail draft exists before retrying.", nameof(confirmedNoDraftExists));
        if (Status is not (GmailDraftExportStatus.Failed or GmailDraftExportStatus.NeedsReconciliation))
            throw new InvalidOperationException("Only a failed or unresolved Gmail draft export can be retried.");

        Status = GmailDraftExportStatus.Pending;
        FailureKind = null;
        CompletedAt = null;
        ClaimedAt = null;
        LeaseExpiresAt = null;
        ExternalDraftId = null;
        UpdatedAt = now.ToUniversalTime();
    }

    public void CancelPending(DateTimeOffset now)
    {
        if (Status != GmailDraftExportStatus.Pending)
            throw new InvalidOperationException("Only a pending Gmail draft export can be cancelled.");
        Status = GmailDraftExportStatus.Cancelled;
        CompletedAt = now.ToUniversalTime();
        UpdatedAt = now.ToUniversalTime();
    }

    private void MarkNeedsReconciliation(DateTimeOffset timestamp)
    {
        Status = GmailDraftExportStatus.NeedsReconciliation;
        FailureKind = GmailDraftExportFailureKind.ProviderOutcomeUnknown;
        CompletedAt = timestamp;
        ClaimedAt = null;
        LeaseExpiresAt = null;
        ExternalDraftId = null;
        UpdatedAt = timestamp;
    }

    private static string Fingerprint(
        Guid draftId,
        Guid revisionId,
        int revision,
        string mailboxId,
        string recipientEmail,
        string subject,
        string body)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            draftId,
            revisionId,
            revision,
            mailboxId,
            recipientEmail,
            subject,
            body,
        }, FingerprintJsonOptions);
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}

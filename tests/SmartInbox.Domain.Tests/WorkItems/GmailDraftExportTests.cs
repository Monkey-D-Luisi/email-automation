using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Domain.Tests.WorkItems;

public sealed class GmailDraftExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Request_pins_the_exact_current_approved_revision()
    {
        var item = ApprovedItem();

        var export = GmailDraftExport.Create(item, "gmail-test-mailbox", "alex@example.test", "reviewer-1", Now);
        item.SaveDraft("alex@example.test", "Re: Invoice edited", "A newer unapproved response.", "reviewer-1", Now.AddMinutes(1));

        Assert.Equal(1, export.ApprovedRevision);
        Assert.Equal("Re: Invoice", export.Subject);
        Assert.Equal("The approved response.", export.Body);
        Assert.Equal(GmailDraftExportStatus.Pending, export.Status);
        Assert.Equal(64, export.ContentFingerprint.Length);
        Assert.Equal(ResponseDraftStatus.Draft, item.Draft!.CurrentRevision.Status);
    }

    [Fact]
    public void Request_rejects_an_unapproved_current_revision_even_if_an_older_revision_was_approved()
    {
        var item = ApprovedItem();
        item.SaveDraft("alex@example.test", "Re: Invoice", "A newer response.", "reviewer-1", Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => GmailDraftExport.Create(
            item, "gmail-test-mailbox", "alex@example.test", "reviewer-1", Now.AddMinutes(2)));
    }

    [Fact]
    public void Request_requires_the_assigned_reviewer_and_the_saved_recipient()
    {
        var item = ApprovedItem();

        Assert.Throws<InvalidOperationException>(() => GmailDraftExport.Create(
            item, "gmail-test-mailbox", "alex@example.test", "reviewer-2", Now));
        Assert.Throws<ArgumentException>(() => GmailDraftExport.Create(
            item, "gmail-test-mailbox", "other@example.test", "reviewer-1", Now));
        Assert.Throws<ArgumentException>(() => GmailDraftExport.Create(
            item, "gmail-test-mailbox", "alex@example.test\r\nBcc:other@example.test", "reviewer-1", Now));
    }

    [Fact]
    public void Ambiguous_provider_failure_requires_manual_reconciliation()
    {
        var export = GmailDraftExport.Create(ApprovedItem(), "gmail-test-mailbox", "alex@example.test", "reviewer-1", Now);
        var attemptId = Guid.NewGuid();
        export.Claim(attemptId, Now, TimeSpan.FromMinutes(5));

        Assert.Equal(GmailDraftExportTransition.Applied, export.Fail(
            attemptId, GmailDraftExportFailureKind.ProviderOutcomeUnknown, Now.AddMinutes(1)));
        Assert.Equal(GmailDraftExportStatus.NeedsReconciliation, export.Status);
        Assert.Throws<InvalidOperationException>(() => export.Claim(Guid.NewGuid(), Now.AddMinutes(2), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Expired_lease_becomes_ambiguous_and_late_callback_cannot_overwrite_it()
    {
        var export = GmailDraftExport.Create(ApprovedItem(), "gmail-test-mailbox", "alex@example.test", "reviewer-1", Now);
        var attemptId = Guid.NewGuid();
        export.Claim(attemptId, Now, TimeSpan.FromMinutes(5));

        Assert.True(export.ExpireLease(Now.AddMinutes(6)));
        Assert.Equal(GmailDraftExportStatus.NeedsReconciliation, export.Status);
        Assert.Equal(GmailDraftExportTransition.Stale, export.Complete(attemptId, "gmail-draft-1", Now.AddMinutes(6)));
        Assert.Null(export.GmailDraftId);
    }

    [Fact]
    public void Repeated_success_callback_is_idempotent_and_conflicting_callback_is_stale()
    {
        var export = GmailDraftExport.Create(ApprovedItem(), "gmail-test-mailbox", "alex@example.test", "reviewer-1", Now);
        var attemptId = Guid.NewGuid();
        export.Claim(attemptId, Now, TimeSpan.FromMinutes(5));

        Assert.Equal(GmailDraftExportTransition.Applied, export.Complete(attemptId, "gmail-draft-1", Now.AddMinutes(1)));
        Assert.Equal(GmailDraftExportTransition.Duplicate, export.Complete(attemptId, "gmail-draft-1", Now.AddMinutes(2)));
        Assert.Equal(GmailDraftExportTransition.Stale, export.Complete(attemptId, "gmail-draft-2", Now.AddMinutes(2)));
        Assert.Equal("gmail-draft-1", export.GmailDraftId);
    }

    [Fact]
    public void Retry_requires_operator_confirmation_and_reconciliation_can_record_a_found_draft()
    {
        var export = GmailDraftExport.Create(ApprovedItem(), "gmail-test-mailbox", "alex@example.test", "reviewer-1", Now);
        var attemptId = Guid.NewGuid();
        export.Claim(attemptId, Now, TimeSpan.FromMinutes(5));
        export.Fail(attemptId, GmailDraftExportFailureKind.ProviderOutcomeUnknown, Now.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => export.RequeueAfterManualCheck(false, Now.AddMinutes(2)));
        export.Reconcile("gmail-draft-found", Now.AddMinutes(3));
        Assert.Equal(GmailDraftExportStatus.Succeeded, export.Status);
        Assert.Equal("gmail-draft-found", export.GmailDraftId);
    }

    private static WorkItem ApprovedItem()
    {
        var item = WorkItem.Create(Guid.NewGuid(), EmailClassification.Create(
            EmailCategory.GeneralInquiry,
            EmailPriority.Normal,
            "Synthetic classification.",
            "en",
            [],
            [],
            [],
            [],
            [],
            Now), Now);
        item.StartReview("reviewer-1", Now);
        item.SaveDraft("alex@example.test", "Re: Invoice", "The approved response.", "reviewer-1", Now.AddMinutes(1));
        item.ApproveDraft(1, "reviewer-1", Now.AddMinutes(2));
        return item;
    }
}
